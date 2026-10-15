"""Atomic durable journal and OS-owned nonblocking lock; corruption fails closed."""

from contextlib import contextmanager
from dataclasses import asdict
from datetime import datetime
import errno
import hashlib
import json
import os
from pathlib import Path
import re
from uuid import UUID, uuid5

from stocklab_ml.artifacts.local_storage import read_bounded
from stocklab_ml.artifacts import S3ArtifactConfig, S3ModelArtifactStore
from stocklab_ml.data.storage import save_raw
from stocklab_ml.data.validation import calendar_date
from stocklab_ml.modeling.contracts import ModelEvaluationMetrics
from stocklab_ml.versioning.registry import _unique_keys
from stocklab_ml.versioning.validation import (canonical, validate_report, validate_timestamp,
    validate_uuid, validate_version)
from .contracts import MODEL_NAMES, RetrainingConfig, RetrainingBusyError, RetrainingStateError

PHASES = {"DATA_READY", "TRAINING", "EVALUATED", "VERSION_REGISTERED", "ARTIFACT_SAVED",
          "ARTIFACT_UPLOADED", "COMPLETED"}
RUN_STATUSES = {"CHECKING", "DATA_READY", "FAILED", "PARTIAL_PUBLICATION", "COMPLETED",
                "SKIPPED_NO_NEW_DATA", "SKIPPED_INSUFFICIENT_NEW_SESSIONS", "SKIPPED_INSUFFICIENT_DATA"}
MODEL_STATUSES = {"PENDING", "FAILED", "COMPLETED"}


def read_json(path: Path, limit=8 * 1024 * 1024):
    # shortcut: journal reads cap at 8 MiB; migrate preserved lineage before reaching that limit.
    try:
        if not path.exists() and not os.path.lexists(path):
            return None
        return json.loads(read_bounded(path, limit).decode("utf-8"), object_pairs_hook=_unique_keys,
                          parse_constant=lambda value: (_ for _ in ()).throw(ValueError("Nonfinite JSON")))
    except Exception:
        raise RetrainingStateError("Unreadable/corrupt JSON; restore verified state explicitly, never reset it.") from None


def config_identity(config) -> dict:
    return {key: str(value) if isinstance(value, Path) else list(value) if isinstance(value, tuple) else value
            for key, value in asdict(config).items() if key not in {"dry_run", "enabled"}}


def identity_digest(identity) -> str:
    return hashlib.sha256(canonical(identity).encode()).hexdigest()


def timestamp(value):
    parsed = datetime.fromisoformat(value)
    validate_timestamp(parsed)
    if parsed.isoformat() != value:
        raise ValueError
    return parsed


def _digest(value):
    if not isinstance(value, str) or re.fullmatch(r"[a-f0-9]{64}", value) is None:
        raise ValueError


def _dates(values, symbols):
    if not isinstance(values, dict) or set(values) != set(symbols):
        raise ValueError
    for value in values.values():
        calendar_date(value)


def read_state(path: Path):
    state = read_json(path)
    if state is None:
        return None
    try:
        if (set(state) != {"schema_version", "symbols", "baseline_dates", "last_check_at_utc",
                           "successful", "pending_run_id", "runs"}
                or type(state["schema_version"]) is not int or state["schema_version"] != 1
                or not isinstance(state["symbols"], list) or not state["symbols"]
                or len(set(state["symbols"])) != len(state["symbols"])
                or not isinstance(state["runs"], list) or not isinstance(state["successful"], dict)):
            raise ValueError
        symbols = state["symbols"]
        _dates(state["baseline_dates"], symbols)
        if state["last_check_at_utc"] is not None:
            timestamp(state["last_check_at_utc"])
        ids, training_ids = set(), set()
        for run in state["runs"]:
            if set(run) != {"run_id", "config", "config_fingerprint", "started_at_utc", "completed_at_utc",
                           "status", "trigger_source", "cutoff", "previous_dataset_fingerprint",
                           "previous_data_dates", "snapshot", "new_session_counts", "feature_rows",
                           "models", "active_versions_before", "active_versions_after", "api_request_count",
                           "failure_category", "s3_target"}:
                raise ValueError
            validate_uuid(run["run_id"])
            if run["run_id"] in ids or run["status"] not in RUN_STATUSES:
                raise ValueError
            ids.add(run["run_id"])
            if run["config_fingerprint"] != identity_digest(run["config"]):
                raise ValueError
            if config_identity(RetrainingConfig(**run["config"])) != run["config"]:
                raise ValueError
            if run["config"]["upload_to_s3"]:
                if (not isinstance(run["s3_target"], dict)
                        or set(run["s3_target"]) != {"bucket", "region", "prefix", "max_artifact_bytes"}):
                    raise ValueError
                S3ArtifactConfig(**run["s3_target"], trusted_private_source=True)
            elif run["s3_target"] is not None:
                raise ValueError
            timestamp(run["started_at_utc"])
            if run["completed_at_utc"] is not None:
                if timestamp(run["completed_at_utc"]) < timestamp(run["started_at_utc"]):
                    raise ValueError
            cutoff = calendar_date(run["cutoff"])
            _digest(run["previous_dataset_fingerprint"])
            _dates(run["previous_data_dates"], symbols)
            if run["trigger_source"] not in {"manual", "scheduler", "offline_smoke"}:
                raise ValueError
            counts = run["new_session_counts"]
            if (not isinstance(counts, dict) or counts and set(counts) != set(symbols)
                    or any(type(count) is not int or count < 0 for count in counts.values())):
                raise ValueError
            for key in ["active_versions_before", "active_versions_after"]:
                active = run[key]
                if not isinstance(active, dict) or set(active) != {MODEL_NAMES[family] for family in run["config"]["model_families"]}:
                    raise ValueError
                for version in active.values():
                    if version is not None:
                        validate_version(version)
            if (type(run["api_request_count"]) is not int or run["api_request_count"] < 0
                    or type(run["feature_rows"]) is not int or run["feature_rows"] < 0):
                raise ValueError
            if run["snapshot"] is not None:
                snap = run["snapshot"]
                if set(snap) != {"symbols", "date_min", "date_max", "rows", "fingerprint", "data_dates",
                                 "cutoff", "preparation"} or snap["symbols"] != symbols:
                    raise ValueError
                _digest(snap["fingerprint"])
                _dates(snap["data_dates"], symbols)
                if (type(snap["rows"]) is not int or snap["rows"] < 1
                        or not calendar_date(snap["date_min"]) <= calendar_date(snap["date_max"]) <= cutoff
                        or snap["cutoff"] != run["cutoff"]):
                    raise ValueError
            if not set(run["models"]).issubset(run["config"]["model_families"]):
                raise ValueError
            for family, model in run["models"].items():
                if (set(model) != {"model_name", "training_run_id", "phase", "status", "trained_at_utc",
                                  "model_version", "payload", "metrics", "artifact_sha256", "s3_reference",
                                  "failure_category"}
                        or family not in MODEL_NAMES or model["model_name"] != MODEL_NAMES[family]
                        or model["phase"] not in PHASES or model["status"] not in MODEL_STATUSES):
                    raise ValueError
                validate_uuid(model["training_run_id"])
                if model["training_run_id"] != str(uuid5(UUID(run["run_id"]), family)):
                    raise ValueError
                if model["training_run_id"] in training_ids:
                    raise ValueError
                training_ids.add(model["training_run_id"])
                if model["trained_at_utc"] is not None:
                    timestamp(model["trained_at_utc"])
                if model["model_version"] is not None:
                    validate_version(model["model_version"])
                if model["artifact_sha256"] is not None:
                    _digest(model["artifact_sha256"])
                if model["s3_reference"] is not None:
                    if run["s3_target"] is None or model["model_version"] is None or model["artifact_sha256"] is None:
                        raise ValueError
                    target = S3ArtifactConfig(**run["s3_target"], trusted_private_source=True)
                    store = S3ModelArtifactStore(target, client=object())  # Pure #76 key derivation, no AWS client.
                    manifest_key, artifact_key = store._keys(model["model_name"], model["model_version"], model["artifact_sha256"])
                    if model["s3_reference"] != {"bucket": target.bucket, "region": target.region,
                        "artifact_key": artifact_key, "manifest_key": manifest_key, "artifact_sha256": model["artifact_sha256"]}:
                        raise ValueError
                if model["payload"] is not None:
                    payload = model["payload"]
                    if (set(payload) != {"model_name", "model_type", "training_report", "metrics", "holdout_sha256"}
                            or payload["model_type"] != family or payload["model_name"] != MODEL_NAMES[family]
                            or model["metrics"] != payload["metrics"] or model["trained_at_utc"] is None):
                        raise ValueError
                    _digest(payload["holdout_sha256"])
                    validate_report(family, payload["training_report"], ModelEvaluationMetrics(**payload["metrics"]))
                elif model["phase"] not in {"DATA_READY", "TRAINING"}:
                    raise ValueError
                if model["status"] == "COMPLETED" and (model["phase"] != "COMPLETED"
                        or model["artifact_sha256"] is None or model["model_version"] is None
                        or run["config"]["upload_to_s3"] and model["s3_reference"] is None):
                    raise ValueError
                if model["phase"] == "COMPLETED" and model["status"] != "COMPLETED":
                    raise ValueError
            if run["status"] == "COMPLETED" and (not run["models"] or run["snapshot"] is None
                    or any(model["status"] != "COMPLETED" for model in run["models"].values())):
                raise ValueError
        for previous, current in zip(state["runs"], state["runs"][1:]):
            if (previous["completed_at_utc"] is None
                    or timestamp(current["started_at_utc"]) < timestamp(previous["completed_at_utc"])):
                raise ValueError
        if state["runs"] and state["baseline_dates"] != state["runs"][0]["previous_data_dates"]:
            raise ValueError
        terminal = [run for run in state["runs"] if run["status"] == "COMPLETED" or run["status"].startswith("SKIPPED_")]
        if (any(run["completed_at_utc"] is None for run in terminal)
                or state["last_check_at_utc"] != (terminal[-1]["completed_at_utc"] if terminal else None)):
            raise ValueError
        pending_runs = [run["run_id"] for run in state["runs"]
                        if run["status"] in {"CHECKING", "DATA_READY", "FAILED", "PARTIAL_PUBLICATION"}]
        if pending_runs != ([state["pending_run_id"]] if state["pending_run_id"] is not None else []):
            raise ValueError
        if state["pending_run_id"] is not None:
            pending = next(run for run in state["runs"] if run["run_id"] == state["pending_run_id"])
            if (pending["status"] not in {"CHECKING", "DATA_READY", "FAILED", "PARTIAL_PUBLICATION"}
                    or pending["run_id"] != state["runs"][-1]["run_id"]):
                raise ValueError
        expected_successful = {family: {"run_id": run["run_id"], "data_dates": run["snapshot"]["data_dates"],
                                        "fingerprint": run["snapshot"]["fingerprint"]}
            for run in state["runs"] for family, model in run["models"].items() if model["status"] == "COMPLETED"}
        if state["successful"] != expected_successful:
            raise ValueError
        for family, success in state["successful"].items():
            if family not in MODEL_NAMES or set(success) != {"run_id", "data_dates", "fingerprint"}:
                raise ValueError
            _dates(success["data_dates"], symbols)
            _digest(success["fingerprint"])
            run = next(run for run in state["runs"] if run["run_id"] == success["run_id"])
            if (run["models"][family]["status"] != "COMPLETED" or success["data_dates"] != run["snapshot"]["data_dates"]
                    or success["fingerprint"] != run["snapshot"]["fingerprint"]):
                raise ValueError
        return state
    except Exception:
        raise RetrainingStateError("Journal violates its recovery contract; no automatic reset or repair.") from None


def write_state(path, state):
    try:
        save_raw(path, state, overwrite=True)
        sync_directory(path.parent)
    except Exception:
        raise RetrainingStateError("Journal publication/flush failed; inspect the last verified checkpoint before retrying.") from None


def sync_directory(path):
    """Flush publication and newly created directory entries on POSIX filesystems."""
    if os.name == "posix":
        for directory in (path, path.parent):
            descriptor = os.open(directory, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(descriptor)
            finally:
                os.close(descriptor)


def run_path(config, run_id, kind):
    validate_uuid(run_id)
    path = config.state_path.parent / kind / run_id
    if not path.resolve().is_relative_to(config.state_path.parent):
        raise RetrainingStateError("Recovery paths must remain inside the journal directory.")
    return path


@contextmanager
def retraining_lock(state_path):
    """The kernel releases ownership on process exit; the lock file is never deleted."""
    path = state_path.with_suffix(state_path.suffix + ".lock")
    try:
        if path.is_symlink() or not path.resolve().is_relative_to(state_path.parent):
            raise RetrainingStateError("Derived lock path cannot be a symlink or escape the journal directory.")
        path.parent.mkdir(parents=True, exist_ok=True)
        stream = path.open("a+b")
    except OSError:
        raise RetrainingStateError("Could not open retraining lock.") from None
    acquired = False
    try:
        if os.fstat(stream.fileno()).st_nlink != 1:
            raise RetrainingStateError("Hard-linked retraining lock is unsupported; use one canonical lock path.")
        if os.name == "nt":
            import msvcrt
            if path.stat().st_size == 0:
                stream.write(b"\0")
                stream.flush()
            stream.seek(0)
            try:
                msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            except OSError as error:
                if error.errno in (errno.EACCES, errno.EAGAIN, errno.EDEADLK):
                    raise RetrainingBusyError("ALREADY_RUNNING") from None
                raise
        else:
            import fcntl
            try:
                fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                raise RetrainingBusyError("ALREADY_RUNNING") from None
        acquired = True
        yield
    finally:
        if acquired:
            if os.name == "nt":
                stream.seek(0)
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(stream.fileno(), fcntl.LOCK_UN)
        stream.close()
