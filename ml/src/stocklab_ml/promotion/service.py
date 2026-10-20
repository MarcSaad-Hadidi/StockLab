"""Read-only status evaluation, followed by an independently explicit application."""

from dataclasses import asdict, replace
from datetime import UTC, datetime
from pathlib import Path

from stocklab_ml.artifacts import LoadedModel, LocalModelArtifact
from stocklab_ml.artifacts.validation import immutable_metadata, metadata_digest
from stocklab_ml.data.models import StorageError
from stocklab_ml.data.storage import cleaning_paths
from stocklab_ml.versioning import (
    ActiveVersionConflictError, ModelStatus, ModelVersionError, ModelVersionNotFoundError, ModelVersionRegistry,
)
from stocklab_ml.versioning.validation import canonical, decode_record, validate_timestamp
from stocklab_ml.versioning.contracts import _thaw
from .comparison import compare_evaluations
from .contracts import (
    ModelPromotionError, ModelPromotionPolicy, ModelPromotionRequest, PromotionApplication,
    PromotionConflictError, PromotionDecision, PromotionRecoveryError,
)
from .evaluation import BlockedEvidence, common_data, evaluate_loaded, load_for_role
from .policy import decide_promotion
from .storage import PromotionAuditStore
from .validation import digest, request_snapshot


def _context(request, registry, audit_store):
    if (not isinstance(request, ModelPromotionRequest) or not isinstance(registry, ModelVersionRegistry)
            or not isinstance(audit_store, PromotionAuditStore)):
        raise ModelPromotionError("An explicit request, existing registry, and audit store are required.")
    return request_snapshot(request)


def _protect(request, registry, store, *sources):
    paths = [registry.path, registry.path.with_suffix(registry.path.suffix + ".lock"),
             request.evaluation_dataset.output_path, request.evaluation_dataset.report_path]
    for source in sources:
        if isinstance(source, LoadedModel):
            source = source.artifact_reference
        if isinstance(source, LocalModelArtifact):
            paths.extend([source.artifact_path, source.manifest_path])
    destinations = [store.evaluation_path(request.evaluation_id), store.intent_path(request.evaluation_id),
                    store.application_path(request.evaluation_id)]
    try:
        for source in paths:
            if source is not None:
                cleaning_paths(Path(source), destinations[0], destinations[1])
                cleaning_paths(Path(source), destinations[0], destinations[2])
    except StorageError as error:
        raise ModelPromotionError("Audit outputs must not alias registry, dataset, lock, or artifact sources.") from error


def evaluate_model_promotion(request: ModelPromotionRequest, *, registry: ModelVersionRegistry,
                             audit_store: PromotionAuditStore, candidate_artifact,
                             active_artifact=None, trusted_source=False) -> PromotionDecision:
    """Persist exact historical evidence; never train or change Active/Archived."""
    snapshot = _context(request, registry, audit_store)
    _protect(request, registry, audit_store, active_artifact, candidate_artifact)
    active, candidate, evidence = None, None, {}
    try:
        active = registry.get_active_version(request.model_name)
        try:
            candidate = registry.get_version(request.model_name, request.candidate_version)
        except ModelVersionNotFoundError as error:
            raise BlockedEvidence("UNKNOWN_CANDIDATE") from error
        if candidate.status == ModelStatus.ACTIVE:
            raise BlockedEvidence("ALREADY_ACTIVE")
        if active is None and not request.initialize:
            raise BlockedEvidence("NO_ACTIVE")
        if (active.model_version if active else None) != request.expected_active_version:
            raise BlockedEvidence("REGISTRY_CHANGED")
        records = [candidate] + ([active] if active else [])
        if any(record.trained_at_utc > request.evaluation_timestamp_utc for record in records):
            raise BlockedEvidence("INVALID_HOLDOUT")
        loaded_candidate = load_for_role(candidate_artifact, candidate, registry, role="candidate",
            trusted_source=trusted_source, cache_dir=audit_store.root / "downloads")
        loaded_active = load_for_role(active_artifact, active, registry, role="active",
            trusted_source=trusted_source, cache_dir=audit_store.root / "downloads") if active else None
        _protect(request, registry, audit_store, loaded_active, loaded_candidate)
        inputs, labels, market, data = common_data(request, records)
        data["registry_path"] = str(registry.path)
        new = evaluate_loaded(loaded_candidate, inputs, labels, market, request.backtest_config)
        old = evaluate_loaded(loaded_active, inputs, labels, market, request.backtest_config) if loaded_active else None
        evidence = compare_evaluations(old, new, data)
        if request_snapshot(request) != snapshot:
            raise BlockedEvidence("DATASET_MISMATCH")
        if (any(registry.get_version(record.model_name, record.model_version) != record for record in records)
                or registry.get_active_version(request.model_name) != active):
            raise BlockedEvidence("REGISTRY_CHANGED")
        status, reasons = decide_promotion(evidence, request.promotion_policy)
    except BlockedEvidence as error:
        status, reasons = "BLOCKED", (error.code,)
    except ModelVersionError:
        status, reasons = "BLOCKED", ("INVALID_REGISTRY",)
    result = PromotionDecision(request.evaluation_id, request.model_name,
        active.model_version if active else None, request.candidate_version, status, reasons,
        asdict(request.promotion_policy), digest(snapshot), request.evaluation_timestamp_utc.isoformat(), evidence)
    audit_store.save_evaluation(result)
    return result


def _verify_models(decision, request, registry, store, candidate_artifact, active_artifact, trusted_source, *, applied):
    expected_records, loaded_models = [], []
    for role, source in (("candidate", candidate_artifact), ("active", active_artifact)):
        model = decision.evidence[role]
        if model is None:
            continue
        expected = decode_record(_thaw(model["record"]))
        current = registry.get_version(expected.model_name, expected.model_version)
        if canonical(immutable_metadata(current)) != canonical(immutable_metadata(expected)):
            raise PromotionConflictError("Evaluated immutable version metadata changed.")
        expected_status = (ModelStatus.ACTIVE if role == "candidate" else ModelStatus.ARCHIVED) if applied else expected.status
        if current.status != expected_status:
            raise PromotionConflictError("Evaluated model status changed.")
        loaded = load_for_role(source, current, registry, role=role, trusted_source=trusted_source,
                               cache_dir=store.root / "downloads")
        loaded_models.append(loaded)
        if loaded.artifact_sha256 != model["artifact_sha256"] or metadata_digest(current) != model["metadata_sha256"]:
            raise PromotionConflictError("Evaluated artifact bytes or immutable metadata changed.")
        expected_records.append(current)
    _protect(request, registry, store, *loaded_models)
    return tuple(expected_records)


def apply_model_promotion(decision: PromotionDecision, *, request: ModelPromotionRequest,
                          registry: ModelVersionRegistry, audit_store: PromotionAuditStore,
                          candidate_artifact, active_artifact=None, trusted_source=False,
                          applied_at_utc: datetime | None = None) -> PromotionApplication:
    """Only approved exact saved evidence may reach the registry's atomic CAS."""
    snapshot = _context(request, registry, audit_store)
    if not isinstance(decision, PromotionDecision):
        raise ModelPromotionError("An exact saved PromotionDecision is required.")
    saved = audit_store.load_evaluation(decision.evaluation_id)
    if (canonical(saved.to_dict()) != canonical(decision.to_dict())
            or digest(snapshot) != decision.request_sha256):
        raise PromotionConflictError("The supplied evaluation or request differs from its immutable audit.")
    if decision.decision != "APPROVED" or decide_promotion(decision.evidence, ModelPromotionPolicy(
            **_thaw(decision.policy_snapshot))) != ("APPROVED", ()):
        raise ModelPromotionError("Only a policy-approved evaluation can be explicitly applied.")
    if decision.evidence["dataset"]["registry_path"] != str(registry.path):
        raise PromotionConflictError("Evaluation belongs to another registry path.")
    _protect(request, registry, audit_store, active_artifact, candidate_artifact)
    intent = audit_store.load_intent(decision.evaluation_id)
    application = audit_store.load_application(decision.evaluation_id)
    current = registry.get_active_version(request.model_name)
    if intent is not None and (intent["evaluation_sha256"] != decision.evidence_sha256
                               or intent["registry_path"] != str(registry.path)):
        raise PromotionConflictError("Intent identifies different evidence or registry.")
    if application is not None:
        if (intent is None or application.evaluation_sha256 != decision.evidence_sha256
                or application.model_name != request.model_name
                or application.candidate_version != request.candidate_version
                or application.expected_active_version != request.expected_active_version
                or current is None or current.model_version != request.candidate_version):
            raise PromotionConflictError("Final application audit does not match the current resulting state.")
        _verify_models(decision, request, registry, audit_store, candidate_artifact, active_artifact,
                       trusted_source, applied=True)
        return replace(application, status="ALREADY_APPLIED")
    if intent is not None:
        # Registry schema v1 has no operation ID: intent + Active cannot prove this operation won.
        raise PromotionRecoveryError("Pending intent has no final proof; inspect audit and registry explicitly. Never replay CAS.")
    if (current.model_version if current else None) != request.expected_active_version:
        raise PromotionConflictError("Active version changed since evaluation.")
    try:
        records = _verify_models(decision, request, registry, audit_store, candidate_artifact, active_artifact,
                                  trusted_source, applied=False)
        applied_at_utc = datetime.now(UTC) if applied_at_utc is None else applied_at_utc
        validate_timestamp(applied_at_utc)
        if applied_at_utc < request.evaluation_timestamp_utc:
            raise ModelPromotionError("Application timestamp must not precede its evaluation.")
        intent = {"evaluation_id": decision.evaluation_id, "evaluation_sha256": decision.evidence_sha256,
            "registry_path": str(registry.path), "model_name": request.model_name,
            "candidate_version": request.candidate_version, "expected_active_version": request.expected_active_version,
            "prepared_at_utc": applied_at_utc.isoformat()}
        audit_store.save_intent(intent)
        registry.set_active_version_if_current(request.model_name, request.candidate_version,
                                               request.expected_active_version, expected_records=records)
    except ActiveVersionConflictError as error:
        raise PromotionConflictError("Active/metadata changed during application; CAS refused the stale evaluation.") from error
    except (ModelVersionError, OSError) as error:
        raise PromotionRecoveryError("Application interrupted; inspect durable intent and current registry before recovery.") from error
    result = PromotionApplication(decision.evaluation_id, decision.evidence_sha256, request.model_name,
        request.candidate_version, request.expected_active_version, "APPLIED", applied_at_utc.isoformat())
    try:
        audit_store.save_application(result)
    except (ModelPromotionError, OSError) as error:
        raise PromotionRecoveryError("CAS completed but final audit failed; preserve intent and inspect registry. Never replay.") from error
    return result
