"""Real fitted models: exact round-trips, trust boundaries and unchanged inference."""

from copy import deepcopy
from dataclasses import asdict, replace
from importlib import import_module
import hashlib
import io
import json
import os
from uuid import uuid4

import joblib
import pandas as pd
import pytest
from sklearn.ensemble import RandomForestClassifier
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler

from stocklab_ml.features import FEATURE_COLUMNS
from stocklab_ml.signals import add_confidence_scores, generate_trading_signals
from stocklab_ml.versioning import (
    ModelStatus, ModelVersionError, ModelVersionRegistry, RegisteredModel,
    save_versioned_decisions, save_versioned_predictions, version_confidence_decisions,
)
from test_model_dataset import feature_frame
from test_model_versioning import models, register, registry


def api():
    return import_module("stocklab_ml.artifacts")


def bound(registry, result):
    record = register(registry, result)
    return registry.bind_version(result, model_name=record.model_name, model_version=record.model_version)


def saved(registry, result, tmp_path):
    context = bound(registry, result)
    artifact = api().save_model_artifact(context, output_dir=tmp_path / "artifacts")
    return context, artifact


def load(artifact, registry=None, **kwargs):
    return api().load_model_artifact(artifact, registry=registry, trusted_source=True, **kwargs)


def never(*args, **kwargs):
    pytest.fail("Unverified deserialization or training is forbidden", pytrace=False)


@pytest.mark.parametrize("index", [0, 1])
def test_local_round_trip_preserves_fitted_model_and_exact_predictions(registry, models, tmp_path, index, monkeypatch):
    result = models[index]
    context = bound(registry, result)
    features = feature_frame().tail(30).drop(columns=["open", "high", "low", "close"])
    original = context.predict(features)
    report_before, predictions_before = asdict(result.report), result.predictions.copy(deep=True)
    registry_before = registry.path.read_bytes()
    estimator = result.pipeline if index == 0 else result.model
    buffer = io.BytesIO()
    joblib.dump(estimator, buffer, protocol=5)
    artifact = api().save_model_artifact(context, output_dir=tmp_path / "artifacts")
    manifest = json.loads(artifact.manifest_path.read_text())
    data = artifact.artifact_path.read_bytes()
    assert artifact.artifact_path == tmp_path / "artifacts" / context.record.model_name / "v000001" / "model.joblib"
    assert manifest["schema_version"] == 1 and manifest["serialization_format"] == "joblib"
    assert manifest["training_run_id"] == context.record.training_run_id
    assert manifest["artifact_sha256"] == hashlib.sha256(data).hexdigest()
    assert manifest["artifact_sha256"] != context.record.holdout_sha256
    assert manifest["artifact_size_bytes"] == len(data)
    assert set(manifest["environment"]) == {"python", "scikit_learn", "numpy", "scipy", "joblib"}
    for cls in [Pipeline, StandardScaler, LogisticRegression, RandomForestClassifier]:
        monkeypatch.setattr(cls, "fit", never)
    for module in ["stocklab_ml.modeling", "stocklab_ml.modeling.logistic", "stocklab_ml.modeling.random_forest"]:
        for name in ["train_logistic_regression", "train_random_forest"]:
            if hasattr(import_module(module), name): monkeypatch.setattr(import_module(module), name, never)
    loaded = load(artifact, registry)
    assert not isinstance(loaded, RegisteredModel)
    assert loaded.record == context.record and loaded.estimator is not estimator
    assert loaded.artifact_sha256 == manifest["artifact_sha256"]
    if index == 0:
        assert type(loaded.estimator) is Pipeline
        assert list(loaded.estimator.named_steps) == ["scaler", "classifier"]
        assert hasattr(loaded.estimator.named_steps["scaler"], "mean_")
    else:
        assert type(loaded.estimator) is RandomForestClassifier
        assert len(loaded.estimator.estimators_) == 300
        assert loaded.estimator.feature_importances_.tolist() == estimator.feature_importances_.tolist()
    assert list(loaded.estimator.feature_names_in_) == FEATURE_COLUMNS
    actual = loaded.predict(features)
    pd.testing.assert_frame_equal(actual.to_dataframe(), original.to_dataframe(), check_exact=True)
    assert "actual_class" not in actual.predictions and "target_date" not in actual.predictions
    expected_decisions = add_confidence_scores(generate_trading_signals(original.predictions, model_name=context.record.model_name))
    decisions = add_confidence_scores(generate_trading_signals(actual.predictions, model_name=loaded.record.model_name))
    pd.testing.assert_frame_equal(decisions.signals, expected_decisions.signals, check_exact=True)
    versioned = version_confidence_decisions(decisions, predictions=actual)
    assert versioned.to_dataframe().model_version.eq("v000001").all()
    save_versioned_predictions(actual, batch_id=str(uuid4()), output_dir=tmp_path / "predictions")
    save_versioned_decisions(versioned, batch_id=str(uuid4()), output_dir=tmp_path / "decisions")
    assert registry.path.read_bytes() == registry_before
    assert asdict(result.report) == report_before
    pd.testing.assert_frame_equal(result.predictions, predictions_before, check_exact=True)
    after = io.BytesIO()
    joblib.dump(estimator, after, protocol=5)
    assert after.getvalue() == buffer.getvalue()


@pytest.mark.parametrize("bad", ["bytes", "short", "long", "size"])
def test_integrity_failure_never_unpickles(registry, models, tmp_path, monkeypatch, bad):
    _, artifact = saved(registry, models[0], tmp_path)
    data = artifact.artifact_path.read_bytes()
    if bad == "bytes": data = bytes([data[0] ^ 1]) + data[1:]
    if bad == "short": data = data[:-1]
    if bad == "long": data += b"x"
    if bad == "size":
        manifest = json.loads(artifact.manifest_path.read_text())
        manifest["artifact_size_bytes"] += 1
        artifact.manifest_path.write_text(json.dumps(manifest))
    else: artifact.artifact_path.write_bytes(data)
    monkeypatch.setattr(joblib, "load", never)
    with pytest.raises(api().ArtifactIntegrityError): load(artifact, registry)


@pytest.mark.parametrize("field,value", [
    ("schema_version", 2), ("schema_version", True), ("serialization_format", "pickle"),
    ("model_name", "wrong"), ("model_version", "v000002"), ("model_version", "latest"),
    ("training_run_id", "00000000-0000-0000-0000-000000000001"),
    ("model_type", "random_forest"), ("artifact_filename", "../model.joblib"),
    ("artifact_sha256", "a" * 64), ("artifact_sha256", "bad"),
    ("artifact_size_bytes", 0), ("artifact_size_bytes", -1), ("artifact_size_bytes", True),
    ("artifact_size_bytes", 100 * 1024 * 1024 + 1), ("metadata_sha256", "a" * 64),
    ("artifact_key", "outside/prefix"),
])
def test_manifest_tampering_rejected_before_deserialization(registry, models, tmp_path, monkeypatch, field, value):
    _, artifact = saved(registry, models[0], tmp_path)
    manifest = json.loads(artifact.manifest_path.read_text())
    manifest[field] = value
    artifact.manifest_path.write_text(json.dumps(manifest))
    monkeypatch.setattr(joblib, "load", never)
    with pytest.raises(api().ModelArtifactError): load(artifact, registry)


@pytest.mark.parametrize("bad", ["invalid_json", "missing", "duplicate", "nonfinite", "oversize"])
def test_invalid_manifest_encoding_is_controlled(registry, models, tmp_path, monkeypatch, bad):
    _, artifact = saved(registry, models[0], tmp_path)
    text = artifact.manifest_path.read_text()
    if bad == "invalid_json": text = "{"
    if bad == "missing":
        payload = json.loads(text)
        del payload["artifact_sha256"]
        text = json.dumps(payload)
    if bad == "duplicate": text = text.replace('"schema_version": 1', '"schema_version": 1, "schema_version": 1')
    if bad == "nonfinite": text = text.replace('"schema_version": 1', '"schema_version": NaN')
    if bad == "oversize": text = " " * (1024 * 1024 + 1)
    artifact.manifest_path.write_text(text)
    monkeypatch.setattr(joblib, "load", never)
    with pytest.raises(api().ModelArtifactError): load(artifact, registry)


@pytest.mark.parametrize("package", ["python", "scikit_learn", "numpy", "scipy", "joblib"])
def test_incompatible_runtime_fails_before_unpickling(registry, models, tmp_path, monkeypatch, package):
    _, artifact = saved(registry, models[0], tmp_path)
    manifest = json.loads(artifact.manifest_path.read_text())
    manifest["environment"][package] = "0.0.0"
    artifact.manifest_path.write_text(json.dumps(manifest))
    monkeypatch.setattr(joblib, "load", never)
    with pytest.raises(api().ArtifactCompatibilityError): load(artifact, registry)


@pytest.mark.parametrize("python_version", ["3.12.invalid", "3.12", "3.12.14.extra"])
def test_malformed_python_runtime_is_rejected_before_unpickling(registry, models, tmp_path, monkeypatch, python_version):
    _, artifact = saved(registry, models[0], tmp_path)
    manifest = json.loads(artifact.manifest_path.read_text())
    manifest["environment"]["python"] = python_version
    artifact.manifest_path.write_text(json.dumps(manifest))
    monkeypatch.setattr(joblib, "load", never)
    with pytest.raises(api().ArtifactIntegrityError): load(artifact, registry)


def test_trust_and_explicit_cold_start_preserve_snapshot_without_creating_registry(registry, models, tmp_path, monkeypatch):
    context, artifact = saved(registry, models[0], tmp_path)
    with pytest.raises(api().ModelArtifactError):
        api().load_model_artifact(artifact, registry=registry)
    missing = ModelVersionRegistry(tmp_path / "absent.json")
    with pytest.raises(api().ArtifactIntegrityError): load(artifact, missing, allow_without_registry=True)
    with pytest.raises(api().ModelArtifactError): load(artifact)
    cold = load(artifact, allow_without_registry=True)
    assert cold.record == context.record
    predictions = cold.predict(feature_frame().tail(3))
    decisions = version_confidence_decisions(add_confidence_scores(generate_trading_signals(
        predictions.predictions, model_name=cold.record.model_name)), predictions=predictions)
    save_versioned_predictions(predictions, batch_id=str(uuid4()), output_dir=tmp_path / "exports")
    save_versioned_decisions(decisions, batch_id=str(uuid4()), output_dir=tmp_path / "exports")
    assert not missing.path.exists()
    assert decisions.to_dataframe().model_version.eq("v000001").all()


def test_status_changes_do_not_invalidate_artifact_or_activate_loaded_version(registry, models, tmp_path):
    context, artifact = saved(registry, models[1], tmp_path)
    second = register(registry, models[1])
    registry.set_active_version(second.model_name, second.model_version)
    before = registry.path.read_bytes()
    loaded = load(artifact, registry)
    assert loaded.record.status == ModelStatus.ARCHIVED
    assert loaded.predict(feature_frame().tail(1)).to_dataframe().model_version.tolist() == ["v000001"]
    registry.set_active_version(context.record.model_name, context.record.model_version)
    assert load(artifact, registry).record.status == ModelStatus.ACTIVE
    registry.archive_version(context.record.model_name, context.record.model_version)
    assert load(artifact, registry).record.status == ModelStatus.ARCHIVED
    assert before != registry.path.read_bytes()  # Only explicit administrative calls changed status.


@pytest.mark.parametrize("bad", ["features", "classes", "parameters", "unfitted", "family", "iterations", "scaler", "trees", "importances"])
def test_loaded_estimator_must_match_registry_metadata(registry, models, tmp_path, bad):
    index = 1 if bad in {"trees", "importances"} else 0
    _, artifact = saved(registry, models[index], tmp_path)
    estimator = deepcopy(models[index].model if index else models[index].pipeline)
    classifier = estimator if index else estimator.named_steps["classifier"]
    if bad == "features": estimator.feature_names_in_[:] = list(reversed(FEATURE_COLUMNS))
    if bad == "classes": classifier.classes_ = [0, 2]
    if bad == "parameters": classifier.set_params(random_state=999)
    if bad == "unfitted": estimator = Pipeline([("scaler", StandardScaler()), ("classifier", LogisticRegression())])
    if bad == "family": estimator = models[1].model
    if bad == "iterations": classifier.n_iter_[:] += 1
    if bad == "scaler": estimator.named_steps["scaler"].scale_[0] = float("nan")
    if bad == "trees": estimator.estimators_.pop()
    if bad == "importances": estimator.estimators_[0].tree_.impurity[:] = 0
    buffer = io.BytesIO()
    joblib.dump(estimator, buffer, protocol=5)
    data = buffer.getvalue()
    manifest = json.loads(artifact.manifest_path.read_text())
    manifest.update(artifact_sha256=hashlib.sha256(data).hexdigest(), artifact_size_bytes=len(data))
    artifact.artifact_path.write_bytes(data)
    artifact.manifest_path.write_text(json.dumps(manifest))
    with pytest.raises(api().ModelArtifactError): load(artifact, registry)


def test_local_no_overwrite_and_retry_cannot_replace_a_version(registry, models, tmp_path):
    context, artifact = saved(registry, models[0], tmp_path)
    before = artifact.artifact_path.read_bytes(), artifact.manifest_path.read_bytes()
    with pytest.raises(api().ArtifactConflictError):
        api().save_model_artifact(context, output_dir=tmp_path / "artifacts")
    assert api().save_model_artifact(context, output_dir=tmp_path / "artifacts", overwrite=True) == artifact
    changed = deepcopy(models[0])
    changed.pipeline.named_steps["classifier"].coef_[0, 0] += 1
    changed_context = registry.bind_version(changed, model_name=context.record.model_name, model_version="v000001")
    with pytest.raises(api().ArtifactConflictError):
        api().save_model_artifact(changed_context, output_dir=tmp_path / "artifacts", overwrite=True)
    assert (artifact.artifact_path.read_bytes(), artifact.manifest_path.read_bytes()) == before


def test_interrupted_local_publication_requires_explicit_identical_retry(registry, models, tmp_path, monkeypatch):
    context = bound(registry, models[0])
    original_link = os.link
    def interrupt(source, destination, *args, **kwargs):
        if destination.name == "manifest.json": raise OSError("interrupted")
        return original_link(source, destination, *args, **kwargs)
    with monkeypatch.context() as patch:
        patch.setattr(os, "link", interrupt)
        with pytest.raises(api().ArtifactStorageError):
            api().save_model_artifact(context, output_dir=tmp_path / "artifacts")
    directory = tmp_path / "artifacts" / context.record.model_name / "v000001"
    assert (directory / "model.joblib").exists() and not (directory / "manifest.json").exists()
    assert not list(directory.glob("*.tmp"))
    artifact = api().save_model_artifact(context, output_dir=tmp_path / "artifacts", overwrite=True)
    assert load(artifact, registry).record == context.record


@pytest.mark.parametrize("bad", ["version", "family", "reference"])
def test_save_rejects_invalid_registered_context_before_writes(registry, models, tmp_path, bad):
    context = bound(registry, models[0])
    if bad == "version": context = replace(context, record=replace(context.record, model_version="v000002", version_number=2))
    if bad == "family": context = replace(context, _result=models[1])
    if bad == "reference": context = models[0]
    with pytest.raises((ModelVersionError, api().ModelArtifactError)):
        api().save_model_artifact(context, output_dir=tmp_path / "artifacts")
    assert not (tmp_path / "artifacts").exists()


def test_local_limits_and_missing_files_are_controlled(registry, models, tmp_path, monkeypatch):
    context, artifact = saved(registry, models[0], tmp_path)
    monkeypatch.setattr(joblib, "load", never)
    with pytest.raises(api().ModelArtifactError): load(artifact, registry, max_artifact_bytes=1)
    artifact.artifact_path.unlink()
    with pytest.raises(api().ArtifactNotFoundError): load(artifact, registry)


def test_existing_local_registry_conflict_is_never_bypassed(registry, models, tmp_path, monkeypatch):
    _, artifact = saved(registry, models[0], tmp_path)
    payload = json.loads(registry.path.read_text())
    payload["models"][0]["training_run_id"] = str(uuid4())
    registry.path.write_text(json.dumps(payload))
    monkeypatch.setattr(joblib, "load", never)
    with pytest.raises((ModelVersionError, api().ModelArtifactError)):
        load(artifact, registry, allow_without_registry=True)


@pytest.mark.parametrize("allow_exact_limit", [False, True])
def test_save_checks_manifest_size_before_publishing_any_file(registry, models, tmp_path, monkeypatch, allow_exact_limit):
    context, artifact = saved(registry, models[0], tmp_path)
    storage = import_module("stocklab_ml.artifacts.local_storage")
    actual_size = len(artifact.manifest_path.read_bytes())
    monkeypatch.setattr(storage, "MAX_MANIFEST_BYTES", actual_size if allow_exact_limit else actual_size - 1)
    output = tmp_path / "another"
    if allow_exact_limit:
        assert api().save_model_artifact(context, output_dir=output).manifest_path.exists()
    else:
        with pytest.raises(api().ArtifactIntegrityError): api().save_model_artifact(context, output_dir=output)
        assert not output.exists()
