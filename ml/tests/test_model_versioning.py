"""Real training results, permanent local identities, and safe registry mutations."""

from concurrent.futures import ThreadPoolExecutor
from dataclasses import FrozenInstanceError, asdict, replace
from datetime import UTC, datetime, timedelta, timezone
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
from sklearn.ensemble import RandomForestClassifier
from sklearn.linear_model import LogisticRegression

from stocklab_ml.features import FEATURE_COLUMNS
from stocklab_ml.modeling import evaluate_predictions, train_random_forest
from test_model_dataset import feature_frame

TRAINED = datetime(2026, 10, 14, 14, tzinfo=UTC)
REGISTERED = datetime(2026, 10, 14, 15, tzinfo=UTC)


def api():
    return import_module("stocklab_ml.versioning")


@pytest.fixture(scope="module")
def models():
    forest = train_random_forest(feature_frame())
    return forest.baseline_result, forest


@pytest.fixture
def registry(tmp_path):
    return api().ModelVersionRegistry(tmp_path / "registry.json", clock=lambda: REGISTERED)


def register(registry, result, run_id=None, **kwargs):
    return registry.register_version(result, training_run_id=run_id or str(uuid4()),
                                     trained_at_utc=TRAINED, **kwargs)


@pytest.mark.parametrize("index", [0, 1])
def test_first_second_version_metadata_and_real_metrics(registry, models, index):
    result = models[index]
    before = result.predictions.copy(deep=True)
    first = register(registry, result)
    second = register(registry, result)
    assert (first.version_number, first.model_version, first.status) == (1, "v000001", api().ModelStatus.ARCHIVED)
    assert (second.version_number, second.model_version) == (2, "v000002")
    assert first.trained_at_utc == TRAINED and first.registered_at_utc == REGISTERED
    assert first.training_date == "2026-10-14"
    assert first.train_end != first.training_date
    assert first.feature_columns == tuple(FEATURE_COLUMNS)
    assert first.target_name == "target_up_1d"
    assert dict(first.hyperparameters) == result.report.hyperparameters
    assert first.train_start == result.report.split.train_start
    assert first.train_end == result.report.split.train_end
    assert first.test_start == result.report.split.test_start
    assert first.test_end == result.report.split.test_end
    assert first.split_date == result.report.split.split_date
    assert first.metrics == evaluate_predictions(model_name=result.report.model_name, predictions=result.predictions)
    assert first.model_type == ["logistic_regression", "random_forest"][index]
    assert registry.list_versions(first.model_name) == (first, second)
    assert registry.get_version(first.model_name, first.model_version) == first
    assert registry.get_active_version(first.model_name) is None
    assert len(json.loads(registry.path.read_text())["models"]) == 2
    pd.testing.assert_frame_equal(result.predictions, before)


def test_families_have_separate_sequences_and_active_statuses(registry, models):
    logistic, forest = models
    l1, l2 = register(registry, logistic), register(registry, logistic)
    r1 = register(registry, forest)
    assert r1.model_version == l1.model_version == "v000001"
    registry.set_active_version(l2.model_name, l2.model_version)
    registry.set_active_version(r1.model_name, r1.model_version)
    assert registry.get_active_version(l2.model_name).model_version == "v000002"
    assert registry.get_active_version(r1.model_name).model_version == "v000001"
    assert len(registry.list_versions(logistic.report.model_name)) == 2
    assert len(registry.list_versions(forest.report.model_name)) == 1  # RF's comparator is not registered.


def test_retry_after_activation_returns_existing_without_write(registry, models):
    run_id = str(uuid4())
    first = register(registry, models[0], run_id)
    active = registry.set_active_version(first.model_name, first.model_version)
    before = registry.path.read_bytes()
    assert register(registry, models[0], run_id) == active
    assert registry.path.read_bytes() == before
    assert register(registry, models[0]).model_version == "v000002"


@pytest.mark.parametrize("change", ["trained_at", "training_metadata", "predictions", "family"])
def test_conflicting_training_run_never_changes_history(registry, models, change):
    run_id = str(uuid4())
    first = register(registry, models[0], run_id)
    before = registry.path.read_bytes()
    result = models[0]
    trained = TRAINED
    if change == "trained_at":
        trained += timedelta(seconds=1)
    elif change == "training_metadata":
        result = replace(result, report=replace(result.report, split=replace(result.report.split, train_start="2023-12-01")))
    elif change == "predictions":
        frame = result.predictions.copy(deep=True)
        frame.loc[0, "probability_up"] *= .99  # Same accuracy; a changed batch still conflicts.
        result = replace(result, predictions=frame)
    else:
        result = models[1]
    with pytest.raises(api().TrainingRunConflictError):
        registry.register_version(result, training_run_id=run_id, trained_at_utc=trained)
    assert registry.path.read_bytes() == before
    assert registry.get_version(first.model_name, first.model_version) == first


def test_status_transitions_preserve_all_metadata_and_never_reuse_versions(registry, models):
    records = [register(registry, models[0]) for _ in range(3)]
    name = records[0].model_name
    registry.set_active_version(name, "v000002")
    assert [r.status for r in registry.list_versions(name)] == [api().ModelStatus.ARCHIVED, api().ModelStatus.ACTIVE,
                                                             api().ModelStatus.ARCHIVED]
    registry.set_active_version(name, "v000003")
    registry.archive_version(name, "v000003")
    assert registry.get_active_version(name) is None
    assert register(registry, models[0]).model_version == "v000004"
    for original in records:
        restored = registry.get_version(name, original.model_version)
        assert restored == original


def test_new_better_version_does_not_promote(registry, models):
    first = register(registry, models[0])
    registry.set_active_version(first.model_name, first.model_version)
    predictions = models[0].predictions.copy(deep=True)
    predictions["predicted_class"] = predictions.actual_class
    improved = replace(models[0], predictions=predictions, report=replace(models[0].report, test_accuracy=1.0))
    second = register(registry, improved)
    assert second.metrics.accuracy > first.metrics.accuracy
    assert second.status == api().ModelStatus.ARCHIVED
    assert registry.get_active_version(first.model_name).model_version == first.model_version


def test_restart_restores_metadata_status_and_counter_without_training(registry, models, monkeypatch):
    first, second = register(registry, models[1]), register(registry, models[1])
    registry.set_active_version(first.model_name, first.model_version)
    def forbidden(*args, **kwargs):
        raise AssertionError("Registry must never fit")
    monkeypatch.setattr(LogisticRegression, "fit", forbidden)
    monkeypatch.setattr(RandomForestClassifier, "fit", forbidden)
    restored = api().ModelVersionRegistry(registry.path, clock=lambda: REGISTERED)
    assert restored.get_active_version(first.model_name).metrics == first.metrics
    assert restored.get_version(second.model_name, second.model_version) == second
    assert register(restored, models[1]).model_version == "v000003"


def test_record_and_nested_metadata_are_immutable(registry, models):
    record = register(registry, models[0])
    with pytest.raises(FrozenInstanceError):
        record.metrics.accuracy = .5
    with pytest.raises(FrozenInstanceError):
        record.trained_at_utc = REGISTERED
    with pytest.raises(TypeError):
        record.hyperparameters["C"] = 123
    with pytest.raises(TypeError):
        record.training_report["split"]["train_end"] = "1900-01-01"
    exported = record.to_dict()
    exported["training_report"]["feature_columns"].reverse()
    assert record.feature_columns == tuple(FEATURE_COLUMNS)


@pytest.mark.parametrize("trained", [None, "2026-10-14", datetime(2026, 10, 14),
    datetime(2026, 10, 14, tzinfo=timezone(timedelta(hours=-4)))])
def test_training_timestamp_must_be_explicit_aware_utc(registry, models, trained):
    with pytest.raises(api().ModelVersionError):
        registry.register_version(models[0], training_run_id=str(uuid4()), trained_at_utc=trained)
    assert not registry.path.exists()


@pytest.mark.parametrize("run_id", ["", "latest", "../run", "bad-uuid", None, str(uuid4()).upper()])
def test_invalid_training_run_uuid_rejected(registry, models, run_id):
    with pytest.raises(api().ModelVersionError):
        registry.register_version(models[0], training_run_id=run_id, trained_at_utc=TRAINED)
    assert not registry.path.exists()


@pytest.mark.parametrize("name", ["", " a", "a ", "../model", "a/b", "a\\b", "a\n", "a" * 129, None])
def test_model_name_validation_on_every_lookup(registry, name):
    with pytest.raises(api().ModelVersionError):
        registry.list_versions(name)


@pytest.mark.parametrize("version", ["", "latest", "current", "v1", "v000000", "v0000001", "v000001 ", "../v000001", None])
def test_version_must_be_exact_canonical_identity(registry, version):
    with pytest.raises(api().ModelVersionError):
        registry.get_version("logistic_regression_baseline", version)


def test_unknown_exact_version_never_falls_back_to_active(registry, models):
    first = register(registry, models[0])
    registry.set_active_version(first.model_name, first.model_version)
    before = registry.path.read_bytes()
    for operation in [registry.get_version, registry.set_active_version, registry.archive_version]:
        with pytest.raises(api().ModelVersionNotFoundError):
            operation(first.model_name, "v999999")
    assert registry.path.read_bytes() == before


@pytest.mark.parametrize("field,value", [("feature_columns", list(reversed(FEATURE_COLUMNS))),
    ("model_name", "random_forest_baseline"), ("model_name", ""), ("target_name", "different"),
    ("target_definition", "different"), ("prediction_timing", "before close"), ("split_rule", "random"),
    ("test_accuracy", np.nan), ("test_accuracy", .12345), ("hyperparameters", {"C": 2})])
def test_training_report_mismatches_fail_before_writing(registry, models, field, value):
    result = replace(models[0], report=replace(models[0].report, **{field: value}))
    with pytest.raises(api().ModelVersionError):
        register(registry, result)
    assert not registry.path.exists()


@pytest.mark.parametrize("field,value", [("test_rows", 999), ("test_rows", True), ("test_start", "2000-01-01"),
    ("test_end", "2000-01-01"), ("split_date", "2000-01-01"), ("train_end", "2026-10-14"),
    ("test_rows_per_symbol", {}), ("test_positive_rate", .123), ("test_fraction", np.inf)])
def test_inconsistent_holdout_metadata_rejected(registry, models, field, value):
    report = replace(models[0].report, split=replace(models[0].report.split, **{field: value}))
    with pytest.raises(api().ModelVersionError):
        register(registry, replace(models[0], report=report))
    assert not registry.path.exists()


def test_wrong_estimator_type_rejected(registry, models):
    with pytest.raises(api().ModelVersionError):
        register(registry, replace(models[0], pipeline=models[1].model))
    with pytest.raises(api().ModelVersionError):
        register(registry, replace(models[1], model=models[0].pipeline))


@pytest.mark.parametrize("index", [0, 1])
def test_model_specific_report_values_must_describe_the_supplied_estimator(registry, models, index):
    result = models[index]
    changes = ({"iterations": [999]} if index == 0
               else {"feature_importances": dict.fromkeys(FEATURE_COLUMNS, 1 / 7)})
    with pytest.raises(api().ModelVersionError):
        register(registry, replace(result, report=replace(result.report, **changes)))
    assert not registry.path.exists()


def test_public_registration_helper(tmp_path, models):
    record = api().register_model_version(models[0], training_run_id=str(uuid4()), trained_at_utc=TRAINED,
                                           registry_path=tmp_path / "registry.json")
    assert record.model_version == "v000001"


def test_concurrent_registrations_and_activations_are_serialized(registry, models):
    def retry_busy(operation):
        for _ in range(10):
            try:
                return operation()
            except api().RegistryBusyError:
                continue
        raise AssertionError("Local writers did not finish after bounded explicit retries")
    def create(_):
        separate_instance = api().ModelVersionRegistry(registry.path, clock=lambda: REGISTERED)
        run_id = str(uuid4())
        return retry_busy(lambda: register(separate_instance, models[0], run_id))
    with ThreadPoolExecutor(max_workers=4) as pool:
        records = list(pool.map(create, range(8)))
    assert sorted(r.version_number for r in records) == list(range(1, 9))
    assert len(registry.list_versions(records[0].model_name)) == 8
    with ThreadPoolExecutor(max_workers=4) as pool:
        list(pool.map(lambda r: retry_busy(lambda: registry.set_active_version(r.model_name, r.model_version)), records))
    assert sum(r.status == api().ModelStatus.ACTIVE for r in registry.list_versions(records[0].model_name)) == 1


@pytest.mark.parametrize("bad", ["json", "schema", "duplicate_json_key", "duplicate_version", "duplicate_run",
    "two_active", "status", "metric", "plausible_metric", "count", "date", "features", "family", "gap"])
def test_corrupt_registry_is_never_reset_or_overwritten(registry, models, bad):
    first, second = register(registry, models[0]), register(registry, models[0])
    data = json.loads(registry.path.read_text())
    if bad == "json":
        content = "{broken"
    elif bad == "duplicate_json_key":
        content = '{"schema_version":1,"schema_version":1,"models":[]}'
    else:
        a, b = data["models"]
        if bad == "schema": data["schema_version"] = True
        elif bad == "duplicate_version": b["model_version"], b["version_number"] = a["model_version"], a["version_number"]
        elif bad == "duplicate_run": b["training_run_id"] = a["training_run_id"]
        elif bad == "two_active": a["status"] = b["status"] = "Active"
        elif bad == "status": a["status"] = "latest"
        elif bad == "metric": a["metrics"]["precision"] = float("nan")
        elif bad == "plausible_metric": a["metrics"]["precision"] = .12345
        elif bad == "count": a["metrics"]["true_positive"] = -1
        elif bad == "date": a["trained_at_utc"] = "2026-10-14"
        elif bad == "features": a["training_report"]["feature_columns"].reverse()
        elif bad == "family": b["model_type"] = "random_forest"
        else: b["version_number"], b["model_version"] = 3, "v000003"
        content = json.dumps(data)
    registry.path.write_text(content)
    before = registry.path.read_bytes()
    restored = api().ModelVersionRegistry(registry.path, clock=lambda: REGISTERED)
    for operation in [lambda: restored.list_versions(first.model_name),
                      lambda: register(restored, models[0]),
                      lambda: restored.set_active_version(second.model_name, second.model_version)]:
        with pytest.raises(api().ModelVersionError):
            operation()
        assert registry.path.read_bytes() == before


@pytest.mark.parametrize("failure", ["replace", "fsync", "permission"])
def test_failed_write_preserves_previous_registry_and_retry_identity(registry, models, monkeypatch, failure):
    from stocklab_ml.data import storage
    first = register(registry, models[0])
    registry.set_active_version(first.model_name, first.model_version)
    before = registry.path.read_bytes()
    run_id = str(uuid4())
    def fail(*args, **kwargs):
        raise PermissionError("synthetic permission failure") if failure == "permission" else OSError("interrupted")
    with monkeypatch.context() as patch:
        patch.setattr(storage.os, "fsync" if failure == "fsync" else "replace", fail)
        with pytest.raises(api().ModelVersionError):
            register(registry, models[0], run_id)
        assert registry.path.read_bytes() == before
        with pytest.raises(api().ModelVersionError):
            registry.archive_version(first.model_name, first.model_version)
        assert registry.path.read_bytes() == before
    second = register(registry, models[0], run_id)
    assert second.model_version == "v000002"
    assert registry.get_active_version(first.model_name).model_version == "v000001"
    assert not list(registry.path.parent.glob("*.tmp"))
    assert not registry.path.with_suffix(".json.lock").exists()


def test_lock_timeout_fails_safely_without_stealing_lock(registry, models, monkeypatch):
    first = register(registry, models[0])
    before = registry.path.read_bytes()
    lock = registry.path.with_suffix(".json.lock")
    lock.write_text("live writer")
    monkeypatch.setattr(import_module("stocklab_ml.versioning.registry"), "LOCK_TIMEOUT_SECONDS", .02)
    with pytest.raises(api().RegistryBusyError):
        register(registry, models[0])
    assert lock.read_text() == "live writer"
    assert registry.path.read_bytes() == before
    assert registry.get_version(first.model_name, first.model_version) == first


def test_invalid_registry_destination_fails_without_success(tmp_path, models):
    blocker = tmp_path / "not_a_directory"
    blocker.write_text("keep")
    registry = api().ModelVersionRegistry(blocker / "registry.json")
    with pytest.raises(api().ModelVersionError):
        register(registry, models[0])
    assert blocker.read_text() == "keep"


def test_transient_windows_lock_permission_race_is_retried(registry, models, monkeypatch):
    module = import_module("stocklab_ml.versioning.registry")
    original = module.os.open
    denied = False
    def open_lock(path, *args, **kwargs):
        nonlocal denied
        if str(path).endswith("registry.json.lock") and not denied:
            denied = True
            raise PermissionError("Windows delete-pending lock")
        return original(path, *args, **kwargs)
    monkeypatch.setattr(module.os, "open", open_lock)
    assert register(registry, models[0]).model_version == "v000001"


def test_separate_processes_allocate_distinct_versions(registry):
    script = """
import socket, sys
from datetime import UTC, datetime
from uuid import uuid4
from stocklab_ml.modeling import train_logistic_regression
from stocklab_ml.versioning import ModelVersionRegistry
from test_model_dataset import feature_frame
def forbidden(*args, **kwargs):
    raise AssertionError('Network forbidden in registry worker')
socket.socket.connect = socket.socket.connect_ex = forbidden
result = train_logistic_regression(feature_frame())
record = ModelVersionRegistry(sys.argv[1]).register_version(
    result, training_run_id=str(uuid4()), trained_at_utc=datetime(2026, 10, 14, 14, tzinfo=UTC))
print(record.model_version)
"""
    root = Path(__file__).resolve().parents[1]
    environment = os.environ | {"PYTHONPATH": os.pathsep.join([str(root / "src"), str(root / "tests")])}
    def worker(_):
        return subprocess.run([sys.executable, "-c", script, str(registry.path)], env=environment,
                              capture_output=True, text=True, check=True, timeout=120).stdout.strip()
    with ThreadPoolExecutor(max_workers=2) as pool:
        assert set(pool.map(worker, range(2))) == {"v000001", "v000002"}
    assert len(registry.list_versions("logistic_regression_baseline")) == 2
