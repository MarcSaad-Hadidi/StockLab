"""Fair, causal price-return benchmark mechanics on literal offline sessions."""

from copy import deepcopy
from dataclasses import asdict, replace
from fractions import Fraction
from importlib import import_module

import numpy as np
import pandas as pd
import pytest

from test_backtesting import config, decisions, market, run, simple_prices, simple_decisions


def compare(backtest, prices):
    return import_module("stocklab_ml.backtesting").compare_to_buy_and_hold(backtest, prices)


def hold_backtest(prices, capital=100000):
    start = prices.dataframe.date.min().date().isoformat()
    rows = [(start, symbol, .5) for symbol in prices.report.symbols
            if ((prices.dataframe.symbol == symbol) & (prices.dataframe.date == start)).any()]
    return run(decisions(rows), prices, config=config(initial_capital=capital))


def test_single_symbol_next_open_and_eod_price_return():
    prices = simple_prices()
    ai = run(simple_decisions(), prices)
    result = compare(ai, prices)
    row = result.benchmark_allocations.iloc[0]
    assert row.entry_date == pd.Timestamp("2024-01-08")
    assert row.entry_price == 100 and row.quantity == 1000
    assert row.invested_amount == 100000 and row.residual_cash == 0
    assert row.final_mark_date == pd.Timestamp("2024-01-10")
    assert row.final_mark_price == 120 and row.component_final_value == 120000
    assert result.benchmark_equity_curve.total_value.tolist() == [100000, 110000, 115000, 120000]
    report = result.benchmark_report
    assert report.final_cash == 0 and report.final_positions_value == report.final_value == 120000
    assert report.total_return == pytest.approx(.2)
    assert report.dividends_included is False and report.commission == report.slippage == 0
    comparison = result.comparison_report
    assert comparison.ai_final_value == ai.report.final_value == 102000
    assert comparison.ai_total_return == ai.report.total_return
    assert comparison.ai_max_drawdown == ai.report.max_drawdown
    assert comparison.final_value_delta == -18000
    assert comparison.return_delta == pytest.approx(-.18)
    assert not any("winner" in name or "promot" in name for name in asdict(comparison))


@pytest.mark.parametrize("capital", [100000, 250000])
def test_equal_weight_shared_capital_includes_never_traded_symbols(capital):
    prices = market([(day, symbol, price, close) for day in ["2024-01-05", "2024-01-08"]
                     for symbol, price, close in [("AAPL", 100, 200), ("MSFT", 200, 200)]])
    ai = run(decisions([("2024-01-05", "AAPL", .8), ("2024-01-05", "MSFT", .5)]),
             prices, config=config(initial_capital=capital))
    result = compare(ai, prices)
    allocations = result.benchmark_allocations
    assert ai.trades.symbol.tolist() == ["AAPL"]
    assert allocations.symbol.tolist() == ["AAPL", "MSFT"]
    assert allocations.target_allocation.tolist() == [capital / 2] * 2
    assert allocations.quantity.tolist() == [capital / 200, capital / 400]
    assert result.benchmark_report.initial_capital == capital
    assert result.benchmark_report.final_value == capital * 1.5
    assert result.comparison_report.same_initial_capital is True
    assert result.comparison_report.same_period is True
    assert result.comparison_report.same_symbols is True
    assert result.benchmark_report.symbols == ai.report.symbols
    assert result.benchmark_report.start_date == ai.report.start_date
    assert result.benchmark_report.end_date == ai.report.end_date
    pd.testing.assert_series_equal(result.benchmark_equity_curve.date, ai.equity_curve.date)


@pytest.mark.parametrize("capital,price,symbols", [(1000, 333, ["AAPL"]),
    (100000, 333, ["AAPL", "MSFT", "NVDA"]), (1e-7, 100, ["AAPL"]),
    (1e18, .001, ["AAPL", "MSFT", "NVDA"])])
def test_exact_tranches_fractional_floor_and_residual_cash(capital, price, symbols):
    prices = market([(day, symbol, price, price) for symbol in symbols
                     for day in ["2024-01-05", "2024-01-08"]])
    result = compare(hold_backtest(prices, capital), prices)
    budget = Fraction(str(capital)) / len(symbols)
    for row in result.benchmark_allocations.itertuples():
        amount = Fraction(str(row.quantity)) * Fraction(str(price))
        assert amount <= budget
        assert row.residual_cash >= 0
        assert round(row.quantity, 8) == row.quantity
    assert result.benchmark_equity_curve.cash.ge(0).all()
    assert result.benchmark_report.final_value == capital
    assert result.benchmark_report.total_return == 0
    if capital == 1000:
        assert result.benchmark_allocations.quantity.iloc[0] == 3.003003
        assert result.benchmark_report.final_cash == pytest.approx(.000001)


def test_no_next_session_retains_tranche_without_redistribution():
    prices = market([("2024-01-05", "AAPL", 100, 100), ("2024-01-08", "AAPL", 100, 200),
                     ("2024-01-05", "MSFT", 500, 500)])
    result = compare(hold_backtest(prices), prices)
    row = result.benchmark_allocations.iloc[1]
    assert row.symbol == "MSFT" and row.target_allocation == row.residual_cash == 50000
    assert pd.isna(row.entry_date) and pd.isna(row.entry_price)
    assert pd.isna(row.final_mark_date) and pd.isna(row.final_mark_price)
    assert row.quantity == row.invested_amount == row.final_market_value == row.component_return == 0
    assert row.component_final_value == 50000
    assert result.benchmark_allocations.quantity.iloc[0] == 500
    assert result.benchmark_report.final_cash == 50000
    assert result.benchmark_report.final_value == 150000
    assert result.benchmark_report.uninvested_symbols == ("MSFT",)


def test_one_day_period_stays_entirely_cash():
    prices = market([("2024-01-05", "AAPL", 1, 1000)])
    result = compare(hold_backtest(prices), prices)
    assert result.benchmark_report.final_cash == result.benchmark_report.final_value == 100000
    assert result.benchmark_positions_history.empty
    assert result.benchmark_report.invested_symbols == ()


def test_asynchronous_marks_future_leakage_and_no_rebalancing():
    rows = [("2024-01-05", "AAPL", 50, 100), ("2024-01-08", "AAPL", 100, 200),
            ("2024-01-10", "AAPL", 200, 300), ("2024-01-05", "MSFT", 200, 200),
            ("2024-01-09", "MSFT", 200, 200)]
    prices = market(rows)
    ai = hold_backtest(prices)
    result = compare(ai, prices)
    assert result.benchmark_equity_curve.total_value.tolist() == [100000, 150000, 150000, 200000]
    marks = result.benchmark_positions_history
    aapl = marks[marks.symbol.eq("AAPL")]
    assert aapl.quantity.tolist() == [500] * 3
    assert aapl.mark_price.tolist() == [200, 200, 300]
    assert aapl.mark_date.tolist() == list(pd.to_datetime(["2024-01-08", "2024-01-08", "2024-01-10"]))
    changed = market([row if row[0] != "2024-01-10" else (*row[:3], 3000) for row in rows])
    future = compare(ai, changed)
    pd.testing.assert_frame_equal(result.benchmark_equity_curve.iloc[:3],
                                  future.benchmark_equity_curve.iloc[:3], check_exact=True)
    assert future.benchmark_report.final_value == 1550000


def test_entry_uses_only_open_and_ignores_rows_outside_period_and_other_symbols():
    prices = simple_prices()
    ai = hold_backtest(prices)
    result = compare(ai, prices)
    changed = market([("2024-01-01", "AAPL", 1, 1), ("2024-01-05", "AAPL", 50, 5000),
        ("2024-01-08", "AAPL", 100, 4000), ("2024-01-09", "AAPL", 110, 115),
        ("2024-01-10", "AAPL", 120, 120), ("2024-01-11", "AAPL", 9000, 9000),
        ("2024-01-08", "NVDA", 1, 100000)])
    actual = compare(ai, changed)
    pd.testing.assert_frame_equal(result.benchmark_allocations, actual.benchmark_allocations, check_exact=True)
    assert actual.benchmark_report.final_value == 120000
    assert actual.benchmark_equity_curve.total_value.iloc[1] == 4000000
    assert actual.benchmark_report.symbols == ("AAPL",)


@pytest.mark.parametrize("last,delta", [(120, -.2), (80, .2), (100, 0)])
def test_positive_negative_and_equal_return_delta(last, delta):
    prices = market([("2024-01-05", "AAPL", 50, 50), ("2024-01-08", "AAPL", 100, last)])
    result = compare(hold_backtest(prices), prices)
    assert result.comparison_report.return_delta == pytest.approx(delta)
    assert result.comparison_report.final_value_delta == pytest.approx(delta * 100000)


def test_drawdown_and_final_asof_mark_without_liquidation():
    prices = market([("2024-01-05", "AAPL", 10, 10), ("2024-01-08", "AAPL", 100, 120),
        ("2024-01-09", "AAPL", 90, 90), ("2024-01-10", "AAPL", 110, 110),
        ("2024-01-05", "MSFT", 100, 100), ("2024-01-11", "MSFT", 100, 100)])
    ai = run(decisions([("2024-01-05", "AAPL", .5)]), prices)
    result = compare(ai, prices)
    assert result.benchmark_equity_curve.total_value.tolist() == [100000, 120000, 90000, 110000]
    assert result.benchmark_report.max_drawdown == .25
    assert result.benchmark_allocations.final_mark_date.iloc[0] == pd.Timestamp("2024-01-10")
    # Extend only an already-present AI date using another report-universe symbol.
    ai = hold_backtest(prices)
    result = compare(ai, prices)
    assert result.benchmark_allocations.final_mark_date.iloc[0] == pd.Timestamp("2024-01-10")
    assert result.benchmark_positions_history.iloc[-2].mark_date <= pd.Timestamp(ai.report.end_date)


def test_independence_from_ai_signals_and_risk_limits_and_input_immutability():
    prices = simple_prices()
    ai = run(simple_decisions(), prices)
    before = deepcopy((ai, prices))
    result = compare(ai, prices)
    again = compare(ai, prices)
    for name in ["benchmark_allocations", "benchmark_equity_curve", "benchmark_positions_history"]:
        pd.testing.assert_frame_equal(getattr(result, name), getattr(again, name), check_exact=True)
    assert result.benchmark_report == again.benchmark_report
    assert result.comparison_report == again.comparison_report
    for name in ["trades", "equity_curve", "positions_history"]:
        pd.testing.assert_frame_equal(getattr(ai, name), getattr(before[0], name), check_exact=True)
    assert ai.report == before[0].report and prices.report == before[1].report
    pd.testing.assert_frame_equal(prices.dataframe, before[1].dataframe, check_exact=True)
    hold = run(decisions([("2024-01-05", "AAPL", .5)]), prices,
               config=config(max_trade_allocation=.01, max_symbol_exposure=.01))
    other = compare(hold, prices)
    pd.testing.assert_frame_equal(result.benchmark_allocations, other.benchmark_allocations, check_exact=True)
    pd.testing.assert_frame_equal(result.benchmark_equity_curve, other.benchmark_equity_curve, check_exact=True)


@pytest.mark.parametrize("bad", ["raw_ai", "ai_report", "ai_equity", "raw_market", "market_report",
    "missing_symbol", "duplicate", "nan", "infinite", "bad_price", "bad_schema", "missing_date", "wrong_open", "wrong_close"])
def test_invalid_or_mismatched_sources_are_rejected(bad):
    prices = simple_prices()
    ai = run(simple_decisions(), prices)
    if bad == "raw_ai": ai = {}
    elif bad == "ai_report": ai = replace(ai, report=replace(ai.report, total_return=.5))
    elif bad == "ai_equity": ai.equity_curve.loc[1, "cash"] = 1
    elif bad == "raw_market": prices = prices.dataframe
    elif bad == "market_report": prices = replace(prices, report=replace(prices.report, output_rows=1))
    elif bad == "missing_symbol": prices = market([("2024-01-05", "MSFT", 100, 100)])
    elif bad == "duplicate": prices = replace(prices, dataframe=pd.concat([prices.dataframe] * 2, ignore_index=True))
    elif bad in ["nan", "infinite", "bad_price"]:
        prices.dataframe.loc[1, "open"] = {"nan": np.nan, "infinite": np.inf, "bad_price": 0}[bad]
    elif bad == "bad_schema": prices = replace(prices, dataframe=prices.dataframe.drop(columns="volume"))
    elif bad == "missing_date": prices = market([("2024-01-05", "AAPL", 50, 90), ("2024-01-10", "AAPL", 120, 120)])
    elif bad == "wrong_open": prices = market([("2024-01-05", "AAPL", 50, 90), ("2024-01-08", "AAPL", 101, 110),
        ("2024-01-09", "AAPL", 110, 115), ("2024-01-10", "AAPL", 120, 120)])
    elif bad == "wrong_close": prices = market([("2024-01-05", "AAPL", 50, 90), ("2024-01-08", "AAPL", 100, 111),
        ("2024-01-09", "AAPL", 110, 115), ("2024-01-10", "AAPL", 120, 120)])
    with pytest.raises(import_module("stocklab_ml.backtesting").BacktestError):
        compare(ai, prices)


def test_actual_logistic_and_random_forest_backtests_need_no_models_for_comparison(monkeypatch):
    from sklearn.ensemble import RandomForestClassifier
    from sklearn.linear_model import LogisticRegression
    from stocklab_ml.data.cleaning import clean_historical_dataset
    from stocklab_ml.modeling import train_random_forest
    from stocklab_ml.signals import add_confidence_scores, generate_trading_signals
    from test_model_dataset import feature_frame

    features = feature_frame()
    forest = train_random_forest(features)
    prices = clean_historical_dataset(features.iloc[:, :7], as_of_date="2100-01-01")
    backtests = [run(add_confidence_scores(generate_trading_signals(model.predictions,
        model_name=model.report.model_name)), prices) for model in [forest.baseline_result, forest]]
    def forbidden(*args, **kwargs):
        raise AssertionError("Comparison must consume existing backtests without inference or training")
    for estimator in [LogisticRegression, RandomForestClassifier]:
        for method in ["fit", "predict", "predict_proba"]:
            monkeypatch.setattr(estimator, method, forbidden)
    monkeypatch.setattr(import_module("stocklab_ml.backtesting"), "run_backtest", forbidden)
    for ai in backtests:
        result = compare(ai, prices)
        assert result.comparison_report.model_name == ai.report.model_name
        assert result.comparison_report.ai_total_return == ai.report.total_return


def test_missing_traded_symbol_sessions_fail_with_domain_error():
    original = market([(day, symbol, 100, 100) for symbol in ["AAPL", "MSFT"]
                       for day in ["2024-01-05", "2024-01-08"]])
    ai = run(decisions([("2024-01-05", "AAPL", .8), ("2024-01-05", "MSFT", .5)]), original)
    changed = market([("2024-01-01", "AAPL", 100, 100), ("2024-01-05", "MSFT", 100, 100),
                      ("2024-01-08", "MSFT", 100, 100)])
    with pytest.raises(import_module("stocklab_ml.backtesting").BacktestError):
        compare(ai, changed)
