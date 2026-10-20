"""Trust-boundary checks shared by evaluation, application and audit reads."""

from dataclasses import asdict, fields
from datetime import datetime
import hashlib
import math
import re

from stocklab_ml.backtesting import BacktestConfig
from stocklab_ml.backtesting.contracts import BacktestReport
from stocklab_ml.backtesting.benchmark_contracts import BuyAndHoldReport, BenchmarkComparisonReport
from stocklab_ml.backtesting.validation import validate_market_data
from stocklab_ml.versioning import ModelVersionError
from stocklab_ml.versioning.validation import (
    _day, canonical, decode_record, frame_digest, validate_metrics, validate_name,
    validate_timestamp, validate_uuid, validate_version,
)
from stocklab_ml.modeling.contracts import ModelEvaluationMetrics
from stocklab_ml.artifacts.validation import metadata_digest
from .contracts import ModelPromotionError, ModelPromotionPolicy


def digest(payload):
    return hashlib.sha256(canonical(payload).encode("utf-8")).hexdigest()


def validate_policy(policy):
    for name in ("minimum_evaluation_rows", "minimum_sample_symbols", "minimum_forward_rows"):
        value = getattr(policy, name)
        if type(value) is not int or value < (0 if name == "minimum_forward_rows" else 1):
            raise ModelPromotionError(f"{name} requires a nonboolean integer within its range.")
    for name in ("minimum_accuracy", "minimum_f1", "minimum_return_improvement", "maximum_drawdown_increase"):
        value = getattr(policy, name)
        try:
            finite = type(value) in (int, float) and math.isfinite(value)
        except OverflowError:
            finite = False
        if (not finite or value < 0
                or name != "minimum_return_improvement" and value > 1):
            raise ModelPromotionError(f"{name} requires a finite nonnegative real within its range.")
    for name in ("require_benchmark_comparison", "require_benchmark_outperformance"):
        if type(getattr(policy, name)) is not bool:
            raise ModelPromotionError(f"{name} requires an actual boolean.")
    if policy.require_benchmark_outperformance and not policy.require_benchmark_comparison:
        raise ModelPromotionError("Benchmark outperformance requires benchmark comparison.")


def validate_request(request):
    try:
        validate_name(request.model_name)
        validate_version(request.candidate_version)
        if request.expected_active_version is not None:
            validate_version(request.expected_active_version)
        validate_uuid(request.evaluation_id)
        validate_timestamp(request.evaluation_timestamp_utc)
        start, end = _day(request.evaluation_start), _day(request.evaluation_end)
        if start > end or end >= request.evaluation_timestamp_utc.date():
            raise ValueError("Evaluation requires an ordered, fully observed historical daily interval.")
        if type(request.initialize) is not bool or request.initialize and request.expected_active_version is not None:
            raise ValueError("Initialization explicitly expects no Active version.")
        if not isinstance(request.promotion_policy, ModelPromotionPolicy) or not isinstance(request.backtest_config, BacktestConfig):
            raise ValueError("Validated policy and backtest configuration are required.")
        validate_policy(request.promotion_policy)
        BacktestConfig(**asdict(request.backtest_config))
        validate_market_data(request.evaluation_dataset)
    except ModelPromotionError:
        raise
    except Exception as error:
        raise ModelPromotionError("Invalid explicit model promotion request.") from error


def request_snapshot(request):
    validate_request(request)
    return {"evaluation_id": request.evaluation_id, "model_name": request.model_name,
            "candidate_version": request.candidate_version, "expected_active_version": request.expected_active_version,
            "initialize": request.initialize, "evaluation_start": request.evaluation_start,
            "evaluation_end": request.evaluation_end,
            "evaluation_timestamp_utc": request.evaluation_timestamp_utc.isoformat(),
            "promotion_policy": asdict(request.promotion_policy), "backtest_config": asdict(request.backtest_config),
            "source_sha256": frame_digest(request.evaluation_dataset.dataframe),
            "source_report": asdict(request.evaluation_dataset.report)}


def _validate_evidence(decision, evidence):
    from .comparison import compare_evaluations
    if set(evidence) != {"dataset", "active", "candidate", "benchmark", "metric_deltas"}:
        raise ValueError
    data = evidence["dataset"]
    if set(data) != {"source_sha256", "market_data_sha256", "feature_rows_sha256", "labels_sha256",
            "evaluation_rows", "symbols", "feature_start", "feature_end", "target_end", "training_boundary",
            "requested_start", "requested_end", "forward_rows", "evidence_kind", "availability_rule",
            "backtest_config", "registry_path"}:
        raise ValueError
    for key in ("evaluation_rows", "forward_rows"):
        if type(data[key]) is not int or data[key] < (1 if key == "evaluation_rows" else 0):
            raise ValueError
    if (data["forward_rows"] > data["evaluation_rows"] or not isinstance(data["symbols"], list)
            or not data["symbols"] or sorted(set(data["symbols"])) != data["symbols"]
            or not isinstance(data["registry_path"], str) or not data["registry_path"]):
        raise ValueError
    if data["evidence_kind"] != ("historical" if not data["forward_rows"] else
            "prospective" if data["forward_rows"] == data["evaluation_rows"] else "mixed"):
        raise ValueError
    start, end, target_end = (_day(data[key]) for key in ("feature_start", "feature_end", "target_end"))
    if (not _day(data["requested_start"]) <= start <= end < target_end <= _day(data["requested_end"])
            or start < _day(data["training_boundary"])
            or target_end >= datetime.fromisoformat(decision.evaluated_at_utc).date()):
        raise ValueError
    config = BacktestConfig(**data["backtest_config"])
    records, supports = [], []
    model_keys = {"record", "artifact_sha256", "metadata_sha256", "runtime", "classification", "backtest",
        "benchmark", "benchmark_comparison", "evaluation_keys_sha256", "predictions_sha256", "decisions_sha256",
        "equity_sha256", "equity_dates", "benchmark_equity_sha256"}
    for role in ("active", "candidate"):
        model = evidence[role]
        if model is None and role == "active" and decision.active_model_version is None:
            continue
        if set(model) != model_keys:
            raise ValueError
        record = decode_record(model["record"])
        records.append(record)
        if (record.model_name != decision.model_name or record.model_version != (
                decision.candidate_model_version if role == "candidate" else decision.active_model_version)
                or record.trained_at_utc > datetime.fromisoformat(decision.evaluated_at_utc)
                or record.status.value != ("Archived" if role == "candidate" else "Active")
                or model["metadata_sha256"] != metadata_digest(record)):
            raise ValueError
        metrics = ModelEvaluationMetrics(**model["classification"])
        validate_metrics(metrics, decision.model_name)
        supports.append((metrics.support_positive, metrics.support_negative))
        if metrics.total_rows != data["evaluation_rows"] or model["evaluation_keys_sha256"] != data["labels_sha256"]:
            raise ValueError
        for key in ("artifact_sha256", "metadata_sha256", "evaluation_keys_sha256", "predictions_sha256",
                    "decisions_sha256", "equity_sha256", "benchmark_equity_sha256"):
            if re.fullmatch(r"[a-f0-9]{64}", model[key]) is None:
                raise ValueError
        for key, contract in (("backtest", BacktestReport), ("benchmark", BuyAndHoldReport),
                              ("benchmark_comparison", BenchmarkComparisonReport)):
            report = model[key]
            if set(report) != {field.name for field in fields(contract)}:
                raise ValueError
            if (report["start_date"] != data["feature_start"] or report["end_date"] != data["target_end"]
                    or report["symbols"] != data["symbols"] or report["initial_capital"] != config.initial_capital):
                raise ValueError
        backtest = model["backtest"]
        if (backtest["model_name"] != decision.model_name or backtest["config"] != asdict(config)
                or model["benchmark"] != evidence["benchmark"]
                or model["benchmark_comparison"]["equity_dates"] != model["equity_dates"]
                or model["equity_dates"] != sorted(set(model["equity_dates"]))
                or model["equity_dates"][0] != data["feature_start"]
                or model["equity_dates"][-1] != data["target_end"]):
            raise ValueError
        for key in ("total_return", "max_drawdown", "final_value", "win_rate"):
            value = backtest[key]
            if type(value) not in (int, float) or not math.isfinite(value):
                raise ValueError
        if (not 0 <= backtest["max_drawdown"] <= 1 or not 0 <= backtest["win_rate"] <= 1
                or backtest["final_value"] <= 0 or not math.isclose(backtest["total_return"],
                    backtest["final_value"] / config.initial_capital - 1, abs_tol=1e-12, rel_tol=1e-9)):
            raise ValueError
    if (len(set(supports)) != 1 or data["training_boundary"] != max(record.split_date for record in records)
            or canonical(compare_evaluations(evidence["active"], evidence["candidate"], data)) != canonical(evidence)):
        raise ValueError
    for key in ("source_sha256", "market_data_sha256", "feature_rows_sha256", "labels_sha256"):
        if re.fullmatch(r"[a-f0-9]{64}", data[key]) is None:
            raise ValueError


def validate_decision(decision):
    try:
        payload = decision.to_dict()
        canonical(payload)
        validate_uuid(decision.evaluation_id)
        validate_name(decision.model_name)
        validate_version(decision.candidate_model_version)
        if decision.active_model_version is not None:
            validate_version(decision.active_model_version)
        validate_timestamp(datetime.fromisoformat(decision.evaluated_at_utc))
        ModelPromotionPolicy(**payload["policy_snapshot"])
        if (decision.decision not in {"APPROVED", "REJECTED", "BLOCKED"}
                or any(not isinstance(code, str) or re.fullmatch(r"[A-Z][A-Z0-9_]*", code) is None
                       for code in decision.reason_codes)
                or decision.decision == "APPROVED" and decision.reason_codes
                or decision.decision != "APPROVED" and not decision.reason_codes
                or re.fullmatch(r"[a-f0-9]{64}", decision.request_sha256) is None):
            raise ValueError
        if decision.decision != "BLOCKED":
            _validate_evidence(decision, payload["evidence"])
    except (ModelVersionError, TypeError, ValueError, KeyError, AttributeError, IndexError, OverflowError) as error:
        raise ModelPromotionError("Invalid promotion decision evidence.") from error
