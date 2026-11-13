"""Executable labels, feature chronology and honest horizon-aware inference."""

import importlib.util
import json
from pathlib import Path

import numpy as np
import pandas as pd
import pytest
from sklearn.exceptions import ConvergenceWarning

from stocklab_ml import research, swing_research as swing


def bars(dates, symbol="AAPL", opens=None):
    dates = pd.to_datetime(dates).astype("datetime64[ns]")
    prices = np.asarray(opens if opens is not None else 100 + np.sin(np.arange(len(dates)) / 5), dtype=float)
    return pd.DataFrame({"date": dates, "symbol": symbol, "open": prices,
                         "high": prices * 1.01, "low": prices * .99, "close": prices, "volume": 1000}).astype({
        "symbol": "string", "volume": "Int64"})


def test_executable_target_excludes_the_gap_before_next_open():
    dates = pd.bdate_range("2024-01-01", periods=30)
    prices = bars(dates, opens=[100.] + [150.] * 29)
    spy = bars(dates, "SPY", opens=np.full(30, 100.))
    labels = swing.add_targets(prices[["date", "symbol"]], prices, spy)
    assert labels.target_up_1d.iloc[0] == 1
    assert labels.target_5.iloc[0] == 0
    assert labels.forward_return_5.iloc[0] == 0
    assert labels.target_date_5.iloc[0] == dates[6]
    assert labels.target_5.iloc[-6:].isna().all()
    assert labels.target_20.iloc[-21:].isna().all()


def test_forward_return_and_excess_targets_use_matching_future_opens():
    dates = pd.bdate_range("2024-01-01", periods=40)
    prices = bars(dates, opens=100 + np.arange(40))
    spy = bars(dates, "SPY", opens=200 + 2 * np.arange(40))
    labels = swing.add_targets(prices[["date", "symbol"]], prices, spy)
    assert labels.forward_return_20.iloc[0] == pytest.approx(121 / 101 - 1)
    assert labels.target_20.iloc[0] == 1
    assert labels.target_excess_20.iloc[0] == 0
    assert labels.excess_return_20.iloc[0] == pytest.approx(0)
    assert labels.target_date_20.iloc[0] == dates[21]


def test_target_labels_never_cross_symbols_or_missing_spy_dates():
    dates = pd.bdate_range("2024-01-01", periods=40)
    aapl = bars(dates, opens=100 + np.arange(40))
    msft = bars(dates, "MSFT", opens=200 - np.arange(40))
    prices = pd.concat([aapl, msft], ignore_index=True)
    spy = bars(dates, "SPY")
    labels = swing.add_targets(prices[["date", "symbol"]], prices, spy)
    assert labels.loc[labels.symbol.eq("AAPL"), "target_20"].iloc[0] == 1
    assert labels.loc[labels.symbol.eq("MSFT"), "target_20"].iloc[0] == 0
    with pytest.raises(ValueError, match="calendar"):
        swing.add_targets(prices[["date", "symbol"]], prices, spy.iloc[:-1].reset_index(drop=True))


def test_causal_swing_features_keep_latest_unlabeled_rows():
    dates = pd.bdate_range("2010-01-01", "2013-03-01")
    daily = bars(dates)
    spy = bars(dates, "SPY")
    monthly = bars(pd.date_range("2010-01-01", "2013-03-01", freq="MS"))
    frame = swing.swing_features(daily, monthly, spy)
    assert frame.date.max() == daily.date.max()
    assert frame.loc[frame.date.eq(frame.date.max()), "target_20"].isna().all()
    changed_daily, changed_spy, changed_monthly = daily.copy(), spy.copy(), monthly.copy()
    for changed in (changed_daily, changed_spy, changed_monthly):
        changed.loc[changed.date > "2013-01-15", ["open", "high", "low", "close"]] *= 2
    changed = swing.swing_features(changed_daily, changed_monthly, changed_spy)
    columns = ["date", "symbol"] + swing.SWING_FEATURES
    pd.testing.assert_frame_equal(frame.loc[frame.date <= "2013-01-15", columns].reset_index(drop=True),
                                  changed.loc[changed.date <= "2013-01-15", columns].reset_index(drop=True))


def test_original_research_feature_default_is_preserved_with_target_free_opt_in():
    daily = bars(pd.bdate_range("2010-01-01", "2013-03-01"))
    monthly = bars(pd.date_range("2010-01-01", "2013-03-01", freq="MS"))
    original = research.research_features(daily, monthly)
    target_free = research.research_features(daily, monthly, labeled_only=False)
    assert original.date.max() < daily.date.max()
    assert target_free.date.max() == daily.date.max()
    assert "target_up_1d" not in target_free
    pd.testing.assert_frame_equal(original.drop(columns=["target_date", "target_up_1d"]),
                                  target_free.iloc[:-1].reset_index(drop=True))


def test_annual_swing_fit_purges_full_horizon_and_predicts_unlabeled_latest_rows():
    dates = pd.bdate_range("2023-01-01", "2024-01-15")
    frame = bars(dates)[["date", "symbol"]]
    for i, column in enumerate(swing.SWING_FEATURES):
        frame[column] = np.sin(np.arange(len(frame)) / (i + 1))
    frame["target_20"] = (np.arange(len(frame)) % 2).astype(float)
    frame["target_date_20"] = frame.date + pd.offsets.BDay(21)
    unknown = frame.target_date_20 > frame.date.max()
    frame.loc[unknown, "target_20"] = np.nan
    frame.loc[unknown, "target_date_20"] = pd.NaT
    predictions, audits = swing.walk_forward_predict(frame, "rf_swing20", "2024-01-01", "2024-01-15")
    assert predictions.date.max() == dates[-1]
    assert np.isfinite(predictions.probability_up).all()
    assert audits[0]["last_target"] < audits[0]["first_prediction"]
    assert audits[0]["last_fit"] < "2023-12-15"
    changed = frame.copy()
    future_targets = changed.target_date_20 >= pd.Timestamp("2024-01-02")
    changed.loc[future_targets, "target_20"] = 1 - changed.loc[future_targets, "target_20"]
    repeated, _ = swing.walk_forward_predict(changed, "rf_swing20", "2024-01-01", "2024-01-15")
    pd.testing.assert_frame_equal(predictions, repeated)


def test_selection_keeps_risk_guard_and_does_not_promote_a_failed_target():
    reports = {name: {"annualized_return": .04, "max_drawdown": .08, "target_met": False}
               for name in swing.RECIPES}
    reports["rf_daily_v1"] = {"annualized_return": .10, "max_drawdown": .10, "target_met": False}
    reports["rf_swing20"] = {"annualized_return": .30, "max_drawdown": .15, "target_met": True}
    assert swing.choose_recipe(reports) == "rf_daily_v1"
    reports["logistic_swing20"] = {"annualized_return": .21, "max_drawdown": .101, "target_met": True}
    assert swing.choose_recipe(reports) == "logistic_swing20"


def test_higher_cost_sensitivity_changes_real_sizing_and_benchmark_costs():
    prices = bars(pd.bdate_range("2025-01-01", periods=5), opens=np.full(5, 100.))
    orders = pd.DataFrame({"date": pd.to_datetime(["2025-01-01"]), "symbol": "AAPL",
                           "action": "BUY", "allocation": 1.})
    curve, trades = research.simulate(prices, orders, "2025-01-01", "2025-01-07", fee_bps=25, slippage_bps=25)
    quantity = np.floor(10000 / (100 * 1.0025 * 1.0025) * 1e8) / 1e8
    assert trades.quantity.iloc[0] == quantity
    assert curve.total_value.iloc[-1] == pytest.approx(100000 - quantity * 100 * 1.0025 * 1.0025 + quantity * 100)
    spy = prices.assign(symbol=pd.Series("SPY", index=prices.index, dtype="string"))
    expensive = research.spy_curve(spy, curve.date, fee_bps=25, slippage_bps=25)
    normal = research.spy_curve(spy, curve.date)
    assert expensive.total_value.iloc[-1] < normal.total_value.iloc[-1]


@pytest.mark.parametrize("bad", [-1, float("nan"), True, 10000, 2.5])
def test_cost_sensitivity_rejects_invalid_assumptions(bad):
    prices = bars(pd.bdate_range("2025-01-01", periods=5))
    orders = pd.DataFrame(columns=["date", "symbol", "action", "allocation"])
    with pytest.raises(ValueError):
        research.simulate(prices, orders, "2025-01-01", "2025-01-07", fee_bps=bad)


def test_calendar_year_returns_include_the_gap_into_a_new_year():
    curve = pd.DataFrame({"date": pd.to_datetime(["2020-01-01", "2020-12-31", "2021-01-04", "2021-11-11"]),
                          "total_value": [100., 90., 99., 108.]})
    assert swing.calendar_returns(curve) == pytest.approx({"2020": -.10, "2021": .20})


def test_long_history_snapshot_requires_the_three_unchanged_sources(tmp_path):
    spec = importlib.util.spec_from_file_location("swing_runner", Path(__file__).parents[1] / "examples/swing_research.py")
    runner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runner)
    manifest = {"schema_version": 1, "protocol": "long-horizon-20261111-v1",
        "symbols": ["AAPL", "JNJ", "JPM", "MSFT", "XOM"], "history_start": "2010-01-01", "as_of": "2026-11-11",
        "adjustments": {"stocks.csv": "splits", "monthly.csv": "splits", "spy.csv": "all"}, "files": {}}
    for name in ("stocks.csv", "monthly.csv", "spy.csv"):
        bars(["2010-01-04", "2026-11-11"], "SPY" if name == "spy.csv" else "AAPL").to_csv(tmp_path / name, index=False)
        manifest["files"][name] = runner.sha256(tmp_path / name)
    metadata = tmp_path / "manifest.json"
    metadata.write_text(json.dumps(manifest))
    # The frozen study must reject an incomplete universe even when hashes match.
    with pytest.raises(ValueError):
        runner.load_snapshot(tmp_path)
    manifest["adjustments"]["spy.csv"] = "splits"
    metadata.write_text(json.dumps(manifest))
    with pytest.raises(ValueError):
        runner.load_snapshot(tmp_path)


def test_filtered_orders_confirm_exits_without_overriding_the_score_floor():
    dates = pd.bdate_range("2024-01-01", periods=7)
    predictions = pd.DataFrame({"date": dates, "symbol": "AAPL",
                                "probability_up": [.64, .70, .30, .30, .30, .30, .30]})
    features = predictions[["date", "symbol"]].assign(close_ma50=.1, close_ma200=-.1, momentum_20=-.1)
    persistent = swing.filtered_orders(predictions, features, "persistent5")
    assert persistent.action.tolist() == ["BUY", "SELL"]
    assert persistent.date.tolist() == [dates[1], dates[-1]]
    trend50 = swing.filtered_orders(predictions, features, "trend50")
    assert trend50.empty
    trend200 = swing.filtered_orders(predictions, features, "trend200")
    assert trend200.action.tolist() == ["BUY"] + ["SELL"] * 5
    assert dates[0] not in trend200.date.tolist()


def test_persistent_sell_confirmation_resets_at_symbol_boundaries():
    dates = pd.bdate_range("2024-01-01", periods=4)
    predictions = pd.concat([pd.DataFrame({"date": dates, "symbol": symbol, "probability_up": .30})
                              for symbol in ("AAPL", "MSFT")], ignore_index=True)
    features = predictions[["date", "symbol"]].assign(close_ma50=-.1, close_ma200=-.1, momentum_20=-.1)
    assert swing.filtered_orders(predictions, features, "persistent5").empty


def test_nonconvergent_logistic_candidate_fails_instead_of_entering_selection(monkeypatch):
    dates = pd.bdate_range("2023-01-01", "2024-01-15")
    frame = bars(dates)[["date", "symbol"]]
    for i, column in enumerate(swing.SWING_FEATURES):
        frame[column] = np.sin(np.arange(len(frame)) / (i + 1))
    frame["target_20"] = (np.arange(len(frame)) % 2).astype(float)
    frame["target_date_20"] = frame.date + pd.offsets.BDay(21)
    monkeypatch.setitem(swing.LOGISTIC_PARAMETERS, "max_iter", 1)
    with pytest.raises(ConvergenceWarning):
        swing.walk_forward_predict(frame, "logistic_swing20", "2024-01-01", "2024-01-15")


def test_exit_policy_selection_uses_the_same_return_and_drawdown_gate():
    reports = {"rf_daily_v1": {"annualized_return": .10, "max_drawdown": .10, "target_met": False},
               "persistent5": {"annualized_return": .25, "max_drawdown": .15, "target_met": True},
               "trend50": {"annualized_return": .15, "max_drawdown": .101, "target_met": False}}
    assert swing.choose_recipe(reports) == "trend50"
    reports["trend50"]["max_drawdown"] = .12
    assert swing.choose_recipe(reports) == "rf_daily_v1"


@pytest.mark.parametrize("horizon", [5, 63])
def test_calibration_purges_targets_at_both_boundaries_and_keeps_latest_inference(horizon):
    dates = pd.bdate_range("2020-01-01", "2024-01-15")
    frame = bars(dates)[["date", "symbol"]]
    for i, column in enumerate(swing.SWING_FEATURES):
        frame[column] = np.sin(np.arange(len(frame)) / (i + 1))
    label, target_date = f"target_{horizon}", f"target_date_{horizon}"
    frame[label] = (np.arange(len(frame)) % 2).astype(float)
    frame[target_date] = frame.date + pd.offsets.BDay(horizon + 1)
    unknown = frame[target_date] > frame.date.max()
    frame.loc[unknown, label] = np.nan
    frame.loc[unknown, target_date] = pd.NaT
    first, audits = swing.walk_forward_predict(frame, f"rf_calibrated{horizon}", "2024-01-01", "2024-01-15")
    audit = audits[0]
    assert audit["base_last_target"] < audit["calibration_start"]
    assert audit["last_target"] < audit["first_prediction"]
    assert audit["calibration_rows"] > (200 if horizon == 5 else 180)
    assert first.date.max() == dates[-1]
    changed = frame.copy()
    excluded = ((changed.date < audit["calibration_start"]) & (changed[target_date] >= audit["calibration_start"]))
    excluded |= changed[target_date] >= audit["first_prediction"]
    assert excluded.any()
    changed.loc[excluded, label] = 1 - changed.loc[excluded, label]
    second, _ = swing.walk_forward_predict(changed, f"rf_calibrated{horizon}", "2024-01-01", "2024-01-15")
    pd.testing.assert_frame_equal(first, second)


def test_calibration_rejects_a_single_class_in_the_separate_calibration_year():
    dates = pd.bdate_range("2020-01-01", "2024-01-15")
    frame = bars(dates)[["date", "symbol"]]
    for i, column in enumerate(swing.SWING_FEATURES):
        frame[column] = np.sin(np.arange(len(frame)) / (i + 1))
    frame["target_5"] = (np.arange(len(frame)) % 2).astype(float)
    frame["target_date_5"] = frame.date + pd.offsets.BDay(6)
    frame.loc[frame.date >= "2023-01-01", "target_5"] = 1
    with pytest.raises(ValueError, match="calibration"):
        swing.walk_forward_predict(frame, "rf_calibrated5", "2024-01-01", "2024-01-15")


def test_quarterly_target_exits_sixty_three_sessions_after_the_entry_open():
    dates = pd.bdate_range("2024-01-01", periods=100)
    market = bars(dates, opens=100 + np.arange(100))
    labels = swing.add_targets(market[["date", "symbol"]], market, bars(dates, "SPY"))
    assert labels.target_date_63.iloc[0] == dates[64]
    assert labels.forward_return_63.iloc[0] == pytest.approx(164 / 101 - 1)
    assert labels.target_63.iloc[0] == 1
    assert labels.target_63.iloc[-64:].isna().all()
