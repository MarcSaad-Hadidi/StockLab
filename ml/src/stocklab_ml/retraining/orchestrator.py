"""One check, shared causal training, archived candidates and checkpointed recovery."""

from dataclasses import asdict
from datetime import UTC, date, datetime, timedelta
import json
import logging
from uuid import UUID, uuid4, uuid5

from stocklab_ml.artifacts import S3ModelArtifactStore, save_model_artifact
from stocklab_ml.artifacts.local_storage import artifact_paths, read_local
from stocklab_ml.data.storage import save_processed
from stocklab_ml.features import engineer_features
from stocklab_ml.features.models import MIN_OBSERVATIONS
from stocklab_ml.modeling import train_logistic_regression, train_random_forest
from stocklab_ml.modeling.dataset import build_supervised_dataset, chronological_holdout_split
from stocklab_ml.modeling.contracts import ModelDatasetError, ModelTrainingError
from stocklab_ml.versioning import ModelVersionRegistry, ModelVersionError
from stocklab_ml.versioning.validation import canonical, frame_digest, training_payload, validate_timestamp, validate_uuid
from .contracts import (MODEL_NAMES, RetrainingBusyError, RetrainingConfig, RetrainingConfigurationError,
    RetrainingDataError, RetrainingDependencies, RetrainingRunResult, RetrainingStateError)
from .data_refresh import data_dates, load_dataset, refresh_dataset, refresh_windows, session_counts, snapshot_metadata
from .state import (config_identity, identity_digest, read_state, retraining_lock, run_path,
                    sync_directory, timestamp, write_state)

logger = logging.getLogger(__name__)


def _active(registry, families):
    try:
        return {MODEL_NAMES[family]: record.model_version if record else None
                for family in families for record in [registry.get_active_version(MODEL_NAMES[family])]}
    except ModelVersionError:
        raise RetrainingStateError("Registry is corrupt; never initialize replacement history.") from None


def _verify_history(state, registry):
    records = {record.training_run_id: record for name in MODEL_NAMES.values()
               for record in registry.list_versions(name)}
    for run in state["runs"]:
        for model in run["models"].values():
            if model["model_version"] is None:
                continue
            record = records.get(model["training_run_id"])
            if (record is None or record.model_version != model["model_version"]
                    or record.trained_at_utc.isoformat() != model["trained_at_utc"]
                    or canonical(model["payload"]) != canonical({key: record.to_dict()[key] for key in model["payload"]})):
                raise RetrainingStateError("Published registry history is missing or changed; never restart version numbering.")
            if ((run["status"] == "COMPLETED" or run["status"].startswith("SKIPPED_"))
                    and timestamp(run["completed_at_utc"]) < record.registered_at_utc):
                raise RetrainingStateError("Terminal run completion precedes its durable publication checkpoint.")


def _validate_run_clock(now, run, registry):
    checkpoints = [run["started_at_utc"], run["completed_at_utc"]] + [
        model["trained_at_utc"] for model in run["models"].values()]
    if (any(value is not None and now < timestamp(value) for value in checkpoints)
            or any(now < record.registered_at_utc for model in run["models"].values()
                   for record in registry.list_versions(model["model_name"])
                   if record.training_run_id == model["training_run_id"])):
        raise RetrainingStateError("Run clock precedes a durable training/publication checkpoint.")


def _s3_target(dependencies):
    if dependencies.s3_store is None:
        return None
    return {key: value for key, value in asdict(dependencies.s3_store.config).items()
            if key != "trusted_private_source"}


def _checkpoint(config, state, run):
    write_state(config.state_path, state)
    logger.info(json.dumps({"run_id": run["run_id"], "state": run["status"],
        "new_sessions": run["new_session_counts"], "models": {name: {"state": model["phase"],
        "model_name": model["model_name"],
        "model_version": model["model_version"], "result": model["status"]}
        for name, model in run["models"].items()}}))


def _result(config, status, now, run=None, *, active=None, state=None, trigger_source="manual"):
    models = {}
    if run:
        for family, model in run["models"].items():
            split = model["payload"]["training_report"]["split"] if model["payload"] else {}
            local = artifact_paths(model["model_name"], model["model_version"], config.artifacts_path) if model["model_version"] else None
            models[family] = {key: value for key, value in model.items() if key != "payload"} | {
                "training_start": split.get("train_start"), "training_end": split.get("train_end"),
                "test_start": split.get("test_start"), "test_end": split.get("test_end"),
                "artifact_local_path": str(local.artifact_path) if local and model["artifact_sha256"] else None,
                "manifest_path": str(local.manifest_path) if local and model["artifact_sha256"] else None}
    snapshot = run["snapshot"] if run else None
    report = {"started_at_utc": run["started_at_utc"] if run else now.isoformat(),
        "completed_at_utc": now.isoformat(), "status": status,
        "trigger_source": run["trigger_source"] if run else trigger_source, "symbols": list(config.symbols),
        "model_families": list(config.model_families),
        "previous_data_dates": run["previous_data_dates"] if run else {},
        "new_data_dates": snapshot["data_dates"] if snapshot else {},
        "new_session_counts": run["new_session_counts"] if run else {},
        "dataset_fingerprint": snapshot["fingerprint"] if snapshot else None,
        "feature_rows": run["feature_rows"] if run else 0, "model_results": models,
        "active_versions_before": run["active_versions_before"] if run else active or {},
        "active_versions_after": run["active_versions_after"] if run else active or {},
        "api_request_count": run["api_request_count"] if run else 0,
        "failure_category": run["failure_category"] if run else None,
        "last_successful_training_data_dates": {family: value["data_dates"]
            for family, value in (state or {}).get("successful", {}).items()}}
    report["duration_seconds"] = max(0., (now - timestamp(report["started_at_utc"])).total_seconds())
    logger.info(json.dumps({"run_id": run["run_id"] if run else None, "result": status,
                           "duration_seconds": report["duration_seconds"]}))
    return RetrainingRunResult(status, run["run_id"] if run else None, report)


def _new_run(config, frame, now, cutoff, active, trigger_source, dependencies):
    run_id = str(uuid4())
    return {"run_id": run_id, "config": config_identity(config),
        "config_fingerprint": identity_digest(config_identity(config)),
        "started_at_utc": now.isoformat(), "completed_at_utc": None, "status": "CHECKING",
        "trigger_source": trigger_source, "cutoff": cutoff.isoformat(),
        "previous_dataset_fingerprint": frame_digest(frame), "previous_data_dates": data_dates(frame),
        "snapshot": None, "new_session_counts": {}, "feature_rows": 0,
        "models": {family: {"model_name": MODEL_NAMES[family],
            "training_run_id": str(uuid5(UUID(run_id), family)), "phase": "DATA_READY", "status": "PENDING",
            "trained_at_utc": None, "model_version": None, "payload": None, "metrics": None,
            "artifact_sha256": None, "s3_reference": None, "failure_category": None}
            for family in config.model_families}, "active_versions_before": active,
        "active_versions_after": active.copy(), "api_request_count": 0, "failure_category": None,
        "s3_target": _s3_target(dependencies) if config.upload_to_s3 else None}


def _counts(config, state, frame):
    counts = {family: session_counts(frame, state["successful"].get(family,
        {"data_dates": state["baseline_dates"]})["data_dates"]) for family in config.model_families}
    combined = {symbol: max(values[symbol] for values in counts.values()) for symbol in config.symbols}
    eligible = [family for family, values in counts.items() if max(values.values()) >= config.minimum_new_sessions]
    return combined, eligible


def _snapshot(config, state, run, frame, dependencies):
    path = run_path(config, run["run_id"], "snapshots") / "dataset.csv"
    cutoff = date.fromisoformat(run["cutoff"])
    if run["snapshot"] is None:
        if frame_digest(frame) != run["previous_dataset_fingerprint"]:
            raise RetrainingStateError("Bootstrap changed during an interrupted refresh.")
        def before_request():
            run["api_request_count"] += 1
            _checkpoint(config, state, run)
        refreshed = refresh_dataset(config, frame, cutoff=cutoff, local_data=dependencies.local_data,
            run_dir=run_path(config, run["run_id"], "ingestion"), before_request=before_request)
        run["new_session_counts"], eligible = _counts(config, state, refreshed)
        if not any(run["new_session_counts"].values()):
            return None, "SKIPPED_NO_NEW_DATA"
        metadata = snapshot_metadata(refreshed, cutoff)
        if path.exists():
            previous = load_dataset(path, config.symbols, cutoff)
            if frame_digest(previous) != metadata["fingerprint"]:
                raise RetrainingStateError("An interrupted snapshot identifies different data.")
        else:
            save_processed(path, refreshed)
            sync_directory(path.parent)
        run["snapshot"] = metadata
        run["models"] = {family: model for family, model in run["models"].items() if family in eligible}
        run["status"] = "DATA_READY"
        _checkpoint(config, state, run)
    try:
        frozen = load_dataset(path, config.symbols, cutoff)
    except RetrainingDataError:
        raise RetrainingStateError("Training snapshot is missing or corrupt; do not substitute live data.") from None
    if snapshot_metadata(frozen, cutoff) != run["snapshot"]:
        raise RetrainingStateError("Training snapshot fingerprint or metadata changed.")
    if frame_digest(frame) not in {run["previous_dataset_fingerprint"], run["snapshot"]["fingerprint"]}:
        raise RetrainingStateError("Mutable dataset changed outside the pending run.")
    if frame_digest(frame) != run["snapshot"]["fingerprint"]:
        save_processed(config.dataset_path, frozen, overwrite=True)
        sync_directory(config.dataset_path.parent)
    if not run["models"]:
        return None, "SKIPPED_INSUFFICIENT_NEW_SESSIONS"
    return frozen, None


def _existing_artifact(config, registry, model):
    # Find a committed #75 UUID even if the process died before journaling its version.
    matches = [record for record in registry.list_versions(model["model_name"])
               if record.training_run_id == model["training_run_id"]]
    if not matches:
        if model["model_version"] is not None:
            raise RetrainingStateError("Previously registered version is missing; never reset its counter.")
        return None
    record = matches[0]
    stored = record.to_dict()
    if (model["payload"] is None or canonical(model["payload"]) != canonical({key: stored[key] for key in model["payload"]})
            or record.trained_at_utc.isoformat() != model["trained_at_utc"]
            or model["model_version"] not in (None, record.model_version)):
        raise RetrainingStateError("Registered UUID differs from its durable evaluated checkpoint.")
    model["model_version"] = record.model_version
    artifact = artifact_paths(record.model_name, record.model_version, config.artifacts_path)
    if not artifact.manifest_path.exists():
        if model["artifact_sha256"] is not None:
            raise RetrainingStateError("Previously verified local artifact is missing.")
        return None
    manifest, _, _ = read_local(artifact, registry=registry)
    if model["artifact_sha256"] not in (None, manifest["artifact_sha256"]):
        raise RetrainingStateError("Artifact differs from the recorded digest.")
    return artifact


def _failure(error):
    if isinstance(error, RetrainingDataError) and str(error) == "DATA_REVISION_CONFLICT":
        return "DATA_REVISION_CONFLICT"
    # Exception text and chained SDK/provider causes never enter logs or journals.
    return type(error).__name__


def _train(config, state, run, features, needs):
    results = {}
    for family in needs:
        if run["models"][family]["payload"] is None:
            run["models"][family]["phase"] = "TRAINING"
    _checkpoint(config, state, run)
    if "random_forest" in needs:
        try:
            forest = train_random_forest(features, test_fraction=config.test_fraction)
            results["random_forest"] = forest
            if "logistic_regression" in needs and forest.baseline_result is not None:
                results["logistic_regression"] = forest.baseline_result
        except Exception as error:
            run["models"]["random_forest"].update(status="FAILED", failure_category=_failure(error))
    if "logistic_regression" in needs and "logistic_regression" not in results:
        try:
            results["logistic_regression"] = train_logistic_regression(features, test_fraction=config.test_fraction)
        except Exception as error:
            run["models"]["logistic_regression"].update(status="FAILED", failure_category=_failure(error))
    _checkpoint(config, state, run)
    return results


def _publish(config, state, run, family, model, result, artifact, registry, dependencies, clock):
    if model["status"] == "COMPLETED" and artifact is not None:
        return
    if artifact is None:
        if result is None:
            return
        payload = training_payload(result)  # #63 metrics plus #75 fitted/report/holdout validation.
        if model["payload"] is not None and canonical(payload) != canonical(model["payload"]):
            raise RetrainingStateError("Reproduced training differs from the frozen evaluated checkpoint.")
        model.update(payload=payload, metrics=payload["metrics"], status="PENDING", failure_category=None,
                     trained_at_utc=model["trained_at_utc"] or clock().isoformat(), phase="EVALUATED")
        _checkpoint(config, state, run)
        record = registry.register_version(result, training_run_id=model["training_run_id"],
                                           trained_at_utc=timestamp(model["trained_at_utc"]))
        sync_directory(config.registry_path.parent)
        model.update(model_version=record.model_version, phase="VERSION_REGISTERED")
        _checkpoint(config, state, run)
        context = registry.bind_version(result, model_name=record.model_name, model_version=record.model_version)
        artifact = save_model_artifact(context, output_dir=config.artifacts_path, overwrite=True)
    manifest, record, _ = read_local(artifact, registry=registry)
    sync_directory(artifact.artifact_path.parent)
    sync_directory(config.artifacts_path)
    model.update(artifact_sha256=manifest["artifact_sha256"], phase="ARTIFACT_SAVED", status="PENDING", failure_category=None)
    _checkpoint(config, state, run)
    if config.upload_to_s3:
        reference = dependencies.s3_store.upload_model_artifact(artifact, registry=registry)
        model.update(s3_reference=asdict(reference), phase="ARTIFACT_UPLOADED")
        _checkpoint(config, state, run)
    model.update(phase="COMPLETED", status="COMPLETED", failure_category=None)
    state["successful"][family] = {"run_id": run["run_id"], "data_dates": run["snapshot"]["data_dates"],
                                   "fingerprint": run["snapshot"]["fingerprint"]}
    _checkpoint(config, state, run)


def _execute(config, dependencies, now, cutoff, clock, retry_run_id, trigger_source):
    registry = ModelVersionRegistry(config.registry_path, clock=clock)
    active = _active(registry, config.model_families)
    state = read_state(config.state_path)
    if state:
        _verify_history(state, registry)
    if state and state["symbols"] != list(config.symbols):
        raise RetrainingStateError("Journal symbol scope cannot change; initialize a separate explicit scope.")
    if state and state["last_check_at_utc"] and timestamp(state["last_check_at_utc"]) > now:
        raise RetrainingStateError("Check clock precedes the last durable check.")
    run = None
    selected = retry_run_id or (state["pending_run_id"] if state else None)
    if selected:
        run = next((r for r in state["runs"] if r["run_id"] == selected), None) if state else None
        if run is None:
            raise RetrainingStateError("Unknown retry run UUID.")
        _validate_run_clock(now, run, registry)
        if run["config"] != config_identity(config):
            raise RetrainingStateError("Retry configuration differs from the frozen run.")
        if not config.dry_run and run["s3_target"] != (_s3_target(dependencies) if config.upload_to_s3 else None):
            raise RetrainingStateError("Retry S3 target differs from the approved frozen publication destination.")
        if state["pending_run_id"] is None:
            return _result(config, "SKIPPED_ALREADY_PROCESSED", now, run, state=state)
        if selected != state["pending_run_id"]:
            raise RetrainingStateError("Another run is pending; recover it first.")
    elif state and state["last_check_at_utc"] and now - timestamp(state["last_check_at_utc"]) < timedelta(days=config.check_interval_days):
        return _result(config, "SKIPPED_NOT_DUE", now, active=active, state=state, trigger_source=trigger_source)
    frame = load_dataset(config.dataset_path, config.symbols, date.fromisoformat(run["cutoff"]) if run else cutoff)
    if state is None:
        state = {"schema_version": 1, "symbols": list(config.symbols), "baseline_dates": data_dates(frame),
                 "last_check_at_utc": None, "successful": {}, "pending_run_id": None, "runs": []}
    if config.dry_run:
        if run:
            return _result(config, "DRY_RUN_PENDING_RETRY", now, run, state=state)
        if dependencies.local_data is None:
            return _result(config, "DRY_RUN_NEEDS_LOCAL_DATA", now, active=active, state=state, trigger_source=trigger_source)
        run = _new_run(config, frame, now, cutoff, active, trigger_source, dependencies)
        refreshed = refresh_dataset(config, frame, cutoff=cutoff, local_data=dependencies.local_data,
            run_dir=config.state_path.parent, before_request=lambda: None)
        run["new_session_counts"], eligible = _counts(config, state, refreshed)
        run["snapshot"] = snapshot_metadata(refreshed, cutoff)
        if eligible:
            status = "DRY_RUN_READY"
        elif any(run["new_session_counts"].values()):
            status = "DRY_RUN_INSUFFICIENT_NEW_SESSIONS"
        else:
            status = "DRY_RUN_NO_NEW_DATA"
        return _result(config, status, now, run, state=state)
    if run is None:
        if state["runs"]:
            latest = state["runs"][-1]
            expected = latest["snapshot"]["fingerprint"] if latest["snapshot"] else latest["previous_dataset_fingerprint"]
            if frame_digest(frame) != expected:
                raise RetrainingStateError("Dataset changed outside the journal; reconcile explicitly before retraining.")
        refresh_windows(config, frame, cutoff)
        run = _new_run(config, frame, now, cutoff, active, trigger_source, dependencies)
        state["runs"].append(run)
        state["pending_run_id"] = run["run_id"]
        _checkpoint(config, state, run)
    try:
        run.update(status="DATA_READY" if run["snapshot"] else "CHECKING", failure_category=None)
        _checkpoint(config, state, run)
        frozen, skip = _snapshot(config, state, run, frame, dependencies)
        if skip:
            run["status"] = skip
        else:
            artifacts, needs = {}, []
            for family, model in run["models"].items():
                artifacts[family] = _existing_artifact(config, registry, model)
                if artifacts[family] is None:
                    needs.append(family)
            results = {}
            if needs:
                if (frozen.groupby("symbol").size() < MIN_OBSERVATIONS).any():
                    run["status"] = "SKIPPED_INSUFFICIENT_DATA"
                else:
                    features = engineer_features(frozen).dataframe
                    try:
                        chronological_holdout_split(build_supervised_dataset(features).dataframe,
                                                    test_fraction=config.test_fraction)
                    except (ModelDatasetError, ModelTrainingError):
                        run["status"] = "SKIPPED_INSUFFICIENT_DATA"
                    else:
                        run["feature_rows"] = len(features)
                        results = _train(config, state, run, features, needs)
            if run["status"] != "SKIPPED_INSUFFICIENT_DATA":
                for family, model in run["models"].items():
                    try:
                        _publish(config, state, run, family, model, results.get(family), artifacts[family],
                                 registry, dependencies, clock)
                    except RetrainingStateError:
                        raise
                    except Exception as error:
                        model.update(status="FAILED", failure_category=_failure(error))
                        _checkpoint(config, state, run)
                successful = all(model["status"] == "COMPLETED" for model in run["models"].values())
                run["status"] = "COMPLETED" if successful else "PARTIAL_PUBLICATION" if any(
                    model["model_version"] for model in run["models"].values()) else "FAILED"
                run["failure_category"] = next((model["failure_category"] for model in run["models"].values()
                                                if model["status"] == "FAILED"), None)
    except RetrainingStateError:
        raise
    except Exception as error:
        run.update(status="FAILED", failure_category=_failure(error))
    run["active_versions_after"] = _active(registry, config.model_families)
    if run["active_versions_after"] != run["active_versions_before"]:
        run.update(status="FAILED", failure_category="ACTIVE_CHANGED_EXTERNALLY")
    completed = clock()
    _validate_run_clock(completed, run, registry)
    run["completed_at_utc"] = completed.isoformat()
    if run["status"] == "COMPLETED" or run["status"].startswith("SKIPPED_"):
        state.update(pending_run_id=None, last_check_at_utc=completed.isoformat())
    _checkpoint(config, state, run)
    return _result(config, run["status"], completed, run, state=state)


def run_retraining_check(config: RetrainingConfig, *, dependencies: RetrainingDependencies,
                         now_utc: datetime | None = None, retry_run_id: str | None = None,
                         trigger_source: str = "manual") -> RetrainingRunResult:
    """Perform one iteration; pending failures resume the same UUID before cadence checks."""
    from .scheduler import validate_execution_context
    if not isinstance(config, RetrainingConfig) or not isinstance(dependencies, RetrainingDependencies):
        raise RetrainingConfigurationError("Validated retraining configuration and dependencies are required.")
    now = now_utc if now_utc is not None else datetime.now(UTC)
    try:
        validate_timestamp(now)
        if retry_run_id is not None:
            validate_uuid(retry_run_id)
    except Exception:
        raise RetrainingConfigurationError("UTC clock and retry UUID must be explicit and canonical.") from None
    if trigger_source not in {"manual", "scheduler", "offline_smoke"}:
        raise RetrainingConfigurationError("Unsupported trigger source.")
    clock = (lambda: now) if now_utc is not None else lambda: datetime.now(UTC)
    # UTC-12 makes the cutoff conservative even just after UTC midnight; #56
    # independently requires a boundary preceding the execution host's today.
    cutoff = min((now - timedelta(hours=12)).date(), date.today()) - timedelta(days=1)
    validate_execution_context(config)
    if not config.enabled:
        return _result(config, "SKIPPED_DISABLED", now, trigger_source=trigger_source)
    if config.upload_to_s3 and not config.dry_run and not isinstance(dependencies.s3_store, S3ModelArtifactStore):
        raise RetrainingConfigurationError("S3 publication requires an explicitly approved #76 artifact store.")
    if config.dry_run:
        return _execute(config, dependencies, now, cutoff, clock, retry_run_id, trigger_source)
    try:
        with retraining_lock(config.state_path):
            return _execute(config, dependencies, now, cutoff, clock, retry_run_id, trigger_source)
    except RetrainingBusyError:
        return _result(config, "SKIPPED_ALREADY_RUNNING", now, trigger_source=trigger_source)
