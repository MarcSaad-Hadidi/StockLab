"""Bounded, hashed, immutable local audit files; no cross-file transaction claim."""

from dataclasses import asdict
from datetime import datetime
import json
import os
from pathlib import Path
import re

from stocklab_ml.artifacts.local_storage import read_bounded
from stocklab_ml.data.models import StorageError
from stocklab_ml.data.storage import save_raw
from stocklab_ml.versioning.registry import _unique_keys
from stocklab_ml.versioning.validation import canonical, validate_name, validate_timestamp, validate_uuid, validate_version
from .contracts import PromotionApplication, PromotionAuditError, PromotionConflictError, PromotionDecision
from .validation import digest

DEFAULT_PROMOTION_DIR = Path(__file__).resolve().parents[3] / "models" / "promotion"
MAX_AUDIT_BYTES = 8 * 1024 * 1024


class PromotionAuditStore:
    def __init__(self, root=None):
        self.root = Path(root if root is not None else DEFAULT_PROMOTION_DIR).resolve()

    def _path(self, evaluation_id, kind):
        validate_uuid(evaluation_id)
        path = self.root / kind / (evaluation_id + ".json")
        if not path.resolve().is_relative_to(self.root):
            raise PromotionAuditError("Audit path escapes its explicitly selected directory.")
        return path

    def evaluation_path(self, evaluation_id):
        return self._path(evaluation_id, "evaluations")

    def application_path(self, evaluation_id):
        return self._path(evaluation_id, "applications")

    def intent_path(self, evaluation_id):
        return self._path(evaluation_id, "intents")

    def _read(self, path):
        try:
            if not os.path.lexists(path):
                return None
            if path.is_symlink() or path.stat().st_nlink != 1:
                raise ValueError
            # shortcut: summaries cap each audit file at 8 MiB; migrate preserved evidence before increasing this limit.
            envelope = json.loads(read_bounded(path, MAX_AUDIT_BYTES), object_pairs_hook=_unique_keys)
            if (set(envelope) != {"schema_version", "sha256", "payload"}
                    or type(envelope["schema_version"]) is not int or envelope["schema_version"] != 1
                    or envelope["sha256"] != digest(envelope["payload"])):
                raise ValueError
            return envelope["payload"]
        except Exception as error:
            raise PromotionAuditError("Unreadable or corrupted audit; restore verified evidence explicitly.") from error

    def _save(self, path, payload, *, reuse=False):
        envelope = {"schema_version": 1, "sha256": digest(payload), "payload": payload}
        if len((json.dumps(envelope, indent=2, ensure_ascii=False, allow_nan=False) + "\n").encode("utf-8")) > MAX_AUDIT_BYTES:
            raise PromotionAuditError("Audit exceeds the bounded file size.")
        try:
            save_raw(path, envelope)
        except (OSError, StorageError) as error:
            existing = self._read(path)
            if existing is not None:
                if reuse and canonical(existing) == canonical(payload):
                    return
                raise PromotionConflictError("EvaluationId already has different evidence or an application owner.") from error
            raise PromotionAuditError("Atomic audit publication failed; no successful application is claimed.") from error
        if os.name != "nt":
            try:
                descriptor = os.open(path.parent, os.O_RDONLY)
                try:
                    os.fsync(descriptor)
                finally:
                    os.close(descriptor)
            except OSError as error:
                raise PromotionAuditError("Audit was published but directory durability is unconfirmed.") from error

    def save_evaluation(self, decision):
        self._save(self.evaluation_path(decision.evaluation_id), decision.to_dict(), reuse=True)

    def load_evaluation(self, evaluation_id):
        payload = self._read(self.evaluation_path(evaluation_id))
        if payload is None:
            raise PromotionAuditError("The exact saved evaluation is required before application.")
        try:
            result = PromotionDecision(**payload)
            if result.evaluation_id != evaluation_id:
                raise ValueError
            return result
        except Exception as error:
            raise PromotionAuditError("Invalid saved promotion evaluation.") from error

    def save_intent(self, intent):
        self._save(self.intent_path(intent["evaluation_id"]), intent)

    def load_intent(self, evaluation_id):
        payload = self._read(self.intent_path(evaluation_id))
        if payload is not None:
            try:
                if (set(payload) != {"evaluation_id", "evaluation_sha256", "registry_path", "model_name",
                        "candidate_version", "expected_active_version", "prepared_at_utc"}
                        or payload["evaluation_id"] != evaluation_id):
                    raise ValueError
                validate_name(payload["model_name"])
                if (not isinstance(payload["evaluation_sha256"], str)
                        or re.fullmatch(r"[a-f0-9]{64}", payload["evaluation_sha256"]) is None
                        or not isinstance(payload["registry_path"], str) or not payload["registry_path"]):
                    raise ValueError
                validate_version(payload["candidate_version"])
                if payload["expected_active_version"] is not None:
                    validate_version(payload["expected_active_version"])
                validate_timestamp(datetime.fromisoformat(payload["prepared_at_utc"]))
            except Exception as error:
                raise PromotionAuditError("Invalid saved promotion intent.") from error
        return payload

    def save_application(self, application):
        self._save(self.application_path(application.evaluation_id), asdict(application))

    def load_application(self, evaluation_id):
        payload = self._read(self.application_path(evaluation_id))
        if payload is None:
            return None
        try:
            result = PromotionApplication(**payload)
            if result.evaluation_id != evaluation_id or result.status != "APPLIED":
                raise ValueError
            validate_name(result.model_name)
            if not isinstance(result.evaluation_sha256, str) or re.fullmatch(r"[a-f0-9]{64}", result.evaluation_sha256) is None:
                raise ValueError
            validate_version(result.candidate_version)
            if result.expected_active_version is not None:
                validate_version(result.expected_active_version)
            validate_timestamp(datetime.fromisoformat(result.applied_at_utc))
            return result
        except Exception as error:
            raise PromotionAuditError("Invalid finalized application audit.") from error
