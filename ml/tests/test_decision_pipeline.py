"""Actual fitted inference and safe, replayable operator batches; no network."""

from datetime import UTC, datetime
from decimal import Decimal
from importlib import import_module
import json
import os
import subprocess
import sys
from uuid import uuid4

import pandas as pd
import pytest
from sklearn.ensemble import RandomForestClassifier
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import Pipeline

from stocklab_ml.artifacts import load_model_artifact, save_model_artifact
from stocklab_ml.artifacts.local_storage import artifact_paths
from stocklab_ml.features import engineer_features
from stocklab_ml.modeling import train_random_forest
from stocklab_ml.versioning import ModelVersionRegistry
from test_retraining import history


def api():
    return import_module("stocklab_ml.decision_pipeline")


@pytest.fixture(scope="module")
def fitted():
    forest = train_random_forest(engineer_features(history()).dataframe)
    return forest.baseline_result, forest


@pytest.fixture
def source(tmp_path, fitted):
    registry = ModelVersionRegistry(tmp_path / "registry.json")
    for result in fitted:
        record = registry.register_version(result, training_run_id=str(uuid4()),
                                           trained_at_utc=datetime(2024, 6, 1, tzinfo=UTC))
        save_model_artifact(registry.bind_version(result, model_name=record.model_name,
                            model_version=record.model_version), output_dir=tmp_path / "artifacts")
        registry.set_active_version(record.model_name, record.model_version)
    dataset = tmp_path / "prices.csv"
    history(110).to_csv(dataset, index=False, date_format="%Y-%m-%d")
    return dict(registry_path=registry.path, artifacts_dir=tmp_path / "artifacts",
                dataset_path=dataset, model_name=fitted[0].report.model_name,
                symbols=("AAPL",), decision_date=history(110).date.iloc[-1].date().isoformat(),
                trusted_artifacts=True)


@pytest.mark.parametrize("index", [0, 1])
def test_verified_active_model_produces_actual_target_free_versioned_decisions(source, fitted, monkeypatch, index):
    source["model_name"] = fitted[index].report.model_name
    before = {name: source[name].read_bytes() for name in ("registry_path", "dataset_path")}

    def no_training(*args, **kwargs):
        pytest.fail("A decision cycle must never fit a model")

    for estimator in (Pipeline, LogisticRegression, RandomForestClassifier):
        monkeypatch.setattr(estimator, "fit", no_training)
    result = api().infer_decisions(**source)
    assert set(result) == {"schema_version", "model_name", "model_version", "decision_date", "decisions"}
    assert result["schema_version"] == 1
    assert result["model_name"] == fitted[index].report.model_name
    assert result["model_version"] == "v000001"
    assert result["decision_date"] == source["decision_date"]
    assert len(result["decisions"]) == 1
    row = result["decisions"][0]
    assert set(row) == {"symbol", "signal", "confidence"}
    assert row["symbol"] == "AAPL" and row["signal"] in {"BUY", "SELL", "HOLD"}
    assert Decimal(0) <= Decimal(row["confidence"]) <= Decimal(1)
    loaded = load_model_artifact(artifact_paths(source["model_name"], "v000001", source["artifacts_dir"]),
                                 registry=ModelVersionRegistry(source["registry_path"]), trusted_source=True)
    predictions = loaded.predict(engineer_features(history(110)).dataframe.tail(1)).predictions
    p = float(predictions.probability_up.iloc[0])
    expected = ("BUY", p) if p >= .6 else ("SELL", 1 - p) if p <= .4 else ("HOLD", 1 - 2 * abs(p - .5))
    assert row["signal"] == expected[0]
    assert Decimal(row["confidence"]) == Decimal(str(expected[1]))
    assert result == api().infer_decisions(**source)
    assert before == {name: source[name].read_bytes() for name in before}


def test_future_rows_do_not_change_the_selected_session(source):
    expected = api().infer_decisions(**source)
    future = history(125)
    future.loc[110:, ["open", "high", "low", "close"]] *= 100
    future.to_csv(source["dataset_path"], index=False, date_format="%Y-%m-%d")
    assert api().infer_decisions(**source) == expected


@pytest.mark.parametrize("bad", ["trust", "no_active", "artifact", "stale", "insufficient", "invalid_row",
                                "future_date", "symbols", "duplicate_symbol", "training_boundary"])
def test_invalid_prerequisites_never_produce_a_batch(source, bad):
    if bad == "trust":
        source["trusted_artifacts"] = False
    elif bad == "no_active":
        ModelVersionRegistry(source["registry_path"]).archive_version(source["model_name"], "v000001")
    elif bad == "artifact":
        artifact_paths(source["model_name"], "v000001", source["artifacts_dir"]).artifact_path.write_bytes(b"bad")
    elif bad == "stale":
        source["decision_date"] = "2024-06-03"
    elif bad == "insufficient":
        history(10).to_csv(source["dataset_path"], index=False)
        source["decision_date"] = "2024-01-12"
    elif bad == "invalid_row":
        frame = history(110)
        frame.loc[0, "close"] = -1
        frame.to_csv(source["dataset_path"], index=False)
    elif bad == "future_date":
        source["decision_date"] = "2100-01-01"
    elif bad == "symbols":
        source["symbols"] = ("MSFT",)
    elif bad == "duplicate_symbol":
        source["symbols"] = ("AAPL", "AAPL")
    else:
        source["decision_date"] = "2024-01-29"
    with pytest.raises(api().DecisionInferenceError):
        api().infer_decisions(**source)


def test_changed_active_version_during_inference_aborts_the_batch(source, fitted, monkeypatch):
    from stocklab_ml.artifacts.serialization import LoadedModel
    registry = ModelVersionRegistry(source["registry_path"])
    record = registry.register_version(fitted[0], training_run_id=str(uuid4()),
                                       trained_at_utc=datetime(2024, 6, 1, tzinfo=UTC))
    original = LoadedModel.predict

    def promote_while_predicting(model, features):
        result = original(model, features)
        registry.set_active_version(record.model_name, record.model_version)
        return result

    monkeypatch.setattr(LoadedModel, "predict", promote_while_predicting)
    with pytest.raises(api().DecisionInferenceError):
        api().infer_decisions(**source)


def test_cli_sanitizes_private_failures_and_keeps_stdout_empty(tmp_path):
    completed = subprocess.run([sys.executable, "-m", "stocklab_ml.decision_pipeline",
        "--registry-path", str(tmp_path / "private-registry"), "--artifacts-dir", str(tmp_path),
        "--dataset-path", str(tmp_path / "private-dataset"), "--model-name", "logistic_regression_baseline",
        "--symbols", "AAPL", "--decision-date", "2024-05-31", "--trusted-artifacts"],
        env=os.environ | {"PYTHONPATH": os.path.dirname(os.path.dirname(__file__)) + "/src"},
        capture_output=True, text=True, timeout=30)
    assert completed.returncode == 1
    assert completed.stdout == ""
    assert completed.stderr.strip() == "decision_inference_failed"
    assert "private" not in completed.stderr
