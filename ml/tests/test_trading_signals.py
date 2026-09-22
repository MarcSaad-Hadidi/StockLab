"""Fixed signal policy, target-free contracts, and future-label independence."""

from dataclasses import asdict
from fractions import Fraction

import numpy as np
import pandas as pd
import pytest

from stocklab_ml.signals import (
    BUY_THRESHOLD, SELL_THRESHOLD, TradingSignal, TradingSignalError,
    generate_trading_signals,
)


def predictions(probabilities=(0.75, 0.50, 0.20)):
    count = len(probabilities)
    return pd.DataFrame({
        "date": pd.date_range("2024-01-01", periods=count, freq="B").as_unit("ns"),
        "symbol": pd.array(["AAPL"] * count, dtype="string"),
        "predicted_class": np.ones(count, dtype="int64"),
        "probability_up": np.array(probabilities, dtype="float64"),
    })


@pytest.mark.parametrize("probability,expected", [
    (1.00, "BUY"), (0.60, "BUY"), (0.6000001, "BUY"),
    (0.5999999, "HOLD"), (0.59, "HOLD"), (0.50, "HOLD"),
    (0.41, "HOLD"), (0.4000001, "HOLD"),
    (0.40, "SELL"), (0.3999999, "SELL"), (0.00, "SELL"),
])
def test_inclusive_policy_boundaries(probability, expected):
    result = generate_trading_signals(predictions([probability]), model_name="future_model")
    assert result.signals.signal.tolist() == [expected]
    assert BUY_THRESHOLD == 0.60 and SELL_THRESHOLD == 0.40
    assert {member.value for member in TradingSignal} == {"BUY", "SELL", "HOLD"}
    assert result.report.input_rows == result.report.output_rows == 1
    assert result.report.buy_count + result.report.sell_count + result.report.hold_count == 1


def test_contract_sorting_counts_and_input_immutability():
    frame = predictions([0.75, 0.50, 0.20, 0.81])
    frame["symbol"] = pd.array(["TSLA:NASDAQ", "NA", "001", "AAPL"], dtype="string")
    frame.loc[3, "date"] = frame.loc[0, "date"]
    frame = frame.iloc[::-1]
    before = frame.copy(deep=True)
    result = generate_trading_signals(frame, model_name="gradient_boosting_baseline")
    assert list(result.signals) == ["date", "symbol", "model_name", "predicted_class", "probability_up", "signal"]
    assert [str(dtype) for dtype in result.signals.dtypes] == [
        "datetime64[ns]", "string", "string", "int64", "float64", "string",
    ]
    assert result.signals.index.tolist() == list(range(4))
    assert result.signals.symbol.tolist() == ["AAPL", "TSLA:NASDAQ", "NA", "001"]
    assert result.signals.model_name.tolist() == ["gradient_boosting_baseline"] * 4
    assert result.signals.signal.tolist() == ["BUY", "BUY", "HOLD", "SELL"]
    assert asdict(result.report) == {
        "model_name": "gradient_boosting_baseline", "input_rows": 4, "output_rows": 4,
        "buy_count": 2, "sell_count": 1, "hold_count": 1,
        "buy_threshold": 0.6, "sell_threshold": 0.4,
        "date_min": "2024-01-01", "date_max": "2024-01-03",
        "symbols": ["001", "AAPL", "NA", "TSLA:NASDAQ"],
    }
    repeated = generate_trading_signals(frame, model_name="gradient_boosting_baseline")
    pd.testing.assert_frame_equal(result.signals, repeated.signals, check_exact=True)
    assert result.report == repeated.report
    pd.testing.assert_frame_equal(frame, before, check_exact=True)


@pytest.mark.parametrize("column,first,second", [
    ("actual_class", [0, 0, 0], [1, 1, 1]),
    ("target_date", pd.date_range("2025-01-01", periods=3), pd.date_range("2030-01-01", periods=3)),
    ("actual_class", [None] * 3, [object() for _ in range(3)]),
    ("target_date", [pd.NaT] * 3, ["not a date"] * 3),
])
def test_optional_historical_values_are_never_read(column, first, second):
    frame = predictions()
    target_free = generate_trading_signals(frame, model_name="baseline")
    for values in [first, second]:
        historical = frame.assign(**{column: values})
        result = generate_trading_signals(historical, model_name="baseline")
        pd.testing.assert_frame_equal(result.signals, target_free.signals, check_exact=True)
        assert result.report == target_free.report


def test_predicted_class_is_audit_only_and_symbols_are_independent():
    frame = predictions([0.55, 0.45, 0.75, 0.2])
    frame["predicted_class"] = [1, 0, 0, 1]
    frame["symbol"] = ["AAPL", "MSFT", "NVDA", "TSLA"]
    result = generate_trading_signals(frame, model_name="baseline")
    assert result.signals.signal.tolist() == ["HOLD", "HOLD", "BUY", "SELL"]
    assert result.signals.predicted_class.tolist() == [1, 0, 0, 1]
    frame.loc[1, "probability_up"] = 1.0
    changed = generate_trading_signals(frame, model_name="baseline")
    pd.testing.assert_frame_equal(result.signals.drop(index=1), changed.signals.drop(index=1))


def test_explicit_custom_thresholds_and_numeric_output_types():
    frame = predictions([0.2, 0.5, 0.8])
    frame["date"] = frame.date.dt.as_unit("us")
    frame["predicted_class"] = frame.predicted_class.astype("Int8")
    frame["probability_up"] = frame.probability_up.astype("Float64")
    result = generate_trading_signals(frame, model_name="Model-2.1", buy_threshold=0.8, sell_threshold=0.2)
    assert result.signals.signal.tolist() == ["SELL", "HOLD", "BUY"]
    assert result.report.buy_threshold == 0.8 and result.report.sell_threshold == 0.2
    assert str(result.signals.date.dtype) == "datetime64[ns]"
    extremes = generate_trading_signals(predictions([0, 0.5, 1]), model_name="m", buy_threshold=1, sell_threshold=0)
    assert extremes.signals.signal.tolist() == ["SELL", "HOLD", "BUY"]


def test_real_fractional_thresholds_match_reported_float_policy():
    result = generate_trading_signals(predictions([0.4, 0.5, 0.6]), model_name="m",
                                      buy_threshold=Fraction(3, 5), sell_threshold=Fraction(2, 5))
    assert result.signals.signal.tolist() == ["SELL", "HOLD", "BUY"]
    assert result.report.buy_threshold == 0.6 and result.report.sell_threshold == 0.4


@pytest.mark.parametrize("buy,sell", [
    (0.4, 0.6), (0.5, 0.5), (1.1, 0.4), (0.6, -0.1),
    (np.nan, 0.4), (0.6, np.nan), (np.inf, 0.4), (0.6, -np.inf),
    (True, 0.4), (0.6, False), (np.bool_(True), 0.4),
    ("0.6", 0.4), (0.6, None), (0.6 + 0j, 0.4),
    (10**100, 0.4), (0.6, -(10**100)),
    (Fraction(1, 2) + Fraction(1, 10**100), Fraction(1, 2)),
])
def test_invalid_thresholds_fail_without_correction(buy, sell):
    with pytest.raises(TradingSignalError, match="threshold"):
        generate_trading_signals(predictions(), model_name="m", buy_threshold=buy, sell_threshold=sell)


@pytest.mark.parametrize("name", [None, "", "  ", 42, " model", "model ", "two models", "../model", "a\n", "x" * 129])
def test_invalid_model_name(name):
    with pytest.raises(TradingSignalError, match="model name"):
        generate_trading_signals(predictions(), model_name=name)


@pytest.mark.parametrize("value", [-0.01, 1.01, np.nan, np.inf, -np.inf, "abc", None, True, False, 0.2 + 0j])
def test_invalid_probabilities(value):
    frame = predictions()
    frame["probability_up"] = value
    with pytest.raises(TradingSignalError, match="probability"):
        generate_trading_signals(frame, model_name="m")


@pytest.mark.parametrize("value", [-1, 2, 0.5, "1", np.nan, True, False, None])
def test_invalid_classes(value):
    frame = predictions()
    frame["predicted_class"] = value
    with pytest.raises(TradingSignalError, match="class"):
        generate_trading_signals(frame, model_name="m")


@pytest.mark.parametrize("bad", [
    "not_frame", "empty", "missing", "duplicate_column", "extra", "model_column", "duplicate_row",
    "date_string", "date_missing", "date_timezone", "date_intraday", "date_out_of_range",
    "symbol_lowercase", "symbol_empty", "symbol_null", "symbol_number", "symbol_whitespace",
])
def test_invalid_schema_dates_symbols_and_keys(bad):
    frame = predictions()
    if bad == "not_frame":
        frame = []
    elif bad == "empty":
        frame = frame.iloc[:0]
    elif bad == "missing":
        frame = frame.drop(columns="symbol")
    elif bad == "duplicate_column":
        frame = pd.concat([frame, frame[["symbol"]]], axis=1)
    elif bad in {"extra", "model_column"}:
        frame["model_name" if bad == "model_column" else "unexpected"] = "wrong_model"
    elif bad == "duplicate_row":
        frame = pd.concat([frame, frame.iloc[:1]], ignore_index=True)
    elif bad == "date_string":
        frame["date"] = frame.date.astype(str)
    elif bad == "date_missing":
        frame.loc[0, "date"] = pd.NaT
    elif bad == "date_timezone":
        frame["date"] = frame.date.dt.tz_localize("UTC")
    elif bad == "date_intraday":
        frame.loc[0, "date"] += pd.Timedelta(hours=1)
    elif bad == "date_out_of_range":
        frame["date"] = pd.date_range("2500-01-01", periods=3).as_unit("us")
    else:
        frame["symbol"] = {"symbol_lowercase": "aapl", "symbol_empty": "", "symbol_null": pd.NA,
                           "symbol_number": 12, "symbol_whitespace": " AAPL"}[bad]
    with pytest.raises(TradingSignalError):
        generate_trading_signals(frame, model_name="m")


def test_real_model_holdouts_and_target_free_inference_share_policy(monkeypatch):
    from sklearn.ensemble import RandomForestClassifier
    from sklearn.linear_model import LogisticRegression
    from stocklab_ml.modeling import predict_direction, predict_random_forest_direction, train_random_forest
    from test_model_dataset import feature_frame

    features = feature_frame()
    forest = train_random_forest(features)
    logistic = forest.baseline_result
    inference = [predict_direction(logistic.pipeline, features), predict_random_forest_direction(forest.model, features)]

    def forbidden(*args, **kwargs):
        raise AssertionError("Signal generation cannot fit or predict")

    for estimator in [LogisticRegression, RandomForestClassifier]:
        for method in ["fit", "predict", "predict_proba"]:
            monkeypatch.setattr(estimator, method, forbidden)
    for model, future in zip([logistic, forest], inference, strict=True):
        historical = generate_trading_signals(model.predictions, model_name=model.report.model_name)
        stripped = generate_trading_signals(model.predictions.drop(columns=["actual_class", "target_date"]),
                                            model_name=model.report.model_name)
        pd.testing.assert_frame_equal(historical.signals, stripped.signals, check_exact=True)
        assert historical.report == stripped.report
        live = generate_trading_signals(future, model_name=model.report.model_name)
        assert live.report.output_rows == len(features)
        for result in [historical, live]:
            assert set(result.signals.signal) <= {"BUY", "SELL", "HOLD"}
            assert result.report.input_rows == result.report.output_rows
