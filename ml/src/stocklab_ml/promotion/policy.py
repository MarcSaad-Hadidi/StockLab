"""V1 nonregression gates; positive drawdown deltas are increased risk."""

import math
import re

from stocklab_ml.data.validation import SYMBOL_PATTERN
from .contracts import ModelPromotionPolicy
from .validation import validate_policy


def _less(left, right):
    return left < right and not math.isclose(left, right, rel_tol=1e-9, abs_tol=1e-12)


def decide_promotion(evidence, policy: ModelPromotionPolicy):
    """Return a decision and every failed gate; never change model status."""
    validate_policy(policy)
    try:
        data = evidence["dataset"]
        if any(type(data[name]) is not int or data[name] < 0 for name in ("evaluation_rows", "forward_rows")):
            raise ValueError
        symbols = data["symbols"]
        if (not isinstance(symbols, (list, tuple)) or not symbols
                or any(not isinstance(symbol, str) or re.fullmatch(SYMBOL_PATTERN, symbol) is None for symbol in symbols)
                or len(set(symbols)) != len(symbols)):
            raise ValueError
        for model in (evidence["candidate"], evidence["active"]):
            if model is None:
                continue
            for metric in ("accuracy", "precision", "recall", "f1"):
                value = model["classification"][metric]
                if type(value) not in (int, float) or not math.isfinite(value) or not 0 <= value <= 1:
                    raise ValueError
            for metric in ("total_return", "max_drawdown"):
                value = model["backtest"][metric]
                if (type(value) not in (int, float) or not math.isfinite(value)
                        or metric == "max_drawdown" and not 0 <= value <= 1):
                    raise ValueError
        if evidence["benchmark"] is not None:
            value = evidence["benchmark"]["total_return"]
            if type(value) not in (int, float) or not math.isfinite(value):
                raise ValueError
    except (KeyError, TypeError, ValueError, OverflowError):
        return "BLOCKED", ("INVALID_METRICS",)
    blocked = []
    if data["evaluation_rows"] < policy.minimum_evaluation_rows or len(data["symbols"]) < policy.minimum_sample_symbols:
        blocked.append("INSUFFICIENT_COMMON_DATA")
    if data["forward_rows"] < policy.minimum_forward_rows:
        blocked.append("INSUFFICIENT_FORWARD_EVIDENCE")
    if blocked:
        return "BLOCKED", tuple(blocked)
    candidate, active = evidence["candidate"], evidence["active"]
    metrics, backtest = candidate["classification"], candidate["backtest"]
    reasons = []
    for key, minimum in (("accuracy", policy.minimum_accuracy), ("f1", policy.minimum_f1)):
        if _less(metrics[key], minimum):
            reasons.append("BELOW_MINIMUM_" + key.upper())
    if active is not None:
        old_metrics, old_backtest = active["classification"], active["backtest"]
        if _less(metrics["f1"], old_metrics["f1"]):
            reasons.append("LOWER_F1")
        if _less(backtest["total_return"], old_backtest["total_return"]):
            reasons.append("LOWER_RETURN")
        if _less(backtest["total_return"] - old_backtest["total_return"], policy.minimum_return_improvement):
            reasons.append("BELOW_MINIMUM_RETURN_IMPROVEMENT")
        if _less(old_backtest["max_drawdown"] + policy.maximum_drawdown_increase, backtest["max_drawdown"]):
            reasons.append("DRAWDOWN_TOO_HIGH")
        if not (_less(old_metrics["f1"], metrics["f1"])
                or _less(old_backtest["total_return"], backtest["total_return"])
                or _less(backtest["max_drawdown"], old_backtest["max_drawdown"])):
            reasons.append("INSUFFICIENT_IMPROVEMENT")
    if policy.require_benchmark_comparison and evidence["benchmark"] is None:
        return "BLOCKED", ("MISSING_BENCHMARK",)
    if policy.require_benchmark_outperformance and not _less(evidence["benchmark"]["total_return"], backtest["total_return"]):
        reasons.append("BENCHMARK_REQUIREMENT_FAILED")
    return ("REJECTED", tuple(reasons)) if reasons else ("APPROVED", ())
