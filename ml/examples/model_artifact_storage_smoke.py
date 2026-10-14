"""Offline #76: discard both trained estimators, retrieve, verify and infer without fit."""

from datetime import UTC, datetime
import hashlib
from importlib import import_module
import io
import json
from pathlib import Path
import socket
from uuid import uuid4

from botocore.exceptions import ClientError
import numpy as np
import pandas as pd

from stocklab_ml.artifacts import (
    S3ArtifactConfig, S3ModelArtifactStore, load_model_artifact, save_model_artifact,
)
from stocklab_ml.features import engineer_features
from stocklab_ml.modeling import train_random_forest
from stocklab_ml.signals import add_confidence_scores, generate_trading_signals
from stocklab_ml.versioning import ModelStatus, ModelVersionRegistry, version_confidence_decisions


class FakeS3:
    """A small private in-memory bucket, with conditional PUT and no network."""

    def __init__(self):
        self.objects = {}

    def get_object(self, *, Bucket, Key):
        if (Bucket, Key) not in self.objects:
            raise ClientError({"Error": {"Code": "NoSuchKey"}}, "GetObject")
        data = self.objects[Bucket, Key]
        return {"Body": io.BytesIO(data), "ContentLength": len(data)}

    def put_object(self, *, Bucket, Key, Body, IfNoneMatch, **kwargs):
        assert IfNoneMatch == "*"
        if (Bucket, Key) in self.objects:
            raise ClientError({"Error": {"Code": "PreconditionFailed"}}, "PutObject")
        self.objects[Bucket, Key] = bytes(Body)
        return {}


def main():
    def forbidden(*args, **kwargs):
        raise AssertionError("Network and retraining are forbidden after persistence")
    socket.socket.connect = forbidden
    socket.socket.connect_ex = forbidden
    positions = np.arange(120)
    prices = 100 + positions * .07 + 3 * np.sin(positions * .7)
    raw = pd.DataFrame({
        "date": pd.date_range("2024-01-01", periods=len(prices), freq="B").as_unit("ns"),
        "symbol": pd.array(["AAPL"] * len(prices), dtype="string"),
        "open": prices, "high": prices + 1, "low": prices - 1, "close": prices,
        "volume": pd.array(1000 + positions, dtype="Int64"),
    })
    features = engineer_features(raw).dataframe
    forest = train_random_forest(features)
    trained_at = datetime.now(UTC)
    directory = Path(__file__).resolve().parents[1] / "results" / "model_artifact_storage_smoke" / str(uuid4())
    registry = ModelVersionRegistry(directory / "registry.json")
    fake = FakeS3()
    store = S3ModelArtifactStore(S3ArtifactConfig("stocklab-offline-dev", "ca-central-1", "dev/smoke",
                                               trusted_private_source=True), client=fake)
    rows = features.tail(30).drop(columns=["open", "high", "low", "close"])
    expected, identities = {}, []
    for result in [forest.baseline_result, forest]:
        record = registry.register_version(result, training_run_id=str(uuid4()), trained_at_utc=trained_at)
        context = registry.bind_version(result, model_name=record.model_name, model_version=record.model_version)
        expected[record.model_name] = context.predict(rows).to_dataframe()
        identities.append((record.model_name, record.model_version))
        artifact = save_model_artifact(context, output_dir=directory / "local")
        reference = store.upload_model_artifact(artifact, registry=registry)
        assert store.upload_model_artifact(artifact, registry=registry) == reference
    del forest, result, context
    for module in ["stocklab_ml.modeling", "stocklab_ml.modeling.logistic", "stocklab_ml.modeling.random_forest"]:
        for name in ["train_logistic_regression", "train_random_forest"]:
            if hasattr(import_module(module), name): setattr(import_module(module), name, forbidden)
    from sklearn.pipeline import Pipeline
    from sklearn.preprocessing import StandardScaler
    from sklearn.linear_model import LogisticRegression
    from sklearn.ensemble import RandomForestClassifier
    for cls in [Pipeline, StandardScaler, LogisticRegression, RandomForestClassifier]: cls.fit = forbidden
    registry_before = registry.path.read_bytes()
    measurements = []
    for name, version in identities:
        downloaded = store.download_model_artifact(name, version, registry=registry, output_dir=directory / "cache")
        manifest = json.loads(downloaded.manifest_path.read_text())
        assert hashlib.sha256(downloaded.artifact_path.read_bytes()).hexdigest() == manifest["artifact_sha256"]
        loaded = load_model_artifact(downloaded, registry=registry, trusted_source=True)
        predictions = loaded.predict(rows)
        pd.testing.assert_frame_equal(predictions.to_dataframe(), expected[name], check_exact=True)
        assert "target_date" not in predictions.predictions and "actual_class" not in predictions.predictions
        assert loaded.record.status == ModelStatus.ARCHIVED
        decisions = add_confidence_scores(generate_trading_signals(predictions.predictions, model_name=name))
        assert version_confidence_decisions(decisions, predictions=predictions).to_dataframe().model_version.eq(version).all()
        measurements.append({"model_name": name, "model_version": version,
                             "artifact_size_bytes": manifest["artifact_size_bytes"],
                             "artifact_sha256": manifest["artifact_sha256"]})
    assert registry.path.read_bytes() == registry_before and len(fake.objects) == 4
    print(json.dumps({"models": measurements, "network_calls": 0, "live_aws_calls": 0,
                      "retraining_calls": 0, "prediction_equivalence": "exact"}, indent=2))


if __name__ == "__main__":
    main()
