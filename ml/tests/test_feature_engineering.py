"""Independent formula examples and causal, strict feature dataset boundaries."""

import numpy as np
import pandas as pd
import pytest

from stocklab_ml.features import FEATURE_COLUMNS, FeatureEngineeringError, engineer_features
from stocklab_ml.features import engineering


def clean_frame(closes, symbol="AAPL"):
    prices = np.asarray(closes, dtype="float64")
    return pd.DataFrame({
        "date": pd.date_range("2024-01-01", periods=len(prices), freq="B").as_unit("ns"),
        "symbol": pd.array([symbol] * len(prices), dtype="string"),
        "open": prices, "high": prices, "low": prices, "close": prices,
        "volume": pd.array([9007199254740993] * len(prices), dtype="Int64"),
    })


def test_individual_formulas_and_first_defined_observations():
    prices = pd.Series(np.arange(1, 31, dtype=float))
    returns = engineering._compute_return(pd.Series([100.0, 102.0]))
    assert np.isnan(returns.iloc[0])
    assert returns.iloc[1] == pytest.approx(0.02)
    short = engineering._compute_sma(prices, 5)
    long = engineering._compute_sma(prices, 20)
    assert short.iloc[:4].isna().all() and short.iloc[4] == 3
    assert long.iloc[:19].isna().all() and long.iloc[19] == 10.5
    momentum = engineering._compute_momentum(pd.Series(np.arange(100, 121, dtype=float)))
    assert momentum.iloc[:10].isna().all()
    assert momentum.iloc[10] == pytest.approx(0.10)
    # Twenty returns alternating 0% and 2% have population std 1%.
    volatility = engineering._compute_volatility(pd.Series([np.nan] + [0.0, 0.02] * 10))
    assert volatility.iloc[:20].isna().all()
    assert volatility.iloc[20] == pytest.approx(0.01, abs=1e-14)


def test_mixed_wilder_seed_and_recursive_regression():
    # Alternating +2/-1: initial gains=14/14=1, losses=7/14=0.5.
    # Subsequent (gain, loss) states: (15/14,13/28),
    # (195/196,197/392), ...; exact rational arithmetic gives values below.
    prices = np.array([100 + 2 * ((i + 1) // 2) - i // 2 for i in range(31)], dtype=float)
    rsi = engineering._compute_rsi_wilder(pd.Series(prices))
    assert rsi.iloc[:14].isna().all()
    np.testing.assert_allclose(rsi.iloc[14:17], [66.66666666666667, 69.76744186046511, 66.43952299829642])
    assert engineer_features(clean_frame(prices)).dataframe.rsi_14.iloc[0] == pytest.approx(66.07317239868347, abs=1e-12)


@pytest.mark.parametrize("prices,rsi", [
    (np.full(30, 100.0), 50.0), (np.arange(1, 31), 100.0), (np.arange(30, 0, -1), 0.0),
])
def test_rsi_extremes_and_flat_features(prices, rsi):
    result = engineer_features(clean_frame(prices)).dataframe
    assert (result.rsi_14 == rsi).all()
    assert np.isfinite(result[FEATURE_COLUMNS].to_numpy(dtype=float)).all()
    if rsi == 50:
        assert (result[["return_1d", "momentum_10", "volatility_20"]] == 0).all().all()
        assert (result[["ma_5", "ma_20"]] == 100).all().all()


def test_constant_nonzero_returns_have_zero_volatility():
    result = engineer_features(clean_frame(100 * 1.01 ** np.arange(40))).dataframe
    np.testing.assert_allclose(result.volatility_20, 0, atol=1e-15)


@pytest.mark.parametrize("count,expected", [(21, 1), (30, 10), (60, 40)])
def test_output_contract_counts_immutability_and_determinism(count, expected):
    frame = clean_frame(np.arange(100, 100 + count))
    before = frame.copy(deep=True)
    result = engineer_features(frame)
    again = engineer_features(frame)
    pd.testing.assert_frame_equal(frame, before)
    pd.testing.assert_frame_equal(result.dataframe, again.dataframe)
    assert result.report == again.report
    assert list(result.dataframe) == list(frame) + [c for c in FEATURE_COLUMNS if c != "volume"]
    assert result.dataframe.dtypes.astype(str).tolist() == ["datetime64[ns]", "string"] + ["float64"] * 4 + ["Int64"] + ["float64"] * 6
    assert result.output_path is None and result.report_path is None
    report = result.report
    assert (report.input_rows, report.output_rows, report.warmup_rows_removed) == (count, expected, 20)
    assert report.rows_per_symbol == {"AAPL": expected}
    assert report.warmup_removed_per_symbol == {"AAPL": 20}
    assert report.date_min == frame.date.iloc[20].date().isoformat()
    assert report.date_max == frame.date.iloc[-1].date().isoformat()
    assert report.feature_columns == FEATURE_COLUMNS
    assert report.parameters == {"return_period": 1, "ma_short_window": 5, "ma_long_window": 20,
                                 "rsi_window": 14, "momentum_window": 10, "volatility_window": 20,
                                 "volatility_ddof": 0, "volatility_annualized": False}
    pd.testing.assert_frame_equal(result.dataframe[frame.columns], frame.iloc[20:].reset_index(drop=True))


@pytest.mark.parametrize("count", [1, 14, 20])
def test_insufficient_history_fails(count):
    with pytest.raises(FeatureEngineeringError, match="21.*per symbol"):
        engineer_features(clean_frame(np.ones(count)))


def test_multisymbol_independence_counts_and_short_symbol_failure():
    a = clean_frame(np.arange(100, 130))
    b = clean_frame(np.arange(225, 200, -1), "MSFT")
    both = pd.concat([a, b], ignore_index=True)
    result = engineer_features(both)
    expected = pd.concat([engineer_features(a).dataframe, engineer_features(b).dataframe], ignore_index=True)
    pd.testing.assert_frame_equal(result.dataframe, expected)
    assert result.report.symbols == ["AAPL", "MSFT"]
    assert result.report.rows_per_symbol == {"AAPL": 10, "MSFT": 5}
    assert result.report.warmup_removed_per_symbol == {"AAPL": 20, "MSFT": 20}
    assert (result.report.input_rows, result.report.output_rows, result.report.warmup_rows_removed) == (55, 15, 40)
    with pytest.raises(FeatureEngineeringError, match="MSFT"):
        engineer_features(pd.concat([a, b.iloc[:20]], ignore_index=True))


def test_prefix_invariance_including_extreme_future_changes():
    a = clean_frame(100 + np.sin(np.arange(60)) * 10)
    b = clean_frame(200 + np.cos(np.arange(60)) * 20, "MSFT")
    b["date"] += pd.Timedelta(days=3)
    full = pd.concat([a, b], ignore_index=True)
    prefix = full.groupby("symbol", sort=False).head(40).reset_index(drop=True)
    for name in ["open", "high", "low", "close"]:
        full.loc[full.groupby("symbol").cumcount() >= 40, name] *= 1000
    actual = engineer_features(full).dataframe.groupby("symbol", sort=False).head(20).reset_index(drop=True)
    pd.testing.assert_frame_equal(actual, engineer_features(prefix).dataframe, check_exact=True)


def test_calendar_gaps_are_not_filled_or_counted_as_sessions():
    frame = clean_frame(np.arange(1, 31))
    frame.loc[10:, "date"] += pd.Timedelta(days=100)
    result = engineer_features(frame).dataframe
    assert len(result) == 10
    assert result.ma_20.iloc[0] == 11.5
    assert result.momentum_10.iloc[0] == pytest.approx(21 / 11 - 1)
    assert result.date.iloc[0] == frame.date.iloc[20]


@pytest.mark.parametrize("corruption", ["extra", "missing", "duplicate_column", "column_order", "nan", "inf",
    "ohlc", "zero", "negative_volume", "float_volume", "object_price", "date_text", "intraday",
    "timezone", "symbol", "duplicate_key", "unsorted", "index", "empty"])
def test_invalid_cleaned_input_is_rejected_without_repair(corruption):
    frame = clean_frame(np.arange(1, 31))
    if corruption == "extra": frame["target"] = 1
    elif corruption == "missing": frame = frame.drop(columns="volume")
    elif corruption == "duplicate_column": frame.columns = list(frame.columns[:-1]) + ["close"]
    elif corruption == "column_order": frame = frame[list(reversed(frame.columns))]
    elif corruption in ("nan", "inf"): frame.loc[25, "close"] = np.nan if corruption == "nan" else np.inf
    elif corruption == "ohlc": frame.loc[25, "high"] = 1
    elif corruption == "zero": frame.loc[25, "close"] = 0
    elif corruption == "negative_volume": frame.loc[25, "volume"] = -1
    elif corruption == "float_volume": frame["volume"] = frame.volume.astype(float)
    elif corruption == "object_price": frame["close"] = frame.close.astype(object)
    elif corruption == "date_text": frame["date"] = frame.date.astype("string")
    elif corruption == "intraday": frame.loc[25, "date"] += pd.Timedelta(hours=1)
    elif corruption == "timezone": frame["date"] = frame.date.dt.tz_localize("UTC")
    elif corruption == "symbol": frame.loc[25, "symbol"] = " aapl "
    elif corruption == "duplicate_key": frame.loc[25, "date"] = frame.date.iloc[24]
    elif corruption == "unsorted": frame = frame.iloc[::-1].reset_index(drop=True)
    elif corruption == "index": frame.index += 1
    elif corruption == "empty": frame = frame.iloc[:0]
    with pytest.raises(FeatureEngineeringError, match="cleaned input"):
        engineer_features(frame)


def test_reapplication_and_non_dataframe_fail():
    with pytest.raises(FeatureEngineeringError):
        engineer_features(engineer_features(clean_frame(np.arange(1, 51))).dataframe)
    with pytest.raises(FeatureEngineeringError):
        engineer_features(None)


@pytest.mark.parametrize("position,value", [(5, np.inf), (15, np.nan), (15, -1), (15, 101),
                                          (25, np.nan), (25, np.inf), (25, -1), (25, 101)])
def test_unexpected_feature_invalidity_is_never_silently_dropped(monkeypatch, position, value):
    compute = engineering._compute_rsi_wilder
    def corrupt(prices):
        result = compute(prices)
        result.iloc[position] = value
        return result
    monkeypatch.setattr(engineering, "_compute_rsi_wilder", corrupt)
    with pytest.raises(FeatureEngineeringError, match="Unexpected feature invalidity"):
        engineer_features(clean_frame(np.arange(1, 41)))
