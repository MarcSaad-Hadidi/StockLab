"""#78 offline: existing fitted artifacts, a common OOS window, honest policy outcome."""

from datetime import UTC, datetime
import json
from pathlib import Path
import socket
from unittest.mock import patch
from uuid import uuid4

from sklearn.ensemble import RandomForestClassifier
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler

from automatic_retraining_smoke import history
from stocklab_ml.artifacts import load_model_artifact, save_model_artifact
from stocklab_ml.data.cleaning import clean_historical_dataset
from stocklab_ml.features import engineer_features
from stocklab_ml.modeling import train_logistic_regression
from stocklab_ml.promotion import (
    ModelPromotionPolicy, ModelPromotionRequest, PromotionAuditStore,
    apply_model_promotion, evaluate_model_promotion,
)
from stocklab_ml.versioning import ModelStatus, ModelVersionRegistry


def main():
    def forbidden(*args, **kwargs):
        raise AssertionError("No training or external network during promotion")
    socket.socket.connect = forbidden
    socket.socket.connect_ex = forbidden
    now = datetime.now(UTC)
    root = Path(__file__).resolve().parents[1] / "results" / "model_promotion_smoke" / str(uuid4())
    registry = ModelVersionRegistry(root / "registry.json")
    artifacts = []
    for count in (100, 140):
        trained = train_logistic_regression(engineer_features(history(count)).dataframe)
        record = registry.register_version(trained, training_run_id=str(uuid4()), trained_at_utc=now)
        bound = registry.bind_version(trained, model_name=record.model_name, model_version=record.model_version)
        artifacts.append(save_model_artifact(bound, output_dir=root / "artifacts"))
    registry.set_active_version(record.model_name, "v000001")  # Explicit simulation setup only.
    request = ModelPromotionRequest(record.model_name, "v000002", "v000001",
        clean_historical_dataset(history(220), as_of_date="2024-11-01"), ModelPromotionPolicy(),
        datetime.now(UTC), str(uuid4()), "2024-07-01", "2024-10-31")
    store = PromotionAuditStore(root / "promotion")
    loaded = [load_model_artifact(source, registry=registry, trusted_source=True) for source in artifacts]
    before = registry.path.read_bytes()
    artifact_bytes = [(source.artifact_path.read_bytes(), source.manifest_path.read_bytes()) for source in artifacts]
    kwargs = dict(registry=registry, audit_store=store, active_artifact=loaded[0],
                  candidate_artifact=loaded[1], trusted_source=True)
    with patch.object(Pipeline, "fit", forbidden), patch.object(StandardScaler, "fit", forbidden), \
         patch.object(LogisticRegression, "fit", forbidden), patch.object(RandomForestClassifier, "fit", forbidden):
        decision = evaluate_model_promotion(request, **kwargs)
        assert decision.decision in {"APPROVED", "REJECTED"}
        assert registry.path.read_bytes() == before
        assert store.load_evaluation(request.evaluation_id) == decision
        if decision.decision == "APPROVED":
            assert apply_model_promotion(decision, request=request, **kwargs).status == "APPLIED"
            assert store.load_application(request.evaluation_id).status == "APPLIED"
            assert registry.get_version(record.model_name, "v000001").status == ModelStatus.ARCHIVED
            assert registry.get_active_version(record.model_name).model_version == "v000002"
        else:
            assert registry.get_active_version(record.model_name).model_version == "v000001"
            assert registry.get_version(record.model_name, "v000002").status == ModelStatus.ARCHIVED
    assert artifact_bytes == [(source.artifact_path.read_bytes(), source.manifest_path.read_bytes()) for source in artifacts]
    print(json.dumps({"evaluation": decision.to_dict(), "evaluation_sha256": decision.evidence_sha256,
        "audit_path": str(store.evaluation_path(request.evaluation_id)), "external_api_calls": 0,
        "training_during_promotion": False, "historical_simulation_only": True}, indent=2))


if __name__ == "__main__":
    main()
