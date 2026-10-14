"""#77 exercises real offline training, immutable lineage and resumable publication."""

from dataclasses import FrozenInstanceError, replace
from datetime import UTC, datetime, timedelta
from importlib import import_module
import json
import os
from pathlib import Path
import subprocess
import sys
from uuid import uuid4

import numpy as np
import pandas as pd
import pytest

from stocklab_ml.data.storage import save_processed
from stocklab_ml.features import engineer_features
from stocklab_ml.modeling import train_random_forest
from stocklab_ml.versioning import ModelStatus, ModelVersionRegistry

NOW = datetime(2024, 7, 1, 16, tzinfo=UTC)


def api():
    return import_module("stocklab_ml.retraining")


def history(count=100, symbol="AAPL"):
    positions = np.arange(count)
    prices = 100 + positions * .07 + 3 * np.sin(positions * .7)
    return pd.DataFrame({
        "date": pd.date_range("2024-01-01", periods=count, freq="B").as_unit("ns"),
        "symbol": pd.array([symbol] * count, dtype="string"),
        "open": prices, "high": prices + 1, "low": prices - 1, "close": prices,
        "volume": pd.array(1000 + positions, dtype="Int64"),
    })


@pytest.fixture
def config(tmp_path):
    save_processed(tmp_path / "dataset.csv", history())
    return api().RetrainingConfig(symbols=("AAPL",), dataset_path=tmp_path / "dataset.csv",
        state_path=tmp_path / "retraining" / "state.json", registry_path=tmp_path / "registry.json",
        artifacts_path=tmp_path / "artifacts")


def run(config, data=None, **kwargs):
    return api().run_retraining_check(config, dependencies=api().RetrainingDependencies(
        local_data=history(103) if data is None else data), now_utc=NOW, **kwargs)


@pytest.mark.parametrize("change", [
    {"symbols": ()}, {"symbols": ("../bad",)}, {"symbols": ("AAPL", "AAPL")},
    {"model_families": ()}, {"model_families": ("unknown",)},
    {"test_fraction": True}, {"test_fraction": float("nan")}, {"test_fraction": 1},
    {"minimum_new_sessions": 0}, {"check_interval_days": False},
    {"overlap_days": 0}, {"maximum_refresh_days": 5000}, {"dry_run": "yes"},
])
def test_invalid_config_fails_before_any_side_effect(tmp_path, change):
    values = dict(symbols=("AAPL",), dataset_path=tmp_path / "data.csv",
        state_path=tmp_path / "state.json", registry_path=tmp_path / "registry.json",
        artifacts_path=tmp_path / "artifacts")
    with pytest.raises(api().RetrainingConfigurationError):
        api().RetrainingConfig(**(values | change))
    assert list(tmp_path.iterdir()) == []


def test_config_is_immutable_and_paths_cannot_alias(config):
    with pytest.raises(FrozenInstanceError):
        config.enabled = False
    with pytest.raises(api().RetrainingConfigurationError):
        replace(config, state_path=config.dataset_path)


def test_no_data_skips_without_fit_version_or_artifact(config, monkeypatch):
    def forbidden(*args, **kwargs):
        pytest.fail("No new sessions must not compute features or fit")
    module = import_module("stocklab_ml.retraining.orchestrator")
    monkeypatch.setattr(module, "engineer_features", forbidden)
    result = run(config, history())
    assert result.status == "SKIPPED_NO_NEW_DATA"
    assert not config.registry_path.exists() and not config.artifacts_path.exists()
    assert result.report["new_session_counts"] == {"AAPL": 0}


def test_three_day_cadence_and_weekend_are_observation_based(config):
    first = run(config, history())
    before = config.state_path.read_bytes()
    second = api().run_retraining_check(config, dependencies=api().RetrainingDependencies(),
        now_utc=NOW + timedelta(days=1))
    assert second.status == "SKIPPED_NOT_DUE" and config.state_path.read_bytes() == before
    weekend = api().run_retraining_check(config, dependencies=api().RetrainingDependencies(local_data=history()),
        now_utc=NOW + timedelta(days=6))
    assert first.status == weekend.status == "SKIPPED_NO_NEW_DATA"


def test_new_sessions_train_both_once_and_keep_existing_active(config, monkeypatch):
    initial = train_random_forest(engineer_features(history()).dataframe)
    registry = ModelVersionRegistry(config.registry_path, clock=lambda: NOW)
    for trained in [initial.baseline_result, initial]:
        record = registry.register_version(trained, training_run_id=str(uuid4()), trained_at_utc=NOW)
        registry.set_active_version(record.model_name, record.model_version)
    before = {name: registry.get_version(name, "v000001").to_dict() for name in
        ["logistic_regression_baseline", "random_forest_baseline"]}
    module = import_module("stocklab_ml.retraining.orchestrator")
    monkeypatch.setattr(module, "train_logistic_regression", lambda *a, **k: pytest.fail("RF retains Logistic"))
    result = run(config)
    assert result.status == "COMPLETED" and result.report["feature_rows"] == 83
    assert result.report["new_session_counts"] == {"AAPL": 3}
    assert len(result.report["model_results"]) == 2
    for model in result.report["model_results"].values():
        name = model["model_name"]
        assert model["model_version"] == "v000002" and model["status"] == "COMPLETED"
        assert registry.get_version(name, "v000001").to_dict() == before[name]
        candidate = registry.get_version(name, "v000002")
        assert candidate.status == ModelStatus.ARCHIVED
        assert model["metrics"] == candidate.to_dict()["metrics"]
        assert candidate.train_end < candidate.test_start
        assert model["artifact_sha256"] and model["s3_reference"] is None
    assert result.report["active_versions_before"] == result.report["active_versions_after"]
    journal_before = config.state_path.read_bytes()
    replay = run(config, retry_run_id=result.run_id)
    assert replay.status == "SKIPPED_ALREADY_PROCESSED"
    assert config.state_path.read_bytes() == journal_before


def test_revision_conflict_does_not_change_dataset_or_registry(config):
    changed = history(103)
    changed.loc[99, ["open", "close"]] += .1
    before = config.dataset_path.read_bytes()
    result = run(config, changed)
    assert result.status == "FAILED" and result.report["failure_category"] == "DATA_REVISION_CONFLICT"
    assert config.dataset_path.read_bytes() == before and not config.registry_path.exists()


def test_minimum_sessions_accumulates_against_training_watermark(config):
    config = replace(config, minimum_new_sessions=4)
    first = run(config, history(103))
    assert first.status == "SKIPPED_INSUFFICIENT_NEW_SESSIONS"
    second = api().run_retraining_check(config, dependencies=api().RetrainingDependencies(local_data=history(104)),
        now_utc=NOW + timedelta(days=3))
    assert second.status == "COMPLETED" and second.report["new_session_counts"] == {"AAPL": 4}


@pytest.mark.parametrize("count", [2, 21, 23])
def test_insufficient_history_never_registers(tmp_path, config, count):
    history(count).to_csv(config.dataset_path, index=False, date_format="%Y-%m-%d")
    result = api().run_retraining_check(config, dependencies=api().RetrainingDependencies(local_data=history(count + 1)),
        now_utc=datetime(2024, 3, 1, 16, tzinfo=UTC))
    assert result.status == "SKIPPED_INSUFFICIENT_DATA" and not config.registry_path.exists()


def test_dry_run_is_read_only_and_offline(config):
    before = config.dataset_path.read_bytes()
    result = run(replace(config, dry_run=True))
    assert result.status == "DRY_RUN_READY" and result.report["api_request_count"] == 0
    assert config.dataset_path.read_bytes() == before
    assert not config.state_path.exists() and not config.registry_path.exists()
    assert not config.artifacts_path.exists() and not config.state_path.parent.exists()


def test_corrupt_journal_fails_closed(config):
    config.state_path.parent.mkdir()
    config.state_path.write_text('{"schema_version":1,"schema_version":1}')
    with pytest.raises(api().RetrainingStateError):
        run(config)
    assert not config.registry_path.exists()


def test_missing_bootstrap_fails_before_fetch(config):
    config.dataset_path.unlink()
    with pytest.raises(api().RetrainingDataError):
        run(config)
    assert not config.state_path.exists()


def test_multi_symbol_refresh_is_shared_and_keeps_missing_sessions(config):
    symbols = ("AAPL", "MSFT", "NVDA")
    baseline = pd.concat([history(symbol=s) for s in symbols], ignore_index=True)
    save_processed(config.dataset_path, baseline, overwrite=True)
    supplied = pd.concat([history(103, s) for s in symbols], ignore_index=True)
    supplied = supplied.drop(supplied[(supplied.symbol == "MSFT")].tail(1).index)
    result = run(replace(config, symbols=symbols), supplied)
    assert result.status == "COMPLETED"
    assert result.report["new_session_counts"] == {"AAPL": 3, "MSFT": 2, "NVDA": 3}
    assert result.report["api_request_count"] == 0
    assert len(pd.read_csv(config.dataset_path)) == 308


def test_provider_failure_is_not_no_data_and_never_leaks_payload(config, monkeypatch):
    from stocklab_ml.data.models import ProviderAuthenticationError
    refresh = import_module("stocklab_ml.retraining.data_refresh")
    def rejected(*args, **kwargs):
        raise ProviderAuthenticationError("private-secret-payload")
    monkeypatch.setattr(refresh, "prepare_historical_dataset", rejected)
    result = api().run_retraining_check(config, dependencies=api().RetrainingDependencies(), now_utc=NOW)
    assert result.status == "FAILED"
    assert result.report["api_request_count"] == 1
    assert "private-secret-payload" not in json.dumps(result.to_dict())
    assert not config.registry_path.exists()


def test_snapshot_is_immutable_and_config_drift_cannot_resume(config, monkeypatch):
    module = import_module("stocklab_ml.retraining.orchestrator")
    def crash(*args, **kwargs):
        raise KeyboardInterrupt
    monkeypatch.setattr(module, "train_random_forest", crash)
    with pytest.raises(KeyboardInterrupt):
        run(config)
    state = json.loads(config.state_path.read_text())
    pending = state["runs"][-1]
    snapshot = config.state_path.parent / "snapshots" / pending["run_id"] / "dataset.csv"
    original = snapshot.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        run(replace(config, test_fraction=.25))
    snapshot.write_bytes(original.replace(b"1000", b"9999", 1))
    with pytest.raises(api().RetrainingStateError):
        run(config)
    assert not config.registry_path.exists()


def test_crash_after_registry_commit_reuses_version_and_training_identity(config, monkeypatch):
    module = import_module("stocklab_ml.retraining.orchestrator")
    config = replace(config, model_families=("logistic_regression",))
    original = module.save_model_artifact
    monkeypatch.setattr(module, "save_model_artifact", lambda *a, **k: (_ for _ in ()).throw(KeyboardInterrupt()))
    with pytest.raises(KeyboardInterrupt):
        run(config)
    registry = ModelVersionRegistry(config.registry_path)
    before = registry.list_versions("logistic_regression_baseline")
    assert len(before) == 1
    monkeypatch.setattr(module, "save_model_artifact", original)
    # New incoming data must not replace the interrupted run's frozen snapshot.
    recovered = run(config, history(106))
    assert recovered.status == "COMPLETED"
    assert registry.list_versions("logistic_regression_baseline") == before
    assert recovered.report["new_session_counts"] == {"AAPL": 3}


def test_partial_family_failure_preserves_logistic_and_recovers_only_missing_family(config, monkeypatch):
    from stocklab_ml.modeling.contracts import ModelTrainingError
    module = import_module("stocklab_ml.retraining.orchestrator")
    original = module.train_random_forest
    monkeypatch.setattr(module, "train_random_forest", lambda *a, **k: (_ for _ in ()).throw(ModelTrainingError("failed")))
    failed = run(config)
    assert failed.status == "PARTIAL_PUBLICATION"
    assert failed.report["model_results"]["logistic_regression"]["status"] == "COMPLETED"
    registry = ModelVersionRegistry(config.registry_path)
    logistic = registry.list_versions("logistic_regression_baseline")
    monkeypatch.setattr(module, "train_random_forest", original)
    recovered = run(config)
    assert recovered.status == "COMPLETED"
    assert registry.list_versions("logistic_regression_baseline") == logistic
    assert len(registry.list_versions("random_forest_baseline")) == 1


def test_future_rows_are_excluded_without_fabricating_sessions(config):
    supplied = history(103)
    future = supplied.tail(1).copy()
    future["date"] = pd.Timestamp("2030-01-01")
    result = run(config, pd.concat([supplied, future], ignore_index=True))
    assert result.status == "COMPLETED" and result.report["new_session_counts"] == {"AAPL": 3}
    assert pd.read_csv(config.dataset_path).date.max() == "2024-05-22"


def test_bounded_provider_request_is_shared_by_models(config, transport):
    supplied = history(103).tail(8)
    transport.body["values"] = [{"datetime": row.date.date().isoformat(),
        **{name: str(getattr(row, name)) for name in ["open", "high", "low", "close", "volume"]}}
        for row in supplied.itertuples()]
    result = api().run_retraining_check(config, dependencies=api().RetrainingDependencies(), now_utc=NOW)
    assert result.status == "COMPLETED"
    assert result.report["api_request_count"] == len(transport.calls) == 1
    from urllib.parse import parse_qs, urlparse
    query = parse_qs(urlparse(transport.calls[0][0].url).query)
    assert query["start_date"] == ["2024-05-10"] and query["end_date"] == ["2024-06-30"]
    assert query["adjust"] == ["splits"] and query["interval"] == ["1day"]
    assert len(result.report["model_results"]) == 2


def test_excessive_incremental_window_fails_without_spending_credits(config, transport):
    result = api().run_retraining_check(replace(config, maximum_refresh_days=10),
        dependencies=api().RetrainingDependencies(), now_utc=NOW)
    assert result.status == "FAILED" and result.report["api_request_count"] == 0
    assert transport.calls == [] and not config.registry_path.exists()


def test_s3_partial_publication_retries_same_artifact_without_fit(config, monkeypatch):
    from test_s3_model_artifacts import FakeS3, store
    client = FakeS3()
    client.fail_manifest = True
    remote = store(client)
    config = replace(config, upload_to_s3=True, model_families=("logistic_regression",))
    dependencies = api().RetrainingDependencies(local_data=history(103), s3_store=remote)
    first = api().run_retraining_check(config, dependencies=dependencies, now_utc=NOW)
    assert first.status == "PARTIAL_PUBLICATION" and len(client.objects) == 1
    model = first.report["model_results"]["logistic_regression"]
    assert model["model_version"] == "v000001" and model["phase"] == "ARTIFACT_SAVED"
    assert model["s3_reference"] is None
    registry_before = config.registry_path.read_bytes()
    module = import_module("stocklab_ml.retraining.orchestrator")
    for name in ["train_logistic_regression", "train_random_forest", "engineer_features"]:
        monkeypatch.setattr(module, name, lambda *a, **k: pytest.fail("Publication retry must reuse verified artifact"))
    client.fail_manifest = False
    second = api().run_retraining_check(config, dependencies=dependencies, now_utc=NOW,
                                      retry_run_id=first.run_id)
    assert second.status == "COMPLETED" and len(client.objects) == 2
    assert config.registry_path.read_bytes() == registry_before
    recovered = second.report["model_results"]["logistic_regression"]
    assert recovered["training_run_id"] == model["training_run_id"]
    assert recovered["artifact_sha256"] == model["artifact_sha256"]
    assert recovered["s3_reference"]["artifact_key"].endswith(model["artifact_sha256"] + ".joblib")


@pytest.mark.parametrize("error_type", ["denied", "bucket", "credentials", "timeout", "throttle", "conflict"])
def test_s3_errors_preserve_version_and_failure_state(config, error_type):
    from test_s3_model_artifacts import FakeS3, store
    from botocore.exceptions import ClientError, NoCredentialsError, ReadTimeoutError
    errors = {"denied": ClientError({"Error": {"Code": "AccessDenied", "Message": "private-secret"}}, "GetObject"),
        "bucket": ClientError({"Error": {"Code": "NoSuchBucket"}}, "GetObject"),
        "credentials": NoCredentialsError(), "timeout": ReadTimeoutError(endpoint_url="https://private.invalid"),
        "throttle": ClientError({"Error": {"Code": "SlowDown"}}, "GetObject"),
        "conflict": ClientError({"Error": {"Code": "ConditionalRequestConflict"}}, "GetObject")}
    client = FakeS3()
    client.error = errors[error_type]
    selected = replace(config, upload_to_s3=True, model_families=("logistic_regression",))
    result = api().run_retraining_check(selected,
        dependencies=api().RetrainingDependencies(history(103), store(client)), now_utc=NOW)
    assert result.status == "PARTIAL_PUBLICATION" and len(client.objects) == 0
    assert "private-secret" not in json.dumps(result.to_dict())
    assert len(ModelVersionRegistry(config.registry_path).list_versions("logistic_regression_baseline")) == 1
    assert result.report["model_results"]["logistic_regression"]["status"] == "FAILED"


def test_two_processes_and_killed_owner_release_os_lock(config):
    environment = os.environ.copy()
    environment["PYTHONPATH"] = str(Path(__file__).resolve().parents[1] / "src")
    command = ("import sys; from pathlib import Path; from stocklab_ml.retraining.state import retraining_lock; "
        "lock=retraining_lock(Path(sys.argv[1])); lock.__enter__(); print('LOCKED', flush=True); sys.stdin.read()")
    holder = subprocess.Popen([sys.executable, "-c", command, str(config.state_path)], env=environment,
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    try:
        assert holder.stdout.readline().strip() == "LOCKED"
        busy = run(config)
        assert busy.status == "SKIPPED_ALREADY_RUNNING" and not config.registry_path.exists()
        assert not config.state_path.exists()
    finally:
        holder.kill()
        holder.communicate(timeout=30)
    assert run(config).status == "COMPLETED"


def test_duplicate_identical_rows_are_deduplicated_before_session_count(config):
    supplied = history(103)
    result = run(config, pd.concat([supplied, supplied.tail(3)], ignore_index=True))
    assert result.status == "COMPLETED" and result.report["new_session_counts"] == {"AAPL": 3}
    assert len(pd.read_csv(config.dataset_path)) == 103


def test_no_lookahead_and_train_only_scaling(config):
    from stocklab_ml.artifacts import load_model_artifact
    from stocklab_ml.artifacts.local_storage import artifact_paths
    from stocklab_ml.modeling import build_supervised_dataset, chronological_holdout_split
    selected = replace(config, model_families=("logistic_regression",))
    result = run(selected)
    assert result.status == "COMPLETED"
    registry = ModelVersionRegistry(config.registry_path)
    loaded = load_model_artifact(artifact_paths("logistic_regression_baseline", "v000001", config.artifacts_path),
                                 registry=registry, trusted_source=True)
    features = engineer_features(history(103)).dataframe
    split = chronological_holdout_split(build_supervised_dataset(features).dataframe)
    assert split.train.target_date.max() < split.test.date.min()
    assert split.test.target_date.max() <= history(103).date.max()
    scaler = loaded.estimator.named_steps["scaler"]
    assert scaler.n_samples_seen_ == len(split.train)
    np.testing.assert_allclose(scaler.mean_, split.train[
        ["return_1d", "ma_5", "ma_20", "rsi_14", "volume", "momentum_10", "volatility_20"]].mean().to_numpy(), rtol=1e-12)


def test_missing_registry_history_cannot_restart_numbering(config):
    assert run(config).status == "COMPLETED"
    config.registry_path.unlink()
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(config, dependencies=api().RetrainingDependencies(local_data=history(106)),
                                  now_utc=NOW + timedelta(days=3))
    assert not config.registry_path.exists()


def test_retry_cannot_publish_to_a_different_private_s3_prefix(config):
    from stocklab_ml.artifacts import S3ArtifactConfig, S3ModelArtifactStore
    from test_s3_model_artifacts import FakeS3, store
    client = FakeS3()
    client.fail_manifest = True
    selected = replace(config, upload_to_s3=True, model_families=("logistic_regression",))
    first = api().run_retraining_check(selected,
        dependencies=api().RetrainingDependencies(history(103), store(client)), now_utc=NOW)
    assert first.status == "PARTIAL_PUBLICATION"
    other = S3ModelArtifactStore(S3ArtifactConfig("stocklab-dev", "ca-central-1", "dev/other", True), client=client)
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(103), other), now_utc=NOW)


def test_config_hardlink_alias_cannot_overwrite_dataset(config):
    linked = config.dataset_path.parent / "aliased-state.json"
    os.link(config.dataset_path, linked)
    with pytest.raises(api().RetrainingConfigurationError):
        replace(config, state_path=linked)


def test_journal_cannot_drop_pending_publication_and_create_another_version(config):
    from test_s3_model_artifacts import FakeS3, store
    client = FakeS3()
    client.fail_manifest = True
    selected = replace(config, upload_to_s3=True, model_families=("logistic_regression",))
    dependencies = api().RetrainingDependencies(history(103), store(client))
    assert api().run_retraining_check(selected, dependencies=dependencies, now_utc=NOW).status == "PARTIAL_PUBLICATION"
    state = json.loads(config.state_path.read_text())
    state["pending_run_id"] = None
    config.state_path.write_text(json.dumps(state))
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(selected, dependencies=dependencies, now_utc=NOW)


def test_journal_cannot_erase_successful_training_watermarks(config):
    assert run(config).status == "COMPLETED"
    state = json.loads(config.state_path.read_text())
    state["successful"] = {}
    config.state_path.write_text(json.dumps(state))
    with pytest.raises(api().RetrainingStateError):
        run(config)


def test_every_partial_recovery_checkpoint_is_readable(config, monkeypatch):
    from stocklab_ml.modeling.contracts import ModelTrainingError
    module = import_module("stocklab_ml.retraining.orchestrator")
    state_api = import_module("stocklab_ml.retraining.state")
    original_write, original_train = module.write_state, module.train_random_forest
    def verified_write(path, state):
        original_write(path, state)
        state_api.read_state(path)
    monkeypatch.setattr(module, "write_state", verified_write)
    monkeypatch.setattr(module, "train_random_forest", lambda *a, **k: (_ for _ in ()).throw(ModelTrainingError()))
    assert run(config).status == "PARTIAL_PUBLICATION"
    monkeypatch.setattr(module, "train_random_forest", original_train)
    assert run(config).status == "COMPLETED"


def test_pending_retry_clock_cannot_precede_durable_run(config, monkeypatch):
    module = import_module("stocklab_ml.retraining.orchestrator")
    with monkeypatch.context() as interrupted:
        interrupted.setattr(module, "train_random_forest", lambda *a, **k: (_ for _ in ()).throw(KeyboardInterrupt()))
        with pytest.raises(KeyboardInterrupt):
            run(config)
    before = config.state_path.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(config, dependencies=api().RetrainingDependencies(history(103)),
                                  now_utc=NOW - timedelta(days=1))
    assert config.state_path.read_bytes() == before


def test_derived_lock_cannot_alias_an_input_after_config_validation(config):
    before = config.dataset_path.read_bytes()
    config.state_path.parent.mkdir()
    lock = config.state_path.with_suffix(".json.lock")
    os.link(config.dataset_path, lock)
    with pytest.raises(api().RetrainingStateError):
        run(config)
    assert config.dataset_path.read_bytes() == before and not config.registry_path.exists()


def test_training_uuid_cannot_change_after_unjournaled_registry_commit(config, monkeypatch):
    selected = replace(config, model_families=("logistic_regression",))
    original = ModelVersionRegistry.register_version
    def crash(registry, *args, **kwargs):
        original(registry, *args, **kwargs)
        raise KeyboardInterrupt
    monkeypatch.setattr(ModelVersionRegistry, "register_version", crash)
    with pytest.raises(KeyboardInterrupt):
        run(selected)
    monkeypatch.setattr(ModelVersionRegistry, "register_version", original)
    state = json.loads(config.state_path.read_text())
    model = state["runs"][-1]["models"]["logistic_regression"]
    assert model["model_version"] is None and model["phase"] == "EVALUATED"
    model["training_run_id"] = str(uuid4())
    config.state_path.write_text(json.dumps(state))
    before = config.registry_path.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        run(selected)
    assert config.registry_path.read_bytes() == before


@pytest.mark.parametrize("field,value", [
    ("trigger_source", "private-secret"), ("new_session_counts", {"AAPL": True}),
    ("active_versions_before", {"unexpected": "bad-version"}),
])
def test_malformed_report_fields_in_journal_fail_closed(config, field, value):
    assert run(config).status == "COMPLETED"
    state = json.loads(config.state_path.read_text())
    state["runs"][-1][field] = value
    config.state_path.write_text(json.dumps(state))
    with pytest.raises(api().RetrainingStateError):
        run(config)


def test_failed_directory_sync_does_not_report_completed(config, monkeypatch):
    state_api = import_module("stocklab_ml.retraining.state")
    def failed(*args, **kwargs):
        raise OSError("fsync directory failed")
    monkeypatch.setattr(state_api, "sync_directory", failed)
    with pytest.raises(api().RetrainingStateError):
        run(config)
    assert not config.registry_path.exists()


@pytest.mark.parametrize("field,value", [("last_check_at_utc", None), ("baseline_dates", {"AAPL": "2030-01-01"})])
def test_journal_cadence_and_bootstrap_reference_cannot_be_reset(config, field, value):
    assert run(config).status == "COMPLETED"
    state = json.loads(config.state_path.read_text())
    state[field] = value
    config.state_path.write_text(json.dumps(state))
    with pytest.raises(api().RetrainingStateError):
        run(config)


def test_completed_s3_reference_cannot_change_identity(config):
    from test_s3_model_artifacts import FakeS3, store
    selected = replace(config, upload_to_s3=True, model_families=("logistic_regression",))
    dependencies = api().RetrainingDependencies(history(103), store(FakeS3()))
    result = api().run_retraining_check(selected, dependencies=dependencies, now_utc=NOW)
    assert result.status == "COMPLETED"
    state = json.loads(config.state_path.read_text())
    state["runs"][-1]["models"]["logistic_regression"]["s3_reference"]["bucket"] = "other-public-source"
    config.state_path.write_text(json.dumps(state))
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(selected, dependencies=dependencies, now_utc=NOW, retry_run_id=result.run_id)


def test_journal_cannot_alias_registry_sentinel_lock(config):
    with pytest.raises(api().RetrainingConfigurationError):
        replace(config, state_path=config.registry_path.with_suffix(".json.lock"))


def test_registry_directory_flush_failure_preserves_unjournaled_identity(config, monkeypatch):
    selected = replace(config, model_families=("logistic_regression",),
                       registry_path=config.registry_path.parent / "registry" / "meta.json")
    module = import_module("stocklab_ml.retraining.orchestrator")
    original_sync = module.sync_directory
    def failed(path):
        if path == selected.registry_path.parent:
            raise OSError("directory flush failed")
        original_sync(path)
    monkeypatch.setattr(module, "sync_directory", failed)
    first = run(selected)
    assert first.status == "FAILED"
    stored = json.loads(selected.state_path.read_text())["runs"][-1]["models"]["logistic_regression"]
    assert stored["model_version"] is None and stored["payload"] is not None
    registry = ModelVersionRegistry(selected.registry_path)
    records = registry.list_versions("logistic_regression_baseline")
    assert len(records) == 1
    monkeypatch.setattr(module, "sync_directory", original_sync)
    assert run(selected).status == "COMPLETED"
    assert registry.list_versions("logistic_regression_baseline") == records
