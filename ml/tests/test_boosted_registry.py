"""One synthetic HGBT28 snapshot through the existing archived registry/artifacts."""

from copy import deepcopy
from dataclasses import replace
from datetime import datetime, timedelta
import hashlib
import json
from types import SimpleNamespace
from uuid import uuid4

import joblib
import pandas as pd
import pytest
from sklearn.ensemble import HistGradientBoostingClassifier

from stocklab_ml.artifacts import load_model_artifact, save_model_artifact
from stocklab_ml.artifacts.contracts import ModelArtifactError
from stocklab_ml.versioning import ModelStatus, ModelVersionError, ModelVersionRegistry
from stocklab_ml.versioning.validation import decode_record
from test_boosted_snapshot import fitted_inputs, fitted_snapshot, snapshot_thread_limits


@pytest.fixture
def bound(fitted_snapshot, tmp_path):
    snapshot = fitted_snapshot[0]
    trained = datetime.fromisoformat(snapshot.metadata["trained_at_utc"])
    registry = ModelVersionRegistry(tmp_path / "registry.json", clock=lambda: trained + timedelta(seconds=1))
    record = registry.register_version(snapshot, training_run_id=str(uuid4()), trained_at_utc=trained)
    return registry, registry.bind_version(snapshot, model_name=record.model_name, model_version=record.model_version)


def feature_rows(fitted_inputs):
    return fitted_inputs[0].loc[lambda x: x.date.dt.year.eq(2024)].drop(columns=["target_20", "target_date_20"])


def forbidden(*args, **kwargs):
    pytest.fail("Registration/artifact inference must not fit or replace retained model bytes.")


def test_snapshot_registers_typed_archived_pending_identity_without_fake_holdout(bound, fitted_snapshot):
    registry, context = bound
    snapshot = fitted_snapshot[0]
    record = context.record
    assert type(record).__name__ == "BoostedModelVersionRecord"
    assert record.status == ModelStatus.ARCHIVED and record.metrics is None and record.holdout_sha256 is None
    assert record.feature_columns == tuple(snapshot.metadata["feature_columns"])
    assert record.target_name == "target_20"
    assert dict(record.hyperparameters) == snapshot.metadata["parameters"]
    assert record.train_start == snapshot.metadata["fit_audit"]["first_fit"]
    assert record.train_end == snapshot.metadata["fit_audit"]["last_fit"]
    assert record.to_dict()["training_report"] == {
        "snapshot_metadata": snapshot.metadata,
        "estimator_sha256": hashlib.sha256(snapshot._model_bytes).hexdigest(),
    }
    assert ModelVersionRegistry(registry.path).get_version(record.model_name, record.model_version) == record
    with pytest.raises(ModelVersionError):
        _ = record.test_start
    with pytest.raises(TypeError):
        record.training_report["snapshot_metadata"]["period_key"] = "fake"


def test_registration_retry_and_timestamp_conflicts_never_rewrite(bound, fitted_snapshot):
    registry, context = bound
    record = context.record
    before = registry.path.read_bytes()
    assert registry.register_version(fitted_snapshot[0], training_run_id=record.training_run_id,
        trained_at_utc=record.trained_at_utc) == record
    for delta in [-1, 1]:
        with pytest.raises(ModelVersionError):
            registry.register_version(fitted_snapshot[0], training_run_id=str(uuid4()),
                trained_at_utc=record.trained_at_utc + timedelta(seconds=delta))
    assert registry.path.read_bytes() == before


def test_activation_is_explicitly_unsupported_without_writes(bound):
    registry, context = bound
    record = context.record
    before = registry.path.read_bytes()
    for activate in [lambda: registry.set_active_version(record.model_name, record.model_version),
            lambda: registry.set_active_version_if_current(record.model_name, record.model_version, None,
                expected_records=(record,))]:
        with pytest.raises(ModelVersionError, match="(?i)activ|promotion|unsupported"):
            activate()
        assert registry.path.read_bytes() == before


@pytest.mark.parametrize("operation", ["prediction", "publication"])
def test_copied_active_snapshot_record_cannot_authorize_inference_or_publication(bound, fitted_inputs, tmp_path, operation):
    registry, context = bound
    forged = replace(context, record=replace(context.record, status=ModelStatus.ACTIVE))
    before = registry.path.read_bytes()
    output = tmp_path / "forged-artifacts"
    with pytest.raises(ModelVersionError):
        if operation == "prediction":
            forged.predict(feature_rows(fitted_inputs))
        else:
            save_model_artifact(forged, output_dir=output)
    assert registry.path.read_bytes() == before and not output.exists()


@pytest.mark.parametrize("bad", ["metrics", "holdout", "active", "features", "target", "family", "bytes"])
def test_closed_record_rejects_fabricated_legacy_or_other_family_claims(bound, bad):
    payload = bound[1].record.to_dict()
    if bad == "metrics": payload["metrics"] = {"brier": .1}
    if bad == "holdout": payload["holdout_sha256"] = "0" * 64
    if bad == "active": payload["status"] = "Active"
    if bad == "features": payload["training_report"]["snapshot_metadata"]["feature_columns"].reverse()
    if bad == "target": payload["training_report"]["snapshot_metadata"]["target_definition"] = "target_up_1d"
    if bad == "family": payload["model_name"] = "random_forest_baseline"
    if bad == "bytes": payload["training_report"]["estimator_sha256"] = "bad"
    with pytest.raises(ModelVersionError):
        decode_record(payload)


def test_registered_prediction_and_policy_use_raw_scores_and_actual_context(bound, fitted_inputs, monkeypatch):
    registry, context = bound
    features = feature_rows(fitted_inputs)
    expected = context._result.predict(features)
    before = registry.path.read_bytes()
    monkeypatch.setattr(HistGradientBoostingClassifier, "fit", forbidden)
    actual = context.predict(features)
    pd.testing.assert_frame_equal(actual.predictions, expected, check_exact=True)
    assert actual.to_dataframe().model_version.eq(context.record.model_version).all()
    from stocklab_ml.boosted_snapshot import policy_decisions
    pd.testing.assert_frame_equal(actual.policy_decisions(features).decisions,
        policy_decisions(expected, features), check_exact=True)
    assert actual.policy_decisions(features).to_dataframe().model_name.eq(context.record.model_name).all()
    blocked = features.assign(close_ma50=0., close_ma200=0.)
    with pytest.raises(ModelVersionError):
        actual.policy_decisions(blocked)
    blocked_predictions = context.predict(blocked)
    held = blocked_predictions.policy_decisions(blocked).decisions
    assert held.action.eq("HOLD").all()
    assert blocked_predictions.predictions.probability_up.lt(.35).any() or blocked_predictions.predictions.probability_up.gt(.65).any()
    pd.testing.assert_series_equal(held.probability_up, blocked_predictions.predictions.probability_up, check_exact=True)
    with pytest.raises(ModelVersionError, match="(?i)historical|frame"):
        context.historical_predictions()
    assert registry.path.read_bytes() == before


@pytest.mark.parametrize("bad", ["changed", "missing", "duplicate", "nonfinite"])
def test_policy_context_is_bound_to_the_actual_prediction_batch(bound, fitted_inputs, bad):
    features = feature_rows(fitted_inputs)
    predictions = bound[1].predict(features)
    context = features.copy()
    if bad == "changed": context.iloc[0, context.columns.get_loc("momentum_20")] += .01
    if bad == "missing": context = context.iloc[1:]
    if bad == "duplicate": context = pd.concat([context, context.iloc[:1]])
    if bad == "nonfinite": context.iloc[0, context.columns.get_loc("close_ma50")] = float("nan")
    with pytest.raises(ModelVersionError):
        predictions.policy_decisions(context)


def test_policy_context_can_include_unchanged_full_history(bound, fitted_inputs):
    features = feature_rows(fitted_inputs)
    predictions = bound[1].predict(features)
    pd.testing.assert_frame_equal(predictions.policy_decisions(fitted_inputs[0]).decisions,
        predictions.policy_decisions(features).decisions, check_exact=True)


def test_registered_artifact_preserves_retained_bytes_without_reserialization(bound, fitted_inputs, tmp_path, monkeypatch):
    registry, context = bound
    features = feature_rows(fitted_inputs)
    expected = context.predict(features).to_dataframe()
    monkeypatch.setattr(HistGradientBoostingClassifier, "fit", forbidden)
    # Snapshot validation serializes only to check mutation; publication must use retained bytes.
    import stocklab_ml.artifacts.serialization as serialization
    monkeypatch.setattr(serialization, "joblib", SimpleNamespace(dump=forbidden, load=joblib.load))
    artifact = save_model_artifact(context, output_dir=tmp_path / "artifacts")
    assert artifact.artifact_path.read_bytes() == context._result._model_bytes
    manifest = json.loads(artifact.manifest_path.read_text())
    assert manifest["artifact_sha256"] == context.record.training_report["estimator_sha256"]
    loaded = load_model_artifact(artifact, registry=registry, trusted_source=True)
    pd.testing.assert_frame_equal(loaded.predict(features).to_dataframe(), expected, check_exact=True)
    pd.testing.assert_frame_equal(loaded.predict(features).policy_decisions(features).decisions,
        context.predict(features).policy_decisions(features).decisions, check_exact=True)
    assert loaded.record == context.record


def test_registered_artifact_wrong_identity_fails_before_unpickle(bound, tmp_path, monkeypatch):
    registry, context = bound
    artifact = save_model_artifact(context, output_dir=tmp_path / "artifacts")
    manifest = json.loads(artifact.manifest_path.read_text())
    manifest["artifact_sha256"] = "0" * 64
    artifact.manifest_path.write_text(json.dumps(manifest))
    monkeypatch.setattr(joblib, "load", forbidden)
    with pytest.raises(ModelArtifactError):
        load_model_artifact(artifact, registry=registry, trusted_source=True)


def test_other_valid_hgb_binary_cannot_replace_registered_bytes(bound, tmp_path, monkeypatch):
    registry, context = bound
    artifact = save_model_artifact(context, output_dir=tmp_path / "artifacts")
    other = deepcopy(context._result._model)
    other._baseline_prediction[0, 0] += .01
    joblib.dump(other, artifact.artifact_path, protocol=5, compress=0)
    data = artifact.artifact_path.read_bytes()
    manifest = json.loads(artifact.manifest_path.read_text())
    manifest.update(artifact_sha256=hashlib.sha256(data).hexdigest(), artifact_size_bytes=len(data))
    artifact.manifest_path.write_text(json.dumps(manifest))
    monkeypatch.setattr(joblib, "load", forbidden)
    with pytest.raises(ModelArtifactError):
        load_model_artifact(artifact, registry=registry, trusted_source=True)


def test_loaded_snapshot_detects_mutated_retained_estimator(bound, fitted_inputs, tmp_path):
    registry, context = bound
    artifact = save_model_artifact(context, output_dir=tmp_path / "artifacts")
    loaded = load_model_artifact(artifact, registry=registry, trusted_source=True)
    loaded.estimator._baseline_prediction[0, 0] += .01
    with pytest.raises((ModelVersionError, ModelArtifactError, ValueError)):
        loaded.predict(feature_rows(fitted_inputs))
