"""Hand-checked offline research accounting and chronology, independent of live artifacts."""

import importlib.util
import json
from pathlib import Path

import numpy as np
import pandas as pd
import pytest

from stocklab_ml import research


def bars(dates, symbols=("AAPL",), price=100.0):
    return pd.DataFrame([
        (day, symbol, price, price + 1, price - 1, price, 1000)
        for symbol in symbols for day in pd.to_datetime(dates)
    ], columns=["date", "symbol", "open", "high", "low", "close", "volume"]).astype({
        "date": "datetime64[ns]", "symbol": "string", "volume": "Int64",
        **{column: "float64" for column in ["open", "high", "low", "close"]}})


def decisions(dates, actions, allocation=1.0):
    return pd.DataFrame({"date": pd.to_datetime(dates), "symbol": "AAPL",
                         "action": actions, "allocation": allocation})


def test_costs_reduce_cash_and_next_open_quantity():
    prices = bars(pd.date_range("2025-01-02", periods=3))
    curve, trades = research.simulate(prices, decisions(["2025-01-02"], ["BUY"]),
                                      "2025-01-01", "2025-01-04")
    quantity = np.floor(10000 / (100 * 1.0005 * 1.0005) * 1e8) / 1e8
    assert len(trades) == 1
    order = trades.iloc[0]
    assert order.execution_date == pd.Timestamp("2025-01-03")
    assert order.quantity == quantity
    assert order.price == pytest.approx(100.05)
    assert order.fee == pytest.approx(quantity * 100.05 * .0005)
    assert curve.cash.iloc[-1] == pytest.approx(100000 - quantity * 100.05 * 1.0005)
    assert curve.total_value.iloc[-1] == pytest.approx(curve.cash.iloc[-1] + quantity * 100)
    assert curve.total_value.iloc[0] == 100000


def test_sell_uses_next_open_net_proceeds_and_never_shorts():
    prices = bars(pd.date_range("2025-01-02", periods=5))
    curve, trades = research.simulate(prices, decisions(
        ["2025-01-02", "2025-01-03", "2025-01-04"], ["BUY", "SELL", "SELL"]),
        "2025-01-01", "2025-01-06")
    assert trades.action.tolist() == ["BUY", "SELL"]
    quantity = trades.quantity.iloc[0]
    assert trades.price.iloc[1] == pytest.approx(99.95)
    assert curve.cash.iloc[-1] == pytest.approx(
        100000 - quantity * 100.05 * 1.0005 + quantity * 99.95 * .9995)
    assert curve.total_value.iloc[-1] == curve.cash.iloc[-1]


def test_exposure_limit_includes_the_cost_reduction_in_equity():
    prices = bars(pd.date_range("2025-01-02", periods=8))
    curve, trades = research.simulate(prices, decisions(
        pd.date_range("2025-01-02", periods=6), ["BUY"] * 6),
        "2025-01-01", "2025-01-09")
    assert (curve.cash >= 0).all()
    assert ((curve.total_value - curve.cash) / curve.total_value <= .20 + 1e-12).all()
    assert trades.quantity.sum() * 100 <= curve.total_value.iloc[-1] * .20 + 1e-6
    assert (trades.all_in <= trades.equity_before * .10 + 1e-8).all()


def test_cagr_target_requires_a_year_and_beats_same_dates_benchmark():
    dates = pd.to_datetime(["2024-01-01", "2025-01-01"])
    curve = pd.DataFrame({"date": dates, "total_value": [100000., 125000.]})
    benchmark = curve.assign(total_value=[100000., 120000.])
    report = research.assess(curve, benchmark)
    assert report["annualized_return"] == pytest.approx(1.25 ** (365.2425 / 366) - 1)
    assert report["target_met"] is True
    short = curve.assign(date=pd.to_datetime(["2025-01-01", "2025-02-01"]))
    assert research.assess(short, short.assign(total_value=[100000., 101000.]))["target_met"] is False
    assert research.assess(curve, curve)["target_met"] is False


@pytest.mark.parametrize("bad", ["dates", "capital", "nonfinite", "duplicate", "unsorted"])
def test_comparison_rejects_unfair_or_invalid_curves(bad):
    curve = pd.DataFrame({"date": pd.to_datetime(["2024-01-01", "2025-01-01"]),
                          "total_value": [100000., 125000.]})
    other = curve.copy()
    if bad == "dates":
        other.loc[1, "date"] = pd.Timestamp("2025-01-02")
    elif bad == "capital":
        other.loc[0, "total_value"] = 90000
    elif bad == "nonfinite":
        other.loc[1, "total_value"] = np.inf
    elif bad == "duplicate":
        other.loc[1, "date"] = other.loc[0, "date"]
    else:
        other = other.iloc[::-1].reset_index(drop=True)
    with pytest.raises(ValueError):
        research.assess(curve, other)


def test_monthly_context_is_confirmed_and_available_strictly_before_daily_row():
    monthly = bars(pd.date_range("2021-01-01", periods=26, freq="MS"))
    monthly["close"] = 100 + np.arange(26) * .01
    context = research.monthly_context(monthly, pd.Timestamp("2023-02-12"))
    assert context.date.max() == pd.Timestamp("2023-01-31")
    assert context.monthly_rsi.iloc[-1] == 100
    assert context.monthly_multiplier.iloc[-1] == 0
    daily = bars(pd.bdate_range("2021-01-01", "2023-02-10"))
    features = research.research_features(daily, monthly)
    jan31 = features.loc[features.date.eq("2023-01-31")].iloc[0]
    feb1 = features.loc[features.date.eq("2023-02-01")].iloc[0]
    assert jan31.monthly_range_12 < feb1.monthly_range_12
    changed = monthly.copy()
    changed.loc[changed.date.eq("2023-02-01"), ["open", "high", "low", "close"]] *= 10
    pd.testing.assert_frame_equal(features, research.research_features(daily, changed))


def test_purged_training_never_includes_the_prediction_days_target():
    dates = pd.bdate_range("2023-12-01", "2024-01-10")
    frame = pd.DataFrame({"date": dates, "target_date": dates + pd.offsets.BDay(1)})
    train = research.training_rows(frame, pd.Timestamp("2024-01-02"))
    assert train.target_date.max() < pd.Timestamp("2024-01-02")
    assert pd.Timestamp("2024-01-01") not in train.date.tolist()


def test_duplicate_decisions_and_incomplete_market_calendar_fail():
    prices = bars(pd.date_range("2025-01-02", periods=3), ("AAPL", "MSFT"))
    signals = decisions(["2025-01-02"] * 2, ["BUY"] * 2)
    with pytest.raises(ValueError):
        research.simulate(prices, signals, "2025-01-01", "2025-01-04")
    with pytest.raises(ValueError):
        research.simulate(prices.iloc[:-1], signals.iloc[:1], "2025-01-01", "2025-01-04")


def test_spy_proxy_buys_once_with_costs_and_preserves_initial_cash_date():
    prices = bars(pd.date_range("2025-01-02", periods=3), ("SPY",))
    dates = pd.to_datetime(["2025-01-01", "2025-01-02", "2025-01-03", "2025-01-04"])
    curve = research.spy_curve(prices, dates)
    quantity = np.floor(100000 / (100 * 1.0005 * 1.0005) * 1e8) / 1e8
    assert curve.total_value.iloc[0] == 100000
    assert curve.total_value.iloc[1] == pytest.approx(100000 - quantity * 100.05 * 1.0005 + quantity * 100)


def test_selection_does_not_choose_a_high_return_outside_drawdown_tolerance():
    reports = {"rf_base": {"annualized_return": .05, "max_drawdown": .10},
               "rf_dca": {"annualized_return": .30, "max_drawdown": .103},
               "logistic_dca": {"annualized_return": .10, "max_drawdown": .101},
               "boosted_dca": {"annualized_return": -.02, "max_drawdown": .05}}
    assert research.select_candidate(reports) == "logistic_dca"
    reports["logistic_dca"]["annualized_return"] = .04
    assert research.select_candidate(reports) == "rf_base"


def test_walk_forward_fit_is_unchanged_by_later_prices_or_targets():
    dates = pd.bdate_range("2023-01-01", "2024-02-01")
    frame = bars(dates)
    for i, column in enumerate(["return_1d", "ma_5", "ma_20", "rsi_14", "momentum_10", "volatility_20"]):
        frame[column] = np.sin(np.arange(len(frame)) / (i + 1)) + i + 2
    frame["target_date"] = frame.date + pd.offsets.BDay(1)
    frame["target_up_1d"] = np.arange(len(frame)) % 2
    first, audits = research.fit_predict(frame, "rf_base", "2024-01-01", "2024-01-12")
    changed = frame.copy()
    changed.loc[changed.date >= "2024-01-13", "target_up_1d"] = 1
    second, _ = research.fit_predict(changed, "rf_base", "2024-01-01", "2024-01-12")
    pd.testing.assert_frame_equal(first, second)
    assert audits[0]["last_target"] < audits[0]["first_prediction"]


def test_spy_source_requires_matching_adjustment_and_untampered_csv(tmp_path):
    spec = importlib.util.spec_from_file_location("dca_runner", Path(__file__).parents[1] / "examples/dca_research.py")
    runner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runner)
    csv = tmp_path / "cleaned.csv"
    frame = bars(pd.date_range("2025-01-02", periods=3), ("SPY",))
    frame.to_csv(csv, index=False)
    manifest = {"params": {"symbol": "SPY", "interval": "1day", "adjust": "splits"},
                "files": {"cleaned.csv": runner.sha256(csv)}}
    metadata = tmp_path / "manifest.json"
    metadata.write_text(json.dumps(manifest))
    with pytest.raises(ValueError):
        runner.read_source(csv, "SPY", "1day", "all")
    manifest["params"]["adjust"] = "all"
    metadata.write_text(json.dumps(manifest))
    pd.testing.assert_frame_equal(frame, runner.read_source(csv, "SPY", "1day", "all"))
    csv.write_text(csv.read_text().replace("100.0", "101.0"))
    with pytest.raises(ValueError):
        runner.read_source(csv, "SPY", "1day", "all")


def test_monthly_history_gaps_are_rejected_instead_of_joined_across():
    monthly = bars(pd.date_range("2021-01-01", periods=26, freq="MS")).drop(index=10).reset_index(drop=True)
    with pytest.raises(ValueError, match="consecutive"):
        research.monthly_context(monthly, pd.Timestamp("2023-03-01"))


def test_a_truncated_monthly_export_cannot_supply_stale_live_context():
    monthly = bars(pd.date_range("2021-01-01", periods=24, freq="MS"))
    daily = bars(pd.bdate_range("2021-01-01", "2023-02-10"))
    with pytest.raises(ValueError, match="monthly"):
        research.research_features(daily, monthly)


def test_daily_cycle_snapshot_requires_original_manifest_and_hash(tmp_path):
    spec = importlib.util.spec_from_file_location("dca_runner", Path(__file__).parents[1] / "examples/dca_research.py")
    runner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runner)
    csv = tmp_path / "cleaned.csv"
    frame = bars(["2021-01-04", "2026-11-11"], runner.SYMBOLS)
    frame.to_csv(csv, index=False)
    manifest = {"schema_version": 1, "decision_date": "2026-11-11", "history_start": "2021-01-01",
                "symbols": list(runner.SYMBOLS), "files": {"cleaned.csv": runner.sha256(csv)}}
    metadata = tmp_path / "manifest.json"
    metadata.write_text(json.dumps(manifest))
    pd.testing.assert_frame_equal(frame, runner.read_daily_snapshot(csv))
    manifest["symbols"] = ["AAPL"]
    metadata.write_text(json.dumps(manifest))
    with pytest.raises(ValueError):
        runner.read_daily_snapshot(csv)
    manifest["symbols"] = list(runner.SYMBOLS)
    metadata.write_text(json.dumps(manifest))
    csv.write_text(csv.read_text().replace("100.0", "101.0"))
    with pytest.raises(ValueError):
        runner.read_daily_snapshot(csv)
