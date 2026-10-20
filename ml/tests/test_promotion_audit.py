"""A verified evaluation never licenses replay of an ambiguous registry write."""

from dataclasses import replace
import json
from uuid import uuid4

import pytest

from test_model_promotion import api, setup, trained


def approved(setup):
    request, kwargs, records = setup(active=False)
    request = replace(request, initialize=True,
        promotion_policy=api().ModelPromotionPolicy(minimum_accuracy=0, minimum_f1=0))
    decision = api().evaluate_model_promotion(request, **kwargs)
    assert decision.decision == "APPROVED"
    return request, kwargs, decision


@pytest.mark.parametrize("change", ["decision", "dataset", "artifact", "active", "audit"])
def test_apply_revalidates_exact_evidence_before_mutating(setup, change):
    request, kwargs, decision = approved(setup)
    if change == "decision":
        decision = replace(decision, request_sha256="0" * 64)
    elif change == "dataset":
        request.evaluation_dataset.dataframe.loc[0, "volume"] += 1
    elif change == "artifact":
        kwargs["candidate_artifact"].artifact_path.write_bytes(b"corrupt")
    elif change == "active":
        kwargs["registry"].set_active_version(request.model_name, "v000001")
    else:
        path = kwargs["audit_store"].evaluation_path(request.evaluation_id)
        path.write_text('{"schema_version":1,"schema_version":1}', encoding="utf-8")
    before = kwargs["registry"].path.read_bytes()
    with pytest.raises(api().ModelPromotionError):
        api().apply_model_promotion(decision, request=request, **kwargs)
    assert kwargs["registry"].path.read_bytes() == before


@pytest.mark.parametrize("phase", ["intent", "before_cas", "after_cas"])
def test_crash_preserves_valid_registry_and_never_replays_pending_intent(setup, monkeypatch, phase):
    request, kwargs, decision = approved(setup)
    store, registry = kwargs["audit_store"], kwargs["registry"]
    def crash(*args, **kwargs):
        raise OSError("simulated interruption")
    if phase == "intent":
        monkeypatch.setattr(store, "save_intent", crash)
    elif phase == "before_cas":
        monkeypatch.setattr(registry, "set_active_version_if_current", crash)
    else:
        monkeypatch.setattr(store, "save_application", crash)
    with pytest.raises(api().ModelPromotionError):
        api().apply_model_promotion(decision, request=request, **kwargs)
    active = registry.get_active_version(request.model_name)
    assert (active.model_version if active else None) == (request.candidate_version if phase == "after_cas" else None)
    monkeypatch.undo()
    if phase != "intent":
        before = registry.path.read_bytes()
        with pytest.raises(api().PromotionRecoveryError):
            api().apply_model_promotion(decision, request=request, **kwargs)
        assert registry.path.read_bytes() == before
        assert store.load_intent(request.evaluation_id)["evaluation_sha256"] == decision.evidence_sha256


def test_finalized_application_retry_requires_matching_resulting_state(setup):
    request, kwargs, decision = approved(setup)
    first = api().apply_model_promotion(decision, request=request, **kwargs)
    before = kwargs["registry"].path.read_bytes()
    assert kwargs["audit_store"].load_application(request.evaluation_id).status == "APPLIED"
    assert api().apply_model_promotion(decision, request=request, **kwargs).status == "ALREADY_APPLIED"
    assert kwargs["registry"].path.read_bytes() == before
    kwargs["registry"].archive_version(request.model_name, request.candidate_version)
    with pytest.raises(api().PromotionConflictError):
        api().apply_model_promotion(decision, request=request, **kwargs)


def test_corrupt_final_audit_does_not_report_idempotent_success(setup):
    request, kwargs, decision = approved(setup)
    api().apply_model_promotion(decision, request=request, **kwargs)
    path = kwargs["audit_store"].application_path(request.evaluation_id)
    content = json.loads(path.read_text())
    content["payload"]["candidate_version"] = "v000099"
    path.write_text(json.dumps(content), encoding="utf-8")
    with pytest.raises(api().PromotionAuditError):
        api().apply_model_promotion(decision, request=request, **kwargs)


@pytest.mark.parametrize("change", ["missing_dataset_field", "extra_model_field", "wrong_row_count", "wrong_delta"])
def test_audit_schema_rejects_inconsistent_summaries_even_with_recomputed_envelope_hash(setup, change):
    from stocklab_ml.promotion.validation import digest
    request, kwargs, _ = setup()
    result = api().evaluate_model_promotion(request, **kwargs)
    path = kwargs["audit_store"].evaluation_path(request.evaluation_id)
    envelope = json.loads(path.read_text())
    evidence = envelope["payload"]["evidence"]
    if change == "missing_dataset_field":
        del evidence["dataset"]["registry_path"]
    elif change == "extra_model_field":
        evidence["candidate"]["unsafe_source"] = "arbitrary"
    elif change == "wrong_row_count":
        evidence["dataset"]["evaluation_rows"] += 1
    else:
        evidence["metric_deltas"]["f1_delta"] = 1
    envelope["sha256"] = digest(envelope["payload"])
    path.write_text(json.dumps(envelope), encoding="utf-8")
    with pytest.raises(api().PromotionAuditError):
        kwargs["audit_store"].load_evaluation(request.evaluation_id)


def test_durable_intent_fsync_failure_prevents_activation(setup, monkeypatch):
    import stocklab_ml.data.storage as storage
    request, kwargs, decision = approved(setup)
    before = kwargs["registry"].path.read_bytes()
    def crash(*args, **kwargs):
        raise OSError("fsync failed")
    monkeypatch.setattr(storage.os, "fsync", crash)
    with pytest.raises(api().PromotionAuditError):
        api().apply_model_promotion(decision, request=request, **kwargs)
    assert kwargs["registry"].path.read_bytes() == before
    assert not kwargs["audit_store"].intent_path(request.evaluation_id).exists()


def test_validly_rehashed_replaced_model_bytes_still_conflict_with_evaluation(setup):
    from test_s3_model_artifacts import variant
    request, kwargs, decision = approved(setup)
    variant(kwargs["candidate_artifact"])
    before = kwargs["registry"].path.read_bytes()
    with pytest.raises(api().PromotionConflictError):
        api().apply_model_promotion(decision, request=request, **kwargs)
    assert kwargs["registry"].path.read_bytes() == before


@pytest.mark.parametrize("phase", ["write", "lock_cleanup"])
def test_wrapped_registry_failure_requires_recovery_without_replaying_cas(setup, monkeypatch, phase):
    from pathlib import Path
    import stocklab_ml.versioning.registry as registry_module
    from stocklab_ml.data.models import StorageError
    from stocklab_ml.versioning import ModelVersionError

    request, kwargs, decision = approved(setup)
    registry, store = kwargs["registry"], kwargs["audit_store"]
    if phase == "write":
        def fail_write(*args, **kwargs):
            raise StorageError("simulated registry publication failure")
        monkeypatch.setattr(registry_module, "save_raw", fail_write)
    else:
        unlink = Path.unlink
        lock = registry.path.with_suffix(registry.path.suffix + ".lock")
        def fail_cleanup(path, *args, **kwargs):
            if path == lock:
                raise OSError("simulated registry lock cleanup failure")
            return unlink(path, *args, **kwargs)
        monkeypatch.setattr(Path, "unlink", fail_cleanup)
    with pytest.raises(api().PromotionRecoveryError) as caught:
        api().apply_model_promotion(decision, request=request, **kwargs)
    assert isinstance(caught.value.__cause__, ModelVersionError)
    active = registry.get_active_version(request.model_name)
    assert (active.model_version if active else None) == (request.candidate_version if phase == "lock_cleanup" else None)
    assert store.load_application(request.evaluation_id) is None
    assert store.load_intent(request.evaluation_id)["evaluation_sha256"] == decision.evidence_sha256
    monkeypatch.undo()
    before = registry.path.read_bytes()
    with pytest.raises(api().PromotionRecoveryError):
        api().apply_model_promotion(decision, request=request, **kwargs)
    assert registry.path.read_bytes() == before
