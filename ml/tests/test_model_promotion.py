"""Real artifact/common-data integration and fail-closed application evidence."""

from dataclasses import replace
from datetime import UTC, datetime
from importlib import import_module
from uuid import uuid4

import pandas as pd
import pytest
from sklearn.ensemble import RandomForestClassifier
from sklearn.linear_model import LogisticRegression
from sklearn.preprocessing import StandardScaler

from stocklab_ml.artifacts import load_model_artifact, save_model_artifact
from stocklab_ml.data.cleaning import clean_historical_dataset
from stocklab_ml.features import engineer_features
from stocklab_ml.modeling import train_random_forest
from stocklab_ml.versioning import ModelStatus, ModelVersionRegistry
from test_retraining import history

NOW = datetime(2026, 10, 20, 12, tzinfo=UTC)


def api():
    return import_module("stocklab_ml.promotion")


@pytest.fixture(scope="module")
def trained():
    return tuple(train_random_forest(engineer_features(history(count)).dataframe) for count in (100, 140))


@pytest.fixture
def setup(tmp_path, trained):
    def build(forest=False, active=True, trained_at=NOW):
        registry = ModelVersionRegistry(tmp_path / "registry.json", clock=lambda: NOW)
        records, artifacts = [], []
        for run in trained:
            result = run if forest else run.baseline_result
            record = registry.register_version(result, training_run_id=str(uuid4()), trained_at_utc=trained_at)
            records.append(record)
            artifacts.append(save_model_artifact(registry.bind_version(result,
                model_name=record.model_name, model_version=record.model_version), output_dir=tmp_path / "artifacts"))
        if active:
            registry.set_active_version(records[0].model_name, records[0].model_version)
        request = api().ModelPromotionRequest(model_name=records[0].model_name,
            candidate_version=records[1].model_version,
            expected_active_version=records[0].model_version if active else None,
            evaluation_dataset=clean_historical_dataset(history(220), as_of_date="2024-11-01"),
            promotion_policy=api().ModelPromotionPolicy(), evaluation_timestamp_utc=NOW,
            evaluation_id=str(uuid4()), evaluation_start="2024-07-01", evaluation_end="2024-10-31")
        store = api().PromotionAuditStore(tmp_path / "audit")
        kwargs = dict(registry=registry, audit_store=store, active_artifact=artifacts[0] if active else None,
                      candidate_artifact=artifacts[1], trusted_source=True)
        return request, kwargs, records
    return build


def forbidden(*args, **kwargs):
    raise AssertionError("Training/network is forbidden during promotion")


@pytest.mark.parametrize("forest", [False, True])
def test_common_oos_evaluation_uses_existing_artifacts_without_training_or_status_writes(setup, monkeypatch, forest):
    request, kwargs, records = setup(forest)
    registry = kwargs["registry"]
    before = registry.path.read_bytes()
    for cls in (LogisticRegression, RandomForestClassifier, StandardScaler):
        monkeypatch.setattr(cls, "fit", forbidden)
    result = api().evaluate_model_promotion(request, **kwargs)
    assert result.decision in {"APPROVED", "REJECTED"}
    assert registry.path.read_bytes() == before
    evidence = result.evidence
    a, c = evidence["active"], evidence["candidate"]
    assert a["record"]["model_version"] == "v000001" and c["record"]["model_version"] == "v000002"
    assert a["classification"]["total_rows"] == c["classification"]["total_rows"] == evidence["dataset"]["evaluation_rows"]
    assert evidence["dataset"]["feature_start"] >= records[1].split_date
    assert evidence["dataset"]["forward_rows"] == 0 and evidence["dataset"]["evidence_kind"] == "historical"
    for field in ("start_date", "end_date", "symbols", "config", "initial_capital"):
        assert a["backtest"][field] == c["backtest"][field]
    assert a["benchmark"] == c["benchmark"] == evidence["benchmark"]
    assert a["equity_dates"] == c["equity_dates"]
    assert a["evaluation_keys_sha256"] == c["evaluation_keys_sha256"] == evidence["dataset"]["labels_sha256"]
    assert kwargs["audit_store"].load_evaluation(request.evaluation_id) == result
    assert api().evaluate_model_promotion(request, **kwargs) == result


@pytest.mark.parametrize("change,reason", [("unknown", "UNKNOWN_CANDIDATE"), ("no_active", "NO_ACTIVE"),
    ("missing_candidate", "MISSING_CANDIDATE_ARTIFACT"), ("missing_active", "MISSING_ACTIVE_ARTIFACT"),
    ("corrupt", "CORRUPTED_ARTIFACT"), ("stale", "REGISTRY_CHANGED"),
    ("short", "INSUFFICIENT_COMMON_DATA"), ("already_active", "ALREADY_ACTIVE")])
def test_blocked_evidence_never_changes_registry(setup, change, reason):
    request, kwargs, records = setup(active=change != "no_active")
    if change == "unknown":
        request = replace(request, candidate_version="v999999")
    elif change == "missing_candidate":
        kwargs["candidate_artifact"].artifact_path.unlink()
    elif change == "missing_active":
        kwargs["active_artifact"].manifest_path.unlink()
    elif change == "corrupt":
        kwargs["candidate_artifact"].artifact_path.write_bytes(b"corrupted")
    elif change == "stale":
        request = replace(request, expected_active_version="v000099")
    elif change == "short":
        request = replace(request, evaluation_end="2024-07-03")
    elif change == "already_active":
        kwargs["registry"].set_active_version(request.model_name, request.candidate_version)
    before = kwargs["registry"].path.read_bytes()
    result = api().evaluate_model_promotion(request, **kwargs)
    assert result.decision == "BLOCKED" and reason in result.reason_codes
    assert kwargs["registry"].path.read_bytes() == before


def test_evaluation_id_conflict_and_frozen_policy_evidence(setup):
    request, kwargs, _ = setup()
    result = api().evaluate_model_promotion(request, **kwargs)
    with pytest.raises(TypeError):
        result.evidence["candidate"]["classification"]["accuracy"] = 1
    with pytest.raises(api().PromotionConflictError):
        api().evaluate_model_promotion(replace(request, evaluation_end="2024-10-30"), **kwargs)


def test_untrusted_or_wrong_exact_source_is_blocked_and_loaded_model_is_reverified(setup):
    request, kwargs, _ = setup()
    result = api().evaluate_model_promotion(request, **(kwargs | {"trusted_source": False}))
    assert result.decision == "BLOCKED" and "UNTRUSTED_SOURCE" in result.reason_codes
    request = replace(request, evaluation_id=str(uuid4()))
    result = api().evaluate_model_promotion(request, **(kwargs | {"candidate_artifact": kwargs["active_artifact"]}))
    assert result.decision == "BLOCKED" and "MODEL_VERSION_MISMATCH" in result.reason_codes
    loaded = load_model_artifact(kwargs["candidate_artifact"], registry=kwargs["registry"], trusted_source=True)
    loaded.artifact_reference.artifact_path.write_bytes(b"corrupted after load")
    result = api().evaluate_model_promotion(replace(request, evaluation_id=str(uuid4())),
                                          **(kwargs | {"candidate_artifact": loaded}))
    assert result.decision == "BLOCKED" and "CORRUPTED_ARTIFACT" in result.reason_codes


def test_explicit_initial_activation_requires_valid_approved_evidence(setup):
    request, kwargs, _ = setup(active=False)
    request = replace(request, initialize=True,
                      promotion_policy=api().ModelPromotionPolicy(minimum_accuracy=0, minimum_f1=0))
    result = api().evaluate_model_promotion(request, **kwargs)
    assert result.decision == "APPROVED"
    assert kwargs["registry"].get_active_version(request.model_name) is None
    applied = api().apply_model_promotion(result, request=request, **kwargs)
    assert applied.status == "APPLIED"
    assert kwargs["registry"].get_active_version(request.model_name).model_version == request.candidate_version
    assert api().apply_model_promotion(result, request=request, **kwargs).status == "ALREADY_APPLIED"


@pytest.mark.parametrize("field,value", [("candidate_version", "latest"), ("model_name", "../unsafe"),
    ("initialize", 1), ("evaluation_id", "../../x"), ("evaluation_start", "2024-10-32"),
    ("evaluation_end", "2027-01-01"), ("evaluation_timestamp_utc", datetime(2026, 10, 20))])
def test_invalid_request_rejected_at_boundary(setup, field, value):
    request, _, _ = setup()
    with pytest.raises(api().ModelPromotionError):
        replace(request, **{field: value})


def test_predictions_metrics_and_backtests_share_exact_rows_without_labels_as_features(setup, monkeypatch):
    from stocklab_ml.artifacts import LoadedModel
    from stocklab_ml.features import FEATURE_COLUMNS
    import stocklab_ml.promotion.evaluation as module
    request, kwargs, _ = setup()
    prediction_inputs, metric_inputs, markets = [], [], []
    predict, evaluate, backtest = LoadedModel.predict, module.evaluate_predictions, module.run_backtest
    def prediction_spy(model, frame):
        assert list(frame.columns) == ["date", "symbol"] + FEATURE_COLUMNS
        prediction_inputs.append(frame.copy(deep=True))
        result = predict(model, frame)
        assert result.to_dataframe().model_version.eq(model.record.model_version).all()
        return result
    def metrics_spy(**arguments):
        metric_inputs.append(arguments["predictions"][["date", "target_date", "symbol", "actual_class"]].copy())
        return evaluate(**arguments)
    def backtest_spy(decisions, market, *, config):
        markets.append((market.dataframe.copy(), config))
        result = backtest(decisions, market, config=config)
        assert result.trades.execution_date.gt(result.trades.decision_date).all()
        return result
    monkeypatch.setattr(LoadedModel, "predict", prediction_spy)
    monkeypatch.setattr(module, "evaluate_predictions", metrics_spy)
    monkeypatch.setattr(module, "run_backtest", backtest_spy)
    result = api().evaluate_model_promotion(request, **kwargs)
    assert result.decision != "BLOCKED" and len(prediction_inputs) == len(metric_inputs) == len(markets) == 2
    pd.testing.assert_frame_equal(prediction_inputs[0], prediction_inputs[1])
    pd.testing.assert_frame_equal(metric_inputs[0], metric_inputs[1])
    pd.testing.assert_frame_equal(markets[0][0], markets[1][0])
    assert markets[0][1] == markets[1][1] == request.backtest_config


def test_future_observations_cannot_change_earlier_predictions_or_execution_window(setup):
    request, kwargs, _ = setup()
    first = api().evaluate_model_promotion(request, **kwargs)
    changed = request.evaluation_dataset.dataframe.copy(deep=True)
    changed.loc[changed.date > request.evaluation_end, ["open", "high", "low", "close"]] *= 10
    second_request = replace(request, evaluation_id=str(uuid4()),
        evaluation_dataset=replace(request.evaluation_dataset, dataframe=changed))
    second = api().evaluate_model_promotion(second_request, **kwargs)
    assert first.evidence["dataset"]["source_sha256"] != second.evidence["dataset"]["source_sha256"]
    for key in ("feature_rows_sha256", "labels_sha256", "market_data_sha256"):
        assert first.evidence["dataset"][key] == second.evidence["dataset"][key]
    for role in ("active", "candidate"):
        assert first.evidence[role] == second.evidence[role]


def test_forward_gate_distinguishes_retroactive_and_observed_after_availability(setup):
    request, kwargs, _ = setup(trained_at=datetime(2024, 7, 15, 16, tzinfo=UTC))
    request = replace(request, evaluation_start="2024-08-01",
        promotion_policy=api().ModelPromotionPolicy(minimum_forward_rows=30))
    result = api().evaluate_model_promotion(request, **kwargs)
    assert result.decision != "BLOCKED"
    assert result.evidence["dataset"]["forward_rows"] == result.evidence["dataset"]["evaluation_rows"]
    assert result.evidence["dataset"]["evidence_kind"] == "prospective"


def test_wrong_family_and_runtime_fail_before_deserialization(setup, trained, monkeypatch):
    import stocklab_ml.artifacts.serialization as serialization
    import json
    request, kwargs, _ = setup()
    registry = kwargs["registry"]
    record = registry.register_version(trained[0], training_run_id=str(uuid4()), trained_at_utc=NOW)
    other = save_model_artifact(registry.bind_version(trained[0], model_name=record.model_name,
        model_version=record.model_version), output_dir=registry.path.parent / "artifacts")
    monkeypatch.setattr(serialization.joblib, "load", forbidden)
    result = api().evaluate_model_promotion(request, **(kwargs | {"candidate_artifact": other}))
    assert result.decision == "BLOCKED" and result.reason_codes == ("WRONG_MODEL_FAMILY",)
    path = kwargs["candidate_artifact"].manifest_path
    manifest = json.loads(path.read_text())
    manifest["environment"]["scikit_learn"] = "0.0.0"
    path.write_text(json.dumps(manifest), encoding="utf-8")
    result = api().evaluate_model_promotion(replace(request, evaluation_id=str(uuid4())), **kwargs)
    assert result.decision == "BLOCKED" and result.reason_codes == ("INCOMPATIBLE_RUNTIME",)


def test_better_real_synthetic_model_is_applied_only_explicitly(tmp_path, trained):
    import numpy as np
    from stocklab_ml.modeling import train_logistic_regression
    noisy = history(100)
    prices = 100 + np.random.default_rng(26).normal(0, 3, len(noisy))
    noisy["open"], noisy["close"] = prices, prices
    noisy["high"], noisy["low"] = prices + 1, prices - 1
    older = train_logistic_regression(engineer_features(noisy).dataframe)
    registry = ModelVersionRegistry(tmp_path / "registry.json", clock=lambda: NOW)
    sources = []
    for run in (older, trained[0].baseline_result):
        record = registry.register_version(run, training_run_id=str(uuid4()), trained_at_utc=NOW)
        sources.append(save_model_artifact(registry.bind_version(run, model_name=record.model_name,
            model_version=record.model_version), output_dir=tmp_path / "artifacts"))
    registry.set_active_version(record.model_name, "v000001")
    request = api().ModelPromotionRequest(record.model_name, "v000002", "v000001",
        clean_historical_dataset(history(220), as_of_date="2024-11-01"),
        api().ModelPromotionPolicy(maximum_drawdown_increase=.02), NOW, str(uuid4()), "2024-07-01", "2024-10-31")
    kwargs = dict(registry=registry, audit_store=api().PromotionAuditStore(tmp_path / "audit"),
                  active_artifact=sources[0], candidate_artifact=sources[1], trusted_source=True)
    before = registry.path.read_bytes()
    result = api().evaluate_model_promotion(request, **kwargs)
    assert result.decision == "APPROVED", result.reason_codes
    assert result.evidence["metric_deltas"]["f1_delta"] >= 0
    assert result.evidence["metric_deltas"]["total_return_delta"] > 0
    assert registry.path.read_bytes() == before
    assert api().apply_model_promotion(result, request=request, **kwargs).status == "APPLIED"
    assert registry.get_version(record.model_name, "v000001").status == ModelStatus.ARCHIVED
    assert registry.get_active_version(record.model_name).model_version == "v000002"
    assert api().apply_model_promotion(result, request=request, **kwargs).status == "ALREADY_APPLIED"


def test_s3_store_downloads_exact_artifacts_without_promotion_uploads(setup):
    from test_s3_model_artifacts import FakeS3, store
    request, kwargs, _ = setup()
    client = FakeS3()
    remote = store(client)
    for source in (kwargs["active_artifact"], kwargs["candidate_artifact"]):
        remote.upload_model_artifact(source, registry=kwargs["registry"])
    before = dict(client.objects)
    client.calls.clear()
    result = api().evaluate_model_promotion(request,
        **(kwargs | {"active_artifact": remote, "candidate_artifact": remote}))
    assert result.decision != "BLOCKED"
    assert dict(client.objects) == before and all(call[0] == "get" for call in client.calls)


@pytest.mark.parametrize("field", ["benchmark", "equity_dates", "evaluation_keys_sha256"])
def test_unmatched_benchmark_period_or_labels_cannot_be_compared(setup, field):
    from stocklab_ml.promotion.comparison import compare_evaluations
    from stocklab_ml.promotion.evaluation import BlockedEvidence
    from stocklab_ml.versioning.contracts import _thaw
    request, kwargs, _ = setup()
    result = api().evaluate_model_promotion(request, **kwargs)
    evidence = _thaw(result.evidence)
    evidence["candidate"][field] = "mismatch"
    with pytest.raises(BlockedEvidence, match="DATASET_MISMATCH"):
        compare_evaluations(evidence["active"], evidence["candidate"], evidence["dataset"])
