"""Conservative policy decisions, without manufacturing estimator returns."""

from copy import deepcopy
from importlib import import_module

import pytest


def api():
    return import_module("stocklab_ml.promotion")


def evidence():
    return {
        "dataset": {"evaluation_rows": 100, "symbols": ["AAPL"], "forward_rows": 0},
        "active": {"classification": {"accuracy": .6, "precision": .6, "recall": .6, "f1": .6},
                   "backtest": {"total_return": .08, "max_drawdown": .1}},
        "candidate": {"classification": {"accuracy": .7, "precision": .7, "recall": .7, "f1": .7},
                      "backtest": {"total_return": .12, "max_drawdown": .08}},
        "benchmark": {"total_return": .1},
    }


def test_better_candidate_is_approved():
    assert api().decide_promotion(evidence(), api().ModelPromotionPolicy()) == ("APPROVED", ())


@pytest.mark.parametrize("section,metric,value,reason", [
    ("classification", "f1", .59, "LOWER_F1"),
    ("backtest", "total_return", .079, "LOWER_RETURN"),
    ("backtest", "max_drawdown", .11, "DRAWDOWN_TOO_HIGH"),
    ("classification", "accuracy", .49, "BELOW_MINIMUM_ACCURACY"),
    ("classification", "f1", .49, "BELOW_MINIMUM_F1"),
])
def test_each_regression_rejects_even_with_other_improvements(section, metric, value, reason):
    sample = evidence()
    sample["candidate"][section][metric] = value
    decision, reasons = api().decide_promotion(sample, api().ModelPromotionPolicy())
    assert decision == "REJECTED" and reason in reasons


def test_equal_performance_and_numerical_noise_do_not_promote():
    sample = evidence()
    sample["candidate"] = deepcopy(sample["active"])
    sample["candidate"]["classification"]["accuracy"] = .9
    sample["candidate"]["backtest"]["total_return"] += 1e-14
    assert api().decide_promotion(sample, api().ModelPromotionPolicy()) == (
        "REJECTED", ("INSUFFICIENT_IMPROVEMENT",))


def test_drawdown_direction_and_explicit_tolerance():
    sample = evidence()
    sample["candidate"]["backtest"]["max_drawdown"] = .12
    assert api().decide_promotion(sample, api().ModelPromotionPolicy(maximum_drawdown_increase=.02))[0] == "APPROVED"


@pytest.mark.parametrize("field,value", [
    ("evaluation_rows", 29), ("symbols", []), ("forward_rows", 0),
])
def test_insufficient_samples_block(field, value):
    sample = evidence()
    sample["dataset"][field] = value
    policy = api().ModelPromotionPolicy(minimum_forward_rows=1) if field == "forward_rows" else api().ModelPromotionPolicy()
    assert api().decide_promotion(sample, policy)[0] == "BLOCKED"


def test_benchmark_outperformance_and_initialization_absolute_gates():
    sample = evidence()
    sample["benchmark"]["total_return"] = .2
    assert api().decide_promotion(sample, api().ModelPromotionPolicy(require_benchmark_outperformance=True))[0] == "REJECTED"
    sample["active"] = None
    assert api().decide_promotion(sample, api().ModelPromotionPolicy())[0] == "APPROVED"
    sample["candidate"]["classification"]["f1"] = .1
    assert api().decide_promotion(sample, api().ModelPromotionPolicy())[0] == "REJECTED"


@pytest.mark.parametrize("field", ["minimum_evaluation_rows", "minimum_sample_symbols", "minimum_forward_rows",
    "minimum_accuracy", "minimum_f1", "minimum_return_improvement", "maximum_drawdown_increase"])
@pytest.mark.parametrize("value", [True, float("nan"), float("inf"), -1, "1"])
def test_policy_rejects_invalid_numbers(field, value):
    with pytest.raises(api().ModelPromotionError):
        api().ModelPromotionPolicy(**{field: value})


@pytest.mark.parametrize("field", ["require_benchmark_comparison", "require_benchmark_outperformance"])
@pytest.mark.parametrize("value", [0, 1, "true", None])
def test_policy_requires_real_booleans(field, value):
    with pytest.raises(api().ModelPromotionError):
        api().ModelPromotionPolicy(**{field: value})


@pytest.mark.parametrize("value", [float("nan"), float("inf"), True, -.1, 1.1])
def test_invalid_metric_evidence_is_blocked(value):
    sample = evidence()
    sample["candidate"]["classification"]["f1"] = value
    assert api().decide_promotion(sample, api().ModelPromotionPolicy()) == ("BLOCKED", ("INVALID_METRICS",))


def test_policy_rejects_integer_too_large_for_finite_math():
    with pytest.raises(api().ModelPromotionError):
        api().ModelPromotionPolicy(minimum_return_improvement=10 ** 1000)


@pytest.mark.parametrize("symbols", [
    "AAPL", None, [], ["AAPL", "AAPL"], ["AAPL", "bad symbol"],
    ["aapl"], [""], [1], [["AAPL"]], {"AAPL": 1}, True,
])
def test_invalid_symbol_evidence_cannot_bypass_sample_gate(symbols):
    sample = evidence()
    sample["dataset"]["symbols"] = symbols
    assert api().decide_promotion(sample, api().ModelPromotionPolicy(minimum_sample_symbols=2)) == (
        "BLOCKED", ("INVALID_METRICS",))


@pytest.mark.parametrize("container", [list, tuple])
def test_distinct_valid_symbols_follow_the_existing_symbol_contract(container):
    sample = evidence()
    sample["dataset"]["symbols"] = container(["0005:HKEX", "BRK.B", "BTC/USD", "SHOP:XTSE"])
    assert api().decide_promotion(sample, api().ModelPromotionPolicy(minimum_sample_symbols=4)) == ("APPROVED", ())
