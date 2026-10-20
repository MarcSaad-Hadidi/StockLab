"""Schema-v1 local JSON registry with locked, atomic read-modify-write operations."""

from contextlib import contextmanager
from collections.abc import Callable
from dataclasses import replace
from datetime import UTC, datetime
import json
import os
from pathlib import Path
import time
from typing import TYPE_CHECKING

from stocklab_ml.data.models import StorageError
from stocklab_ml.data.storage import save_raw
from stocklab_ml.modeling.contracts import LogisticRegressionResult, RandomForestResult
from .contracts import (
    ActiveVersionConflictError, ModelStatus, ModelVersionError, ModelVersionNotFoundError, ModelVersionRecord,
    RegistryBusyError, TrainingRunConflictError,
)
from .validation import (
    canonical, decode_record, training_payload, validate_name, validate_timestamp,
    validate_uuid, validate_version, version_string,
)

if TYPE_CHECKING:
    from .association import RegisteredModel

DEFAULT_REGISTRY_PATH = Path(__file__).resolve().parents[3] / "models" / "registry.json"
LOCK_TIMEOUT_SECONDS = 5.0
_UNCONDITIONAL = object()


def _unique_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ModelVersionError("Duplicate JSON key in registry; no automatic repair.")
        result[key] = value
    return result


class ModelVersionRegistry:
    """Metadata only. Reads use exact identities; list_versions sorts by number ASC."""

    def __init__(self, registry_path: str | Path | None = None, *, clock: Callable[[], datetime] | None = None):
        self.path = Path(registry_path if registry_path is not None else DEFAULT_REGISTRY_PATH).resolve()
        self._clock = clock if clock is not None else lambda: datetime.now(UTC)

    def _read(self) -> list[ModelVersionRecord]:
        try:
            if self.path.exists() and self.path.stat().st_nlink != 1:
                raise ModelVersionError("Hard-linked registry files are unsupported; use one canonical path.")
            content = self.path.read_text(encoding="utf-8")
        except FileNotFoundError:
            if os.path.lexists(self.path):
                raise ModelVersionError("Registry exists but cannot be read safely.") from None
            return []
        except (OSError, UnicodeError) as error:
            raise ModelVersionError("Could not read local model registry.") from error
        try:
            payload = json.loads(content, object_pairs_hook=_unique_keys)
            if (not isinstance(payload, dict) or set(payload) != {"schema_version", "models"}
                    or type(payload["schema_version"]) is not int or payload["schema_version"] != 1
                    or not isinstance(payload["models"], list)):
                raise ModelVersionError("Invalid or unsupported registry schema; no automatic reset.")
            records = [decode_record(item) for item in payload["models"]]
            keys, runs, types, active, versions = set(), set(), {}, set(), {}
            for record in records:
                key, name = (record.model_name, record.model_version), record.model_name
                if key in keys or record.training_run_id in runs:
                    raise ModelVersionError("Duplicate model/version or TrainingRunId in registry.")
                keys.add(key)
                runs.add(record.training_run_id)
                if name in types and types[name] != record.model_type:
                    raise ModelVersionError("One ModelName cannot refer to different model types.")
                types[name] = record.model_type
                versions.setdefault(name, []).append(record.version_number)
                if record.status == ModelStatus.ACTIVE:
                    if name in active:
                        raise ModelVersionError("More than one Active version in a model family.")
                    active.add(name)
            for numbers in versions.values():
                if sorted(numbers) != list(range(1, len(numbers) + 1)):
                    raise ModelVersionError("Registry version history has a gap; no silent counter reset.")
            return records
        except ModelVersionError:
            raise
        except (ValueError, TypeError, UnicodeError) as error:
            raise ModelVersionError("Corrupted registry JSON; no automatic reset or repair.") from error

    @contextmanager
    def _locked(self):
        lock = self.path.with_suffix(self.path.suffix + ".lock")
        try:
            lock.parent.mkdir(parents=True, exist_ok=True)
            deadline = time.monotonic() + LOCK_TIMEOUT_SECONDS
            while True:
                try:
                    descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
                    break
                except (FileExistsError, PermissionError):
                    # Windows can report a just-deleted lock as access denied.
                    if time.monotonic() >= deadline:
                        raise RegistryBusyError("Registry lock unavailable; retry later. Never remove a live lock.") from None
                    time.sleep(.01)
            os.close(descriptor)
        except OSError as error:
            raise ModelVersionError("Could not acquire local registry lock.") from error
        try:
            yield
        finally:
            # shortcut: a killed writer leaves this lock, recover manually only after verifying no writer is alive.
            try:
                lock.unlink()
            except OSError as error:
                raise ModelVersionError("Registry lock cleanup failed; inspect the registry before retrying.") from error

    def _write(self, records: list[ModelVersionRecord]) -> None:
        try:
            save_raw(self.path, {"schema_version": 1, "models": [r.to_dict() for r in records]}, overwrite=True)
        except (OSError, StorageError) as error:
            raise ModelVersionError("Registry atomic publication failed; previous registry remains intact.") from error

    def register_version(
        self, training_result: LogisticRegressionResult | RandomForestResult, *,
        training_run_id: str, trained_at_utc: datetime,
    ) -> ModelVersionRecord:
        """Register an existing run once; retries return its record or raise a typed conflict."""
        validate_uuid(training_run_id)
        validate_timestamp(trained_at_utc)
        payload = training_payload(training_result) | {
            "training_run_id": training_run_id, "trained_at_utc": trained_at_utc.isoformat(),
        }
        with self._locked():
            records = self._read()
            for record in records:
                if record.training_run_id == training_run_id:
                    existing = record.to_dict()
                    if canonical({key: existing[key] for key in payload}) != canonical(payload):
                        raise TrainingRunConflictError("TrainingRunId already identifies different immutable metadata.")
                    return record
            family = [record for record in records if record.model_name == payload["model_name"]]
            if any(record.model_type != payload["model_type"] for record in family):
                raise ModelVersionError("ModelName already belongs to a different model type.")
            number = max((record.version_number for record in family), default=0) + 1
            registered = self._clock()
            validate_timestamp(registered)
            record = decode_record(payload | {"version_number": number, "model_version": version_string(number),
                "registered_at_utc": registered.isoformat(), "status": ModelStatus.ARCHIVED.value})
            self._write(records + [record])
            return record

    def list_versions(self, model_name: str) -> tuple[ModelVersionRecord, ...]:
        validate_name(model_name)
        return tuple(sorted((r for r in self._read() if r.model_name == model_name), key=lambda r: r.version_number))

    def get_version(self, model_name: str, model_version: str) -> ModelVersionRecord:
        validate_name(model_name)
        validate_version(model_version)
        for record in self.list_versions(model_name):
            if record.model_version == model_version:
                return record
        raise ModelVersionNotFoundError(f"Model version not registered: {model_name}/{model_version}.")

    def get_active_version(self, model_name: str) -> ModelVersionRecord | None:
        return next((r for r in self.list_versions(model_name) if r.status == ModelStatus.ACTIVE), None)

    def _set_status(self, model_name: str, model_version: str, status: ModelStatus, *,
                    expected_active_version=_UNCONDITIONAL, expected_records=()) -> ModelVersionRecord:
        validate_name(model_name)
        validate_version(model_version)
        with self._locked():
            records = self._read()
            if expected_active_version is not _UNCONDITIONAL:
                active = next((r.model_version for r in records
                               if r.model_name == model_name and r.status == ModelStatus.ACTIVE), None)
                if active != expected_active_version:
                    raise ActiveVersionConflictError("Active version changed; evaluate against the current Active model.")
                for expected in expected_records:
                    if expected not in records:
                        raise ActiveVersionConflictError("Evaluated model metadata/status changed before activation.")
            if not any(r.model_name == model_name and r.model_version == model_version for r in records):
                raise ModelVersionNotFoundError(f"Model version not registered: {model_name}/{model_version}.")
            updated = []
            for record in records:
                if record.model_name == model_name:
                    if record.model_version == model_version:
                        record = replace(record, status=status)
                    elif status == ModelStatus.ACTIVE and record.status == ModelStatus.ACTIVE:
                        record = replace(record, status=ModelStatus.ARCHIVED)
                updated.append(record)
            if updated != records:
                self._write(updated)
            return next(r for r in updated if r.model_name == model_name and r.model_version == model_version)

    def set_active_version(self, model_name: str, model_version: str) -> ModelVersionRecord:
        return self._set_status(model_name, model_version, ModelStatus.ACTIVE)

    def set_active_version_if_current(self, model_name: str, candidate_version: str,
                                      expected_active_version: str | None, *,
                                      expected_records: tuple[ModelVersionRecord, ...] = ()) -> ModelVersionRecord:
        """Compare and publish under the existing lock; None explicitly expects no Active."""
        if expected_active_version is not None:
            validate_version(expected_active_version)
        if (not isinstance(expected_records, tuple)
                or any(not isinstance(record, ModelVersionRecord) or record.model_name != model_name
                       for record in expected_records)):
            raise ModelVersionError("Expected records must be exact snapshots of the selected model family.")
        return self._set_status(model_name, candidate_version, ModelStatus.ACTIVE,
                                expected_active_version=expected_active_version, expected_records=expected_records)

    def archive_version(self, model_name: str, model_version: str) -> ModelVersionRecord:
        return self._set_status(model_name, model_version, ModelStatus.ARCHIVED)

    def bind_version(
        self, training_result: LogisticRegressionResult | RandomForestResult, *, model_name: str, model_version: str,
    ) -> "RegisteredModel":
        """Associate this existing estimator/result with an explicitly registered run."""
        from .association import RegisteredModel
        record = self.get_version(model_name, model_version)
        payload = training_payload(training_result)
        stored = record.to_dict()
        if canonical(payload) != canonical({key: stored[key] for key in payload}):
            raise ModelVersionError("Supplied training result does not match the registered model/version.")
        return RegisteredModel(training_result, record, self.path)


def register_model_version(
    training_result: LogisticRegressionResult | RandomForestResult, *, training_run_id: str,
    trained_at_utc: datetime, registry_path: str | Path | None = None,
) -> ModelVersionRecord:
    return ModelVersionRegistry(registry_path).register_version(
        training_result, training_run_id=training_run_id, trained_at_utc=trained_at_utc)
