"""#77 offline: real synthetic training, archived candidates, S3 recovery and replay."""

from datetime import UTC, datetime, timedelta
import json
from pathlib import Path
import socket
from uuid import uuid4
from unittest.mock import patch

import numpy as np
import pandas as pd
from botocore.exceptions import EndpointConnectionError

from model_artifact_storage_smoke import FakeS3
from stocklab_ml.artifacts import S3ArtifactConfig, S3ModelArtifactStore
from stocklab_ml.data.storage import save_processed
from stocklab_ml.features import engineer_features
from stocklab_ml.modeling import train_random_forest
from stocklab_ml.retraining import RetrainingConfig, RetrainingDependencies, run_retraining_check
from stocklab_ml.versioning import ModelStatus, ModelVersionRegistry


def history(count):
    positions = np.arange(count)
    prices = 100 + positions * .07 + 3 * np.sin(positions * .7)
    return pd.DataFrame({
        "date": pd.date_range("2024-01-01", periods=count, freq="B").as_unit("ns"),
        "symbol": pd.array(["AAPL"] * count, dtype="string"),
        "open": prices, "high": prices + 1, "low": prices - 1, "close": prices,
        "volume": pd.array(1000 + positions, dtype="Int64"),
    })


def main():
    def forbidden(*args, **kwargs):
        raise AssertionError("Network is forbidden in the offline smoke")
    socket.socket.connect = forbidden
    socket.socket.connect_ex = forbidden
    clock = datetime(2024, 7, 1, 16, tzinfo=UTC)
    root = Path(__file__).resolve().parents[1] / "results" / "automatic_retraining_smoke" / str(uuid4())
    config = RetrainingConfig(symbols=("AAPL",), dataset_path=root / "data.csv",
        state_path=root / "retraining" / "state.json", registry_path=root / "registry.json",
        artifacts_path=root / "artifacts", upload_to_s3=True)
    save_processed(config.dataset_path, history(100))
    registry = ModelVersionRegistry(config.registry_path, clock=lambda: clock)
    initial = train_random_forest(engineer_features(history(100)).dataframe)
    for trained in [initial.baseline_result, initial]:
        record = registry.register_version(trained, training_run_id=str(uuid4()), trained_at_utc=clock)
        registry.set_active_version(record.model_name, record.model_version)  # Explicit simulation setup only.
    active_before = {name: registry.get_active_version(name).to_dict() for name in
                    ["logistic_regression_baseline", "random_forest_baseline"]}
    class InterruptedS3(FakeS3):
        fail_once = True

        def put_object(self, **kwargs):
            if kwargs["Key"].endswith("manifest.json") and self.fail_once:
                self.fail_once = False
                raise EndpointConnectionError(endpoint_url="https://offline.invalid")
            return super().put_object(**kwargs)
    fake = InterruptedS3()
    remote = S3ModelArtifactStore(S3ArtifactConfig("stocklab-offline-dev", "ca-central-1", "dev/smoke", True), client=fake)
    dependencies = RetrainingDependencies(history(103), remote)
    partial = run_retraining_check(config, dependencies=dependencies, now_utc=clock, trigger_source="offline_smoke")
    assert partial.status == "PARTIAL_PUBLICATION" and len(fake.objects) == 3
    with patch("stocklab_ml.retraining.orchestrator.train_random_forest", forbidden), \
         patch("stocklab_ml.retraining.orchestrator.train_logistic_regression", forbidden), \
         patch("stocklab_ml.retraining.orchestrator.engineer_features", forbidden):
        result = run_retraining_check(config, dependencies=dependencies, now_utc=clock, retry_run_id=partial.run_id)
    assert result.status == "COMPLETED" and result.report["new_session_counts"] == {"AAPL": 3}
    assert result.report["api_request_count"] == 0 and len(fake.objects) == 4
    for family, model in result.report["model_results"].items():
        name = model["model_name"]
        assert registry.get_active_version(name).to_dict() == active_before[name]
        candidate = registry.get_version(name, "v000002")
        assert candidate.status == ModelStatus.ARCHIVED and candidate.training_run_id == model["training_run_id"]
        assert candidate.metrics.total_rows == sum(candidate.to_dict()["metrics"][key] for key in
            ["true_positive", "true_negative", "false_positive", "false_negative"])
        assert model["metrics"] == candidate.to_dict()["metrics"]
        assert model["s3_reference"]["artifact_sha256"] == model["artifact_sha256"]
    state_before, registry_before = config.state_path.read_bytes(), config.registry_path.read_bytes()
    replay = run_retraining_check(config, dependencies=dependencies, now_utc=clock, retry_run_id=result.run_id)
    assert replay.status == "SKIPPED_ALREADY_PROCESSED"
    assert config.state_path.read_bytes() == state_before and config.registry_path.read_bytes() == registry_before
    no_data = run_retraining_check(config, dependencies=dependencies, now_utc=clock + timedelta(days=3))
    assert no_data.status == "SKIPPED_NO_NEW_DATA" and config.registry_path.read_bytes() == registry_before
    print(json.dumps({"synthetic_data_only": True, "network_calls": 0, "live_aws_calls": 0,
        "run": result.to_dict(), "partial_publication": partial.status,
        "replay": replay.status, "no_new_data": no_data.status,
        "active_preserved": True, "automatic_promotion": False}, indent=2))


if __name__ == "__main__":
    main()
