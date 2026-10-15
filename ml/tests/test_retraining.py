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


@pytest.fixture
def active_registry(config):
    from stocklab_ml.modeling import train_logistic_regression
    selected = replace(config, model_families=("logistic_regression",))
    result = train_logistic_regression(engineer_features(history()).dataframe)
    registry = ModelVersionRegistry(config.registry_path, clock=lambda: NOW)
    for _ in range(2):
        record = registry.register_version(result, training_run_id=str(uuid4()), trained_at_utc=NOW)
    registry.set_active_version(record.model_name, "v000001")
    return selected, registry, record.model_name


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


@pytest.mark.parametrize("trigger", ["manual", "scheduler", "offline_smoke"])
@pytest.mark.parametrize("status", ["SKIPPED_DISABLED", "SKIPPED_NOT_DUE",
    "SKIPPED_ALREADY_RUNNING", "DRY_RUN_NEEDS_LOCAL_DATA"])
def test_runless_reports_preserve_invocation_trigger(config, status, trigger):
    from contextlib import nullcontext
    from stocklab_ml.retraining.state import retraining_lock
    selected, lock = config, nullcontext()
    if status == "SKIPPED_DISABLED":
        selected = replace(config, enabled=False)
    elif status == "SKIPPED_NOT_DUE":
        assert run(config, history()).status == "SKIPPED_NO_NEW_DATA"
    elif status == "SKIPPED_ALREADY_RUNNING":
        lock = retraining_lock(config.state_path)
    else:
        selected = replace(config, dry_run=True)
    before = config.dataset_path.read_bytes(), config.state_path.read_bytes() if config.state_path.exists() else None
    with lock:
        result = api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(),
            now_utc=NOW, trigger_source=trigger)
    assert result.status == status and result.run_id is None
    assert result.report["trigger_source"] == trigger
    assert before == (config.dataset_path.read_bytes(), config.state_path.read_bytes() if config.state_path.exists() else None)
    assert not config.registry_path.exists() and not config.artifacts_path.exists()


def test_replayed_run_report_preserves_original_trigger(config):
    first = run(config, history(), trigger_source="offline_smoke")
    before = config.state_path.read_bytes()
    replay = run(config, history(), retry_run_id=first.run_id, trigger_source="scheduler")
    assert replay.status == "SKIPPED_ALREADY_PROCESSED"
    assert first.report["trigger_source"] == replay.report["trigger_source"] == "offline_smoke"
    assert config.state_path.read_bytes() == before


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


@pytest.mark.parametrize("outcome", ["skip", "complete", "training_failure"])
def test_external_active_change_preserves_check_outcome(active_registry, monkeypatch, caplog, outcome):
    from stocklab_ml.modeling.contracts import ModelTrainingError
    selected, registry, name = active_registry
    module = import_module("stocklab_ml.retraining.orchestrator")
    original_refresh = module.refresh_dataset
    def refreshed_then_promoted(*args, **kwargs):
        refreshed = original_refresh(*args, **kwargs)
        registry.set_active_version(name, "v000002")
        return refreshed
    caplog.set_level("INFO", logger=module.__name__)
    incoming = history() if outcome == "skip" else history(103)
    with monkeypatch.context() as attempt:
        attempt.setattr(module, "refresh_dataset", refreshed_then_promoted)
        if outcome == "training_failure":
            attempt.setattr(module, "train_logistic_regression", lambda *a, **k: (_ for _ in ()).throw(ModelTrainingError()))
        first = run(selected, incoming)
    expected = {"skip": "SKIPPED_NO_NEW_DATA", "complete": "COMPLETED", "training_failure": "FAILED"}[outcome]
    assert first.status == expected
    assert first.report["failure_category"] == ("ModelTrainingError" if outcome == "training_failure" else None)
    assert first.report["active_versions_before"] == {name: "v000001"}
    assert first.report["active_versions_after"] == {name: "v000002"}
    assert any(json.loads(record.message).get("result") == "ACTIVE_CHANGED_EXTERNALLY" for record in caplog.records)
    state = import_module("stocklab_ml.retraining.state").read_state(selected.state_path)
    assert state["pending_run_id"] == (first.run_id if outcome == "training_failure" else None)
    second = api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(incoming),
                                       now_utc=NOW + timedelta(days=3))
    assert second.status == ("COMPLETED" if outcome == "training_failure" else "SKIPPED_NO_NEW_DATA")
    assert registry.get_active_version(name).model_version == "v000002"
    assert len(registry.list_versions(name)) == (2 if outcome == "skip" else 3)
    if outcome != "skip":
        assert registry.get_version(name, "v000003").status == ModelStatus.ARCHIVED
    assert json.loads(selected.state_path.read_text())["pending_run_id"] is None


def test_existing_external_active_failure_can_resume_without_reverting_promotion(active_registry):
    selected, registry, name = active_registry
    result = run(selected)
    assert result.status == "COMPLETED"
    registry.set_active_version(name, "v000002")
    state = json.loads(selected.state_path.read_text())
    state["runs"][-1].update(status="FAILED", failure_category="ACTIVE_CHANGED_EXTERNALLY",
                              active_versions_after={name: "v000002"})
    state.update(pending_run_id=result.run_id, last_check_at_utc=None)
    selected.state_path.write_text(json.dumps(state))
    before = selected.registry_path.read_bytes()
    recovered = api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(103)),
                                          now_utc=NOW + timedelta(days=3))
    assert recovered.status == "COMPLETED" and recovered.run_id == result.run_id
    assert selected.registry_path.read_bytes() == before
    assert json.loads(selected.state_path.read_text())["pending_run_id"] is None
    assert registry.get_active_version(name).model_version == "v000002" and len(registry.list_versions(name)) == 3


def test_revision_conflict_does_not_change_dataset_or_registry(config):
    changed = history(103)
    changed.loc[99, ["open", "close"]] += .1
    before = config.dataset_path.read_bytes()
    result = run(config, changed)
    assert result.status == "FAILED" and result.report["failure_category"] == "DATA_REVISION_CONFLICT"
    assert config.dataset_path.read_bytes() == before and not config.registry_path.exists()


def test_revision_conflict_closes_run_and_preserves_lineage_during_reconciliation(config):
    from stocklab_ml.modeling import train_logistic_regression
    selected = replace(config, model_families=("logistic_regression",))
    registry = ModelVersionRegistry(config.registry_path, clock=lambda: NOW)
    initial = train_logistic_regression(engineer_features(history()).dataframe)
    record = registry.register_version(initial, training_run_id=str(uuid4()), trained_at_utc=NOW)
    registry.set_active_version(record.model_name, record.model_version)
    active_before = registry.get_active_version(record.model_name).to_dict()
    assert run(selected, history()).status == "SKIPPED_NO_NEW_DATA"
    incoming = history(103)
    incoming.loc[99, ["open", "close"]] += .1
    old_dataset, old_registry = config.dataset_path.read_bytes(), config.registry_path.read_bytes()
    conflict = api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(incoming),
                                         now_utc=NOW + timedelta(days=3))
    assert conflict.status == "FAILED" and conflict.report["failure_category"] == "DATA_REVISION_CONFLICT"
    state = import_module("stocklab_ml.retraining.state").read_state(config.state_path)
    assert state["pending_run_id"] is None and state["last_check_at_utc"] == (NOW + timedelta(days=3)).isoformat()
    old_journal = config.state_path.read_bytes()
    replay = api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(incoming),
        now_utc=NOW + timedelta(days=3), retry_run_id=conflict.run_id)
    assert replay.status == "SKIPPED_ALREADY_PROCESSED" and config.state_path.read_bytes() == old_journal
    reconciled = replace(selected, dataset_path=config.dataset_path.parent / "reconciled.csv",
                         state_path=config.state_path.parent / "reconciled" / "state.json")
    save_processed(reconciled.dataset_path, incoming.iloc[:100].copy())
    result = api().run_retraining_check(reconciled, dependencies=api().RetrainingDependencies(incoming),
                                       now_utc=NOW + timedelta(days=3))
    assert result.status == "COMPLETED"
    assert result.report["model_results"]["logistic_regression"]["model_version"] == "v000002"
    assert registry.get_version(record.model_name, "v000002").status == ModelStatus.ARCHIVED
    assert registry.get_active_version(record.model_name).to_dict() == active_before
    assert config.state_path.read_bytes() == old_journal and config.dataset_path.read_bytes() == old_dataset
    assert old_registry != config.registry_path.read_bytes() and len(registry.list_versions(record.model_name)) == 2


@pytest.mark.parametrize("erase_snapshot", [False, True])
def test_revision_conflict_cannot_discard_published_pending_work(config, erase_snapshot):
    from test_s3_model_artifacts import FakeS3, store
    client = FakeS3()
    client.fail_manifest = True
    selected = replace(config, model_families=("logistic_regression",), upload_to_s3=True)
    dependencies = api().RetrainingDependencies(history(103), store(client))
    assert api().run_retraining_check(selected, dependencies=dependencies, now_utc=NOW).status == "PARTIAL_PUBLICATION"
    state = json.loads(config.state_path.read_text())
    state["runs"][-1].update(status="FAILED", failure_category="DATA_REVISION_CONFLICT")
    if erase_snapshot:
        state["runs"][-1]["snapshot"] = None
    state.update(pending_run_id=None, last_check_at_utc=NOW.isoformat())
    config.state_path.write_text(json.dumps(state))
    before = config.state_path.read_bytes(), config.registry_path.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(selected, dependencies=dependencies, now_utc=NOW + timedelta(days=3))
    assert before == (config.state_path.read_bytes(), config.registry_path.read_bytes())


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


@pytest.mark.parametrize("symbol,watermark", [("AAPL", "2099-01-01"), ("AAPL", "2023-12-31"),
    ("AAPL", "2024-05-16"), ("MSFT", "2024-05-28"), ("AAPL", "2024-05-21"), ("AAPL", "2024-05-23")])
def test_snapshot_watermarks_must_describe_the_recorded_range(config, symbol, watermark):
    baseline = pd.concat([history(), history(105, "MSFT")], ignore_index=True)
    save_processed(config.dataset_path, baseline, overwrite=True)
    selected = replace(config, symbols=("AAPL", "MSFT"), model_families=("logistic_regression",))
    incoming = pd.concat([history(103), history(108, "MSFT")], ignore_index=True)
    assert run(selected, incoming).status == "COMPLETED"
    state = json.loads(config.state_path.read_text())
    state["runs"][-1]["snapshot"]["data_dates"][symbol] = watermark
    state["successful"]["logistic_regression"]["data_dates"][symbol] = watermark
    config.state_path.write_text(json.dumps(state))
    before = config.state_path.read_bytes(), config.dataset_path.read_bytes(), config.registry_path.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(incoming),
                                  now_utc=NOW + timedelta(days=3))
    assert before == (config.state_path.read_bytes(), config.dataset_path.read_bytes(), config.registry_path.read_bytes())


def test_older_snapshot_watermarks_are_verified_from_the_snapshot(config):
    baseline = pd.concat([history(), history(105, "MSFT")], ignore_index=True)
    save_processed(config.dataset_path, baseline, overwrite=True)
    selected = replace(config, symbols=("AAPL", "MSFT"), model_families=("logistic_regression",))
    assert run(selected, pd.concat([history(103), history(108, "MSFT")], ignore_index=True)).status == "COMPLETED"
    incoming = pd.concat([history(104), history(109, "MSFT")], ignore_index=True)
    assert api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(incoming),
                                     now_utc=NOW + timedelta(days=3)).status == "COMPLETED"
    state = json.loads(config.state_path.read_text())
    state["runs"][0]["snapshot"]["data_dates"]["AAPL"] = "2024-05-21"
    config.state_path.write_text(json.dumps(state))
    before = config.state_path.read_bytes(), config.registry_path.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(incoming),
                                  now_utc=NOW + timedelta(days=6))
    assert before == (config.state_path.read_bytes(), config.registry_path.read_bytes())


def test_completed_snapshot_bytes_must_match_the_recorded_fingerprint(config):
    selected = replace(config, model_families=("logistic_regression",))
    result = run(selected)
    path = config.state_path.parent / "snapshots" / result.run_id / "dataset.csv"
    changed = history(103)
    changed.loc[99, "volume"] += 1
    save_processed(path, changed, overwrite=True)
    before = config.state_path.read_bytes(), config.registry_path.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(103)),
                                  now_utc=NOW + timedelta(days=3))
    assert before == (config.state_path.read_bytes(), config.registry_path.read_bytes())


@pytest.mark.parametrize("has_run", [False, True])
@pytest.mark.parametrize("watermark", ["2099-01-01", "2024-05-16"])
def test_bootstrap_watermarks_are_bound_to_observed_data(config, watermark, has_run):
    baseline = pd.concat([history(), history(105, "MSFT")], ignore_index=True)
    save_processed(config.dataset_path, baseline, overwrite=True)
    selected = replace(config, symbols=("AAPL", "MSFT"), model_families=("logistic_regression",))
    if has_run:
        assert run(selected, baseline).status == "SKIPPED_NO_NEW_DATA"
        state = json.loads(config.state_path.read_text())
        state["runs"][0]["previous_data_dates"]["AAPL"] = watermark
    else:
        config.state_path.parent.mkdir()
        state = {"schema_version": 1, "symbols": ["AAPL", "MSFT"],
            "baseline_dates": {"AAPL": "2024-05-17", "MSFT": "2024-05-24"},
            "last_check_at_utc": None, "successful": {}, "pending_run_id": None, "runs": []}
    state["baseline_dates"]["AAPL"] = watermark
    config.state_path.write_text(json.dumps(state))
    before = config.state_path.read_bytes(), config.dataset_path.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(baseline),
                                  now_utc=NOW + timedelta(days=3))
    assert before == (config.state_path.read_bytes(), config.dataset_path.read_bytes())
    assert not config.registry_path.exists() and not config.artifacts_path.exists()


@pytest.mark.parametrize("family", ["logistic_regression", "random_forest"])
@pytest.mark.parametrize("erase_config", [False, True])
def test_published_family_lineage_cannot_be_erased(config, family, erase_config):
    from stocklab_ml.retraining.state import identity_digest
    assert run(config).status == "COMPLETED"
    state = json.loads(config.state_path.read_text())
    completed = state["runs"][-1]
    model = completed["models"].pop(family)
    state["successful"].pop(family)
    if erase_config:
        completed["config"]["model_families"].remove(family)
        completed["config_fingerprint"] = identity_digest(completed["config"])
        for key in ["active_versions_before", "active_versions_after"]:
            completed[key].pop(model["model_name"])
    config.state_path.write_text(json.dumps(state))
    before = config.state_path.read_bytes(), config.dataset_path.read_bytes(), config.registry_path.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(config, dependencies=api().RetrainingDependencies(history(103)),
                                  now_utc=NOW + timedelta(days=3))
    assert before == (config.state_path.read_bytes(), config.dataset_path.read_bytes(), config.registry_path.read_bytes())


def test_pending_run_cannot_erase_a_completed_family(config, monkeypatch):
    from stocklab_ml.modeling.contracts import ModelTrainingError
    module = import_module("stocklab_ml.retraining.orchestrator")
    with monkeypatch.context() as failed:
        failed.setattr(module, "train_random_forest", lambda *a, **k: (_ for _ in ()).throw(ModelTrainingError()))
        assert run(config).status == "PARTIAL_PUBLICATION"
    state = json.loads(config.state_path.read_text())
    state["runs"][-1]["models"].pop("logistic_regression")
    state["successful"].pop("logistic_regression")
    config.state_path.write_text(json.dumps(state))
    before = config.state_path.read_bytes(), config.registry_path.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(config, dependencies=api().RetrainingDependencies(history(103)),
                                  now_utc=NOW + timedelta(days=3))
    assert before == (config.state_path.read_bytes(), config.registry_path.read_bytes())


@pytest.mark.parametrize("older_entry", [False, True])
@pytest.mark.parametrize("checkpoint", ["training", "registration", "before_start"])
def test_terminal_history_must_enclose_publication_timestamps(config, checkpoint, older_entry):
    selected = replace(config, model_families=("logistic_regression",))
    assert run(selected).status == "COMPLETED"
    if older_entry:
        assert api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(103)),
            now_utc=NOW + timedelta(days=3)).status == "SKIPPED_NO_NEW_DATA"
    state = json.loads(config.state_path.read_text())
    registry = json.loads(config.registry_path.read_text())
    record = registry["models"][0]
    if checkpoint == "before_start":
        record["trained_at_utc"] = (NOW - timedelta(days=1)).isoformat()
    else:
        record["registered_at_utc"] = (NOW + timedelta(days=9)).isoformat()
        if checkpoint == "training":
            record["trained_at_utc"] = record["registered_at_utc"]
    state["runs"][0]["models"]["logistic_regression"]["trained_at_utc"] = record["trained_at_utc"]
    config.state_path.write_text(json.dumps(state))
    config.registry_path.write_text(json.dumps(registry))
    assert len(ModelVersionRegistry(config.registry_path).list_versions(record["model_name"])) == 1
    before = config.state_path.read_bytes(), config.dataset_path.read_bytes(), config.registry_path.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(104)),
                                  now_utc=NOW + timedelta(days=6))
    assert before == (config.state_path.read_bytes(), config.dataset_path.read_bytes(), config.registry_path.read_bytes())


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


@pytest.mark.parametrize("has_history", [False, True])
def test_excessive_incremental_window_allows_explicit_catch_up(config, transport, has_history):
    config = replace(config, model_families=("logistic_regression",))
    if has_history:
        assert api().run_retraining_check(config, dependencies=api().RetrainingDependencies(history()),
            now_utc=NOW - timedelta(days=37)).status == "SKIPPED_NO_NEW_DATA"
    before = config.dataset_path.read_bytes(), config.state_path.read_bytes() if config.state_path.exists() else None
    with pytest.raises(api().RetrainingDataError, match="REFRESH_RANGE_EXCEEDED"):
        api().run_retraining_check(replace(config, maximum_refresh_days=10),
            dependencies=api().RetrainingDependencies(), now_utc=NOW)
    assert before == (config.dataset_path.read_bytes(), config.state_path.read_bytes() if config.state_path.exists() else None)
    assert transport.calls == [] and not config.registry_path.exists() and not config.artifacts_path.exists()
    recovered = api().run_retraining_check(replace(config, maximum_refresh_days=60),
        dependencies=api().RetrainingDependencies(history(130)), now_utc=NOW)
    assert recovered.status == "COMPLETED" and recovered.report["new_session_counts"] == {"AAPL": 30}
    assert recovered.report["api_request_count"] == 0 and transport.calls == []
    state = json.loads(config.state_path.read_text())
    assert len(state["runs"]) == 1 + has_history and state["pending_run_id"] is None
    assert state["baseline_dates"] == {"AAPL": "2024-05-17"}


def test_all_symbol_windows_checked_before_any_provider_request(config, transport):
    baseline = pd.concat([history(130), history(100, "MSFT")], ignore_index=True)
    save_processed(config.dataset_path, baseline, overwrite=True)
    selected = replace(config, symbols=("AAPL", "MSFT"), maximum_refresh_days=10)
    before = config.dataset_path.read_bytes()
    with pytest.raises(api().RetrainingDataError, match="REFRESH_RANGE_EXCEEDED"):
        api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(), now_utc=NOW)
    assert transport.calls == [] and config.dataset_path.read_bytes() == before
    assert not config.state_path.exists() and not config.registry_path.exists()


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


@pytest.mark.parametrize("status", ["COMPLETED", "SKIPPED_NO_NEW_DATA",
    "SKIPPED_INSUFFICIENT_NEW_SESSIONS", "SKIPPED_INSUFFICIENT_DATA"])
def test_terminal_run_cannot_erase_both_cadence_timestamps(config, status):
    selected = replace(config, model_families=("logistic_regression",),
        minimum_new_sessions=4 if status == "SKIPPED_INSUFFICIENT_NEW_SESSIONS" else 1)
    clock, incoming = NOW, history(103)
    if status == "SKIPPED_NO_NEW_DATA":
        incoming = history()
    elif status == "SKIPPED_INSUFFICIENT_DATA":
        save_processed(selected.dataset_path, history(21), overwrite=True)
        clock, incoming = datetime(2024, 3, 1, 16, tzinfo=UTC), history(22)
    first = api().run_retraining_check(selected,
        dependencies=api().RetrainingDependencies(incoming), now_utc=clock)
    assert first.status == status
    state = json.loads(selected.state_path.read_text())
    state["runs"][-1]["completed_at_utc"] = state["last_check_at_utc"] = None
    selected.state_path.write_text(json.dumps(state))
    before = selected.state_path.read_bytes(), selected.dataset_path.read_bytes(), (
        selected.registry_path.read_bytes() if selected.registry_path.exists() else None)
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(selected,
            dependencies=api().RetrainingDependencies(incoming), now_utc=clock + timedelta(days=1))
    assert before == (selected.state_path.read_bytes(), selected.dataset_path.read_bytes(),
        selected.registry_path.read_bytes() if selected.registry_path.exists() else None)


def test_older_terminal_run_also_requires_completion_timestamp(config):
    assert run(config, history()).status == "SKIPPED_NO_NEW_DATA"
    assert api().run_retraining_check(config, dependencies=api().RetrainingDependencies(history()),
        now_utc=NOW + timedelta(days=3)).status == "SKIPPED_NO_NEW_DATA"
    state = json.loads(config.state_path.read_text())
    state["runs"][0]["completed_at_utc"] = None
    config.state_path.write_text(json.dumps(state))
    with pytest.raises(api().RetrainingStateError):
        import_module("stocklab_ml.retraining.state").read_state(config.state_path)


@pytest.mark.parametrize("invalid_order", ["backdated", "overlapping", "unfinished_predecessor"])
def test_journal_rejects_runs_out_of_chronological_order(config, invalid_order):
    assert run(config, history()).status == "SKIPPED_NO_NEW_DATA"
    assert api().run_retraining_check(config, dependencies=api().RetrainingDependencies(history()),
        now_utc=NOW + timedelta(days=3)).status == "SKIPPED_NO_NEW_DATA"
    state = json.loads(config.state_path.read_text())
    first, latest = state["runs"]
    if invalid_order == "backdated":
        latest["started_at_utc"] = latest["completed_at_utc"] = (NOW - timedelta(days=3)).isoformat()
        state["last_check_at_utc"] = latest["completed_at_utc"]
    elif invalid_order == "overlapping":
        latest["started_at_utc"] = (NOW - timedelta(seconds=1)).isoformat()
    else:
        first.update(status="CHECKING", completed_at_utc=None)
        state["pending_run_id"] = first["run_id"]
    config.state_path.write_text(json.dumps(state))
    before = config.state_path.read_bytes(), config.dataset_path.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(config, dependencies=api().RetrainingDependencies(history(103)),
                                  now_utc=NOW + timedelta(days=4))
    assert before == (config.state_path.read_bytes(), config.dataset_path.read_bytes())
    assert not config.registry_path.exists()


def test_pending_run_cannot_precede_a_later_completed_run(config):
    assert run(config, history()).status == "SKIPPED_NO_NEW_DATA"
    assert api().run_retraining_check(config, dependencies=api().RetrainingDependencies(history()),
        now_utc=NOW + timedelta(days=3)).status == "SKIPPED_NO_NEW_DATA"
    state = json.loads(config.state_path.read_text())
    state["runs"][0]["status"] = "FAILED"
    state["pending_run_id"] = state["runs"][0]["run_id"]
    config.state_path.write_text(json.dumps(state))
    with pytest.raises(api().RetrainingStateError):
        import_module("stocklab_ml.retraining.state").read_state(config.state_path)


def test_interrupted_retry_preserves_last_attempt_completion(config, monkeypatch):
    from test_s3_model_artifacts import FakeS3, store
    selected = replace(config, model_families=("logistic_regression",), upload_to_s3=True)
    client = FakeS3()
    client.fail_manifest = True
    dependencies = api().RetrainingDependencies(history(103), store(client))
    for day in [0, 3]:
        assert api().run_retraining_check(selected, dependencies=dependencies,
            now_utc=NOW + timedelta(days=day)).status == "PARTIAL_PUBLICATION"
    module = import_module("stocklab_ml.retraining.orchestrator")
    with monkeypatch.context() as interrupted:
        interrupted.setattr(module, "_existing_artifact", lambda *a, **k: (_ for _ in ()).throw(KeyboardInterrupt()))
        with pytest.raises(KeyboardInterrupt):
            api().run_retraining_check(selected, dependencies=dependencies, now_utc=NOW + timedelta(days=4))
    before = selected.state_path.read_bytes(), selected.registry_path.read_bytes()
    client.fail_manifest = False
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(selected, dependencies=dependencies, now_utc=NOW + timedelta(days=2))
    assert before == (selected.state_path.read_bytes(), selected.registry_path.read_bytes())
    state = json.loads(before[0])
    assert state["runs"][-1]["completed_at_utc"] == (NOW + timedelta(days=3)).isoformat()


def test_retry_clock_cannot_precede_checkpointed_model_training(config, monkeypatch):
    from stocklab_ml.modeling.contracts import ModelTrainingError
    selected = replace(config, model_families=("logistic_regression",))
    module = import_module("stocklab_ml.retraining.orchestrator")
    with monkeypatch.context() as failed:
        failed.setattr(module, "train_logistic_regression", lambda *a, **k: (_ for _ in ()).throw(ModelTrainingError()))
        assert run(selected).status == "FAILED"
    original_save = module.save_model_artifact
    def saved_then_interrupted(*args, **kwargs):
        original_save(*args, **kwargs)
        raise KeyboardInterrupt
    with monkeypatch.context() as interrupted:
        interrupted.setattr(module, "save_model_artifact", saved_then_interrupted)
        with pytest.raises(KeyboardInterrupt):
            api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(103)),
                                      now_utc=NOW + timedelta(days=3))
    before = selected.state_path.read_bytes(), selected.registry_path.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(103)),
                                  now_utc=NOW + timedelta(days=2))
    assert before == (selected.state_path.read_bytes(), selected.registry_path.read_bytes())


@pytest.mark.parametrize("prior_attempt", [False, True])
def test_training_clock_is_validated_before_publication(config, monkeypatch, prior_attempt):
    from types import SimpleNamespace
    from stocklab_ml.modeling.contracts import ModelTrainingError
    selected = replace(config, model_families=("logistic_regression",))
    module = import_module("stocklab_ml.retraining.orchestrator")
    if prior_attempt:
        with monkeypatch.context() as failed:
            failed.setattr(module, "train_logistic_regression", lambda *a, **k: (_ for _ in ()).throw(ModelTrainingError()))
            for day in [0, 3]:
                assert api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(103)),
                    now_utc=NOW + timedelta(days=day)).status == "FAILED"
    entry = NOW + timedelta(days=4) if prior_attempt else NOW
    trained = NOW + timedelta(days=2) if prior_attempt else NOW - timedelta(seconds=1)
    times = iter([entry, trained, entry + timedelta(seconds=1), entry + timedelta(seconds=2)])
    with monkeypatch.context() as shifted:
        shifted.setattr(module, "datetime", SimpleNamespace(now=lambda tz: next(times)))
        with pytest.raises(api().RetrainingStateError):
            api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(103)))
    assert not config.registry_path.exists() and not config.artifacts_path.exists()
    state = import_module("stocklab_ml.retraining.state").read_state(config.state_path)
    assert state["runs"][-1]["models"]["logistic_regression"]["trained_at_utc"] is None
    assert api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(103)),
        now_utc=entry + timedelta(seconds=3)).status == "COMPLETED"
    assert len(ModelVersionRegistry(config.registry_path).list_versions("logistic_regression_baseline")) == 1


def test_system_clock_cannot_backdate_terminal_completion(config, monkeypatch):
    from types import SimpleNamespace
    selected = replace(config, model_families=("logistic_regression",))
    module = import_module("stocklab_ml.retraining.orchestrator")
    times = iter([NOW, NOW + timedelta(seconds=2), NOW + timedelta(seconds=3), NOW + timedelta(seconds=1)])
    with monkeypatch.context() as shifted:
        shifted.setattr(module, "datetime", SimpleNamespace(now=lambda tz: next(times)))
        with pytest.raises(api().RetrainingStateError):
            api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(103)))
    assert api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(103)),
        now_utc=NOW + timedelta(seconds=5)).status == "COMPLETED"
    assert len(ModelVersionRegistry(selected.registry_path).list_versions("logistic_regression_baseline")) == 1


def test_retry_clock_cannot_precede_unjournaled_registration(config, monkeypatch):
    from stocklab_ml.versioning import ModelVersionError
    selected = replace(config, model_families=("logistic_regression",))
    original_register = ModelVersionRegistry.register_version
    with monkeypatch.context() as failed:
        failed.setattr(ModelVersionRegistry, "register_version", lambda *a, **k: (_ for _ in ()).throw(ModelVersionError()))
        assert run(selected).status == "FAILED"
    def registered_then_interrupted(*args, **kwargs):
        original_register(*args, **kwargs)
        raise KeyboardInterrupt
    with monkeypatch.context() as interrupted:
        interrupted.setattr(ModelVersionRegistry, "register_version", registered_then_interrupted)
        with pytest.raises(KeyboardInterrupt):
            api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(103)),
                                      now_utc=NOW + timedelta(days=3))
    before = selected.state_path.read_bytes(), selected.registry_path.read_bytes()
    with pytest.raises(api().RetrainingStateError):
        api().run_retraining_check(selected, dependencies=api().RetrainingDependencies(history(103)),
                                  now_utc=NOW + timedelta(days=2))
    assert before == (selected.state_path.read_bytes(), selected.registry_path.read_bytes())


@pytest.mark.parametrize("new_sessions,expected", [(0, "DRY_RUN_NO_NEW_DATA"),
    (3, "DRY_RUN_INSUFFICIENT_NEW_SESSIONS"), (4, "DRY_RUN_READY")])
def test_dry_run_distinguishes_session_threshold_without_writes(config, new_sessions, expected):
    before = config.dataset_path.read_bytes()
    result = run(replace(config, dry_run=True, minimum_new_sessions=4), history(100 + new_sessions))
    assert result.status == expected and result.report["new_session_counts"] == {"AAPL": new_sessions}
    assert result.report["api_request_count"] == 0 and config.dataset_path.read_bytes() == before
    assert not config.state_path.parent.exists() and not config.registry_path.exists()
    assert not config.artifacts_path.exists()


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
