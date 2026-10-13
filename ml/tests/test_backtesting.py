"""Offline portfolio mechanics, input boundaries, and causal execution timing."""

from dataclasses import asdict, replace
from importlib import import_module

import numpy as np
import pandas as pd
import pytest

from stocklab_ml.data.cleaning import clean_historical_dataset
from stocklab_ml.signals import add_confidence_scores, generate_trading_signals


def market(rows):
    """Literal (date, symbol, open, close) observations; no invented sessions."""
    frame = pd.DataFrame(rows, columns=["date", "symbol", "open", "close"])
    frame["high"] = frame[["open", "close"]].max(axis=1)
    frame["low"] = frame[["open", "close"]].min(axis=1)
    frame["volume"] = 100
    return clean_historical_dataset(frame, as_of_date="2100-01-01")


def decisions(rows, model="synthetic", **labels):
    frame = pd.DataFrame(rows, columns=["date", "symbol", "probability_up"])
    frame["date"] = pd.to_datetime(frame.date).astype("datetime64[ns]")
    frame["predicted_class"] = (frame.probability_up >= 0.5).astype("int64")
    return add_confidence_scores(generate_trading_signals(frame.assign(**labels), model_name=model))


def config(**kwargs):
    return import_module("stocklab_ml.backtesting").BacktestConfig(**kwargs)


def run(source, prices, **kwargs):
    return import_module("stocklab_ml.backtesting").run_backtest(source, prices, **kwargs)


def simple_prices(sell_open=120):
    return market([
        ("2024-01-05", "AAPL", 50, 90),
        ("2024-01-08", "AAPL", 100, 110),
        ("2024-01-09", "AAPL", 110, 115),
        ("2024-01-10", "AAPL", sell_open, sell_open),
    ])


def simple_decisions():
    return decisions([("2024-01-05", "AAPL", .8), ("2024-01-08", "AAPL", .5),
                      ("2024-01-09", "AAPL", .2)])


@pytest.mark.parametrize("sell_open,pnl,final,win", [(120, 2000, 102000, 1),
                                                      (80, -2000, 98000, 0),
                                                      (100, 0, 100000, 0)])
def test_next_open_full_close_profit_loss_and_flat(sell_open, pnl, final, win):
    result = run(simple_decisions(), simple_prices(sell_open))
    assert result.trades.signal.tolist() == ["BUY", "SELL"]
    assert result.trades.execution_price.tolist() == [100, sell_open]
    assert result.trades.quantity.tolist() == [100, 100]
    assert result.trades.execution_date.tolist() == list(pd.to_datetime(["2024-01-08", "2024-01-10"]))
    assert (result.trades.execution_date > result.trades.decision_date).all()
    assert np.isnan(result.trades.realized_pnl.iloc[0])
    assert result.trades.realized_pnl.iloc[1] == pnl
    assert result.trades.cash_after.tolist() == [90000, final]
    assert result.trades.position_quantity_after.tolist() == [100, 0]
    assert result.report.final_value == result.report.final_cash == final
    assert result.report.final_positions_value == 0
    assert result.report.total_return == pytest.approx(final / 100000 - 1)
    assert result.report.win_rate == win
    assert result.report.open_positions_final == 0
    assert result.report.hold_decisions == 1
    assert result.equity_curve.total_value.iloc[0] == 100000
    assert not result.positions_history.date.eq(pd.Timestamp("2024-01-10")).any()


def test_hold_never_trades_even_with_confidence_one():
    source = decisions([("2024-01-05", "AAPL", .5), ("2024-01-10", "AAPL", .5)])
    result = run(source, simple_prices())
    assert result.trades.empty and result.positions_history.empty
    assert result.equity_curve.total_value.tolist() == [100000] * 4
    assert result.report.total_return == result.report.win_rate == result.report.max_drawdown == 0
    assert result.report.hold_decisions == 2 and result.report.no_next_session == 0


@pytest.mark.parametrize("probability,count,skips", [(.65, 0, 1), (.70, 1, 0)])
def test_confidence_filter_inclusive_boundary(probability, count, skips):
    result = run(decisions([("2024-01-05", "AAPL", probability)]), simple_prices())
    assert len(result.trades) == count
    assert result.report.skipped_low_confidence == skips
    assert result.report.decision_count == 1


def test_low_confidence_sell_leaves_existing_position_open():
    source = decisions([("2024-01-05", "AAPL", .8), ("2024-01-09", "AAPL", .35)])
    result = run(source, simple_prices())
    assert result.trades.signal.tolist() == ["BUY"]
    assert result.report.skipped_low_confidence == 1
    assert result.report.open_positions_final == 1


@pytest.mark.parametrize("next_date", ["2024-01-08", "2024-01-09"])
def test_weekend_and_missing_session_use_next_observation(next_date):
    prices = market([("2024-01-05", "AAPL", 99, 90), (next_date, "AAPL", 123, 130)])
    result = run(decisions([("2024-01-05", "AAPL", .8)]), prices)
    assert result.trades.execution_date.tolist() == [pd.Timestamp(next_date)]
    assert result.trades.execution_price.tolist() == [123]


@pytest.mark.parametrize("probability", [.8, .2])
def test_last_symbol_session_has_no_execution(probability):
    prices = market([("2024-01-05", "AAPL", 100, 500),
                     ("2024-01-05", "MSFT", 50, 50), ("2024-01-08", "MSFT", 60, 60)])
    source = decisions([("2024-01-05", "AAPL", probability), ("2024-01-05", "MSFT", .5)])
    result = run(source, prices)
    assert result.trades.empty and result.report.no_next_session == 1
    assert result.report.final_value == 100000


def test_same_day_close_and_future_prices_cannot_change_earlier_trade():
    source = simple_decisions()
    expected = run(source, simple_prices())
    changed = market([("2024-01-05", "AAPL", 50, 5000),
                      ("2024-01-08", "AAPL", 100, 4000),
                      ("2024-01-09", "AAPL", 110, 3000),
                      ("2024-01-10", "AAPL", 500, 6000)])
    result = run(source, changed)
    pd.testing.assert_frame_equal(result.trades.iloc[:1], expected.trades.iloc[:1], check_exact=True)
    assert result.equity_curve.total_value.iloc[1] == 490000


def test_additional_buy_uses_todays_open_for_every_held_symbol():
    source = decisions([("2024-01-05", "AAPL", .8), ("2024-01-08", "AAPL", .8)])
    def prices(close):
        return market([("2024-01-05", "AAPL", 100, 100), ("2024-01-08", "AAPL", 100, 100),
                       ("2024-01-09", "AAPL", 120, close)])
    cfg = config(initial_capital=10000, max_symbol_exposure=1)
    normal = run(source, prices(120), config=cfg)
    future = run(source, prices(12000), config=cfg)
    pd.testing.assert_frame_equal(normal.trades, future.trades, check_exact=True)
    # Equity at open = 9000 cash + 10*120 = 10200; allocation = 1020.
    assert normal.trades.quantity.tolist() == [10, 8.5]
    assert normal.trades.average_cost_after.iloc[1] == pytest.approx(2020 / 18.5)


def test_weighted_average_cost_and_sell_realized_pnl():
    source = decisions([("2024-01-05", "AAPL", .8), ("2024-01-08", "AAPL", .8),
                        ("2024-01-09", "AAPL", .2)])
    prices = market([("2024-01-05", "AAPL", 100, 100), ("2024-01-08", "AAPL", 100, 100),
                     ("2024-01-09", "AAPL", 120, 120), ("2024-01-10", "AAPL", 130, 130)])
    result = run(source, prices, config=config(initial_capital=11800, max_symbol_exposure=1))
    assert result.trades.quantity.tolist() == [11.8, 10.03, 21.83]
    assert result.trades.average_cost_after.iloc[1] == pytest.approx(2383.6 / 21.83)
    assert result.trades.realized_pnl.iloc[2] == pytest.approx(454.3)
    assert result.positions_history.average_cost.iloc[-1] == result.trades.average_cost_after.iloc[1]


def test_shared_cash_symbol_order_and_cash_exhaustion():
    prices = market([(day, symbol, 100, 100) for symbol in ["AAPL", "MSFT", "NVDA"]
                     for day in ["2024-01-05", "2024-01-08"]])
    source = decisions([("2024-01-05", symbol, .8) for symbol in ["NVDA", "MSFT", "AAPL"]])
    cfg = config(initial_capital=1000, max_symbol_exposure=1, max_trade_allocation=.6)
    first = run(source, prices, config=cfg)
    assert first.trades.symbol.tolist() == ["AAPL", "MSFT"]
    assert first.trades.total_amount.tolist() == [600, 400]
    assert first.trades.cash_after.tolist() == [400, 0]
    assert first.report.skipped_buying_power == 1
    assert first.report.final_positions_value == first.report.final_value == 1000
    source = replace(source, signals=source.signals.iloc[::-1].set_axis([7, 7, 2]))
    second = run(source, prices, config=cfg)
    for name in ["trades", "equity_curve", "positions_history"]:
        pd.testing.assert_frame_equal(getattr(first, name), getattr(second, name), check_exact=True)
    assert first.report == second.report


def test_max_positions_does_not_block_existing_symbol_buy():
    rows = [(day, sym, 100, 100) for sym in ["AAPL", "MSFT", "NVDA"]
            for day in ["2024-01-05", "2024-01-08", "2024-01-09"]]
    source = decisions([("2024-01-05", sym, .8) for sym in ["AAPL", "MSFT", "NVDA"]]
                       + [("2024-01-08", "AAPL", .8)])
    result = run(source, market(rows), config=config(max_open_positions=2))
    assert result.trades.symbol.tolist() == ["AAPL", "MSFT", "AAPL"]
    assert result.report.skipped_max_positions == 1
    assert result.report.open_positions_final == 2


def test_exposure_remaining_and_allocation_limit():
    prices = market([("2024-01-05", "AAPL", 100, 100), ("2024-01-08", "AAPL", 100, 100),
                     ("2024-01-09", "AAPL", 150, 150), ("2024-01-10", "AAPL", 150, 150)])
    source = decisions([(day, "AAPL", .8) for day in ["2024-01-05", "2024-01-08", "2024-01-09"]])
    result = run(source, prices)
    assert result.trades.total_amount.tolist() == pytest.approx([10000, 6000])
    assert result.report.skipped_exposure == 1
    assert result.trades.quantity.tolist() == [100, 40]


def test_no_short_for_sell_without_position():
    result = run(decisions([("2024-01-05", "AAPL", .2)]), simple_prices())
    assert result.trades.empty and result.positions_history.empty
    assert result.report.skipped_no_position == 1 and result.report.final_value == 100000


@pytest.mark.parametrize("precision,quantity", [(8, 3.003003), (2, 3.00), (0, 3.0)])
def test_fractional_quantity_is_floored_and_never_exceeds_cash(precision, quantity):
    prices = market([("2024-01-05", "AAPL", 333, 333), ("2024-01-08", "AAPL", 333, 333)])
    result = run(decisions([("2024-01-05", "AAPL", .8)]), prices,
                 config=config(initial_capital=1000, max_symbol_exposure=1,
                               max_trade_allocation=1, quantity_decimals=precision))
    assert result.trades.quantity.tolist() == [quantity]
    assert result.trades.total_amount.iloc[0] <= 1000
    assert result.report.final_cash >= 0


def test_too_small_quantity_is_audited():
    result = run(decisions([("2024-01-05", "AAPL", .8)]), simple_prices(),
                 config=config(initial_capital=.00000001))
    assert result.trades.empty and result.report.skipped_buying_power == 1


def test_asynchronous_calendars_open_sizing_and_eod_asof_marks():
    prices = market([("2024-01-05", "AAPL", 100, 100), ("2024-01-08", "AAPL", 100, 200),
                     ("2024-01-10", "AAPL", 1000, 5000),
                     ("2024-01-05", "MSFT", 100, 100), ("2024-01-08", "MSFT", 100, 100),
                     ("2024-01-09", "MSFT", 100, 100)])
    source = decisions([("2024-01-05", "AAPL", .8), ("2024-01-08", "MSFT", .8)])
    result = run(source, prices)
    # Jan 9: AAPL has no open, previous close is 200, total at open is 110000.
    assert result.trades.total_amount.tolist() == [10000, 11000]
    aapl = result.positions_history[result.positions_history.symbol.eq("AAPL")]
    assert aapl.mark_price.tolist() == [200, 200, 5000]
    assert aapl.opening_mark_price.tolist() == [100, 200, 1000]
    assert result.equity_curve.total_value.tolist() == [100000, 110000, 110000, 590000]
    assert result.report.final_cash == 79000
    assert result.report.final_positions_value == 511000


def test_drawdown_uses_initial_capital_and_running_portfolio_peak():
    prices = market([("2024-01-05", "AAPL", 100, 100), ("2024-01-08", "AAPL", 100, 120),
                     ("2024-01-09", "AAPL", 90, 90), ("2024-01-10", "AAPL", 110, 110)])
    source = decisions([("2024-01-05", "AAPL", .8)])
    cfg = config(max_trade_allocation=1, max_symbol_exposure=1)
    result = run(source, prices, config=cfg)
    assert result.equity_curve.total_value.tolist() == [100000, 120000, 90000, 110000]
    assert result.equity_curve.drawdown.tolist() == pytest.approx([0, 0, .25, 1 / 12])
    assert result.report.max_drawdown == .25
    prices = market([("2024-01-05", "AAPL", 100, 100), ("2024-01-08", "AAPL", 100, 80)])
    assert run(source, prices, config=cfg).report.max_drawdown == .2


def test_final_open_position_is_marked_without_forced_sell():
    result = run(decisions([("2024-01-05", "AAPL", .8)]), simple_prices())
    assert result.trades.signal.tolist() == ["BUY"]
    assert result.report.final_cash == 90000 and result.report.final_positions_value == 12000
    assert result.report.final_value == 102000 and result.report.win_rate == 0
    assert result.positions_history.market_value.iloc[-1] == 12000
    assert result.positions_history.unrealized_pnl.iloc[-1] == 2000


def test_win_rate_counts_only_profitable_closed_positions():
    days = pd.date_range("2024-01-01", periods=7, freq="B").strftime("%Y-%m-%d").tolist()
    prices = market([(day, "AAPL", price, price) for day, price in zip(days, [100, 100, 110, 100, 95, 100, 100])])
    source = decisions([(day, "AAPL", p) for day, p in zip(days[:-1], [.8, .2, .8, .2, .8, .2])])
    result = run(source, prices)
    assert result.report.winning_closed_trades == 1
    assert result.report.losing_or_flat_closed_trades == 2
    assert result.report.win_rate == 1 / 3


def test_inputs_and_reports_are_not_mutated_or_shared():
    source, prices = simple_decisions(), simple_prices()
    source_before, prices_before = source.signals.copy(deep=True), prices.dataframe.copy(deep=True)
    reports_before = (asdict(source.report), asdict(prices.report))
    result = run(source, prices)
    pd.testing.assert_frame_equal(source.signals, source_before, check_exact=True)
    pd.testing.assert_frame_equal(prices.dataframe, prices_before, check_exact=True)
    assert reports_before == (asdict(source.report), asdict(prices.report))
    with pytest.raises(AttributeError):
        result.report.final_value = 0
    with pytest.raises(AttributeError):
        result.report.config.initial_capital = 0


@pytest.mark.parametrize("field,value", [
    ("initial_capital", 0), ("initial_capital", -1), ("initial_capital", True),
    ("initial_capital", "100"), ("initial_capital", np.inf), ("initial_capital", np.nan),
    ("initial_capital", 10 ** 400),
    ("minimum_confidence", -.1), ("minimum_confidence", 1.1), ("minimum_confidence", True),
    ("max_symbol_exposure", 0), ("max_symbol_exposure", 1.1), ("max_symbol_exposure", np.nan),
    ("max_trade_allocation", 0), ("max_trade_allocation", np.inf), ("max_trade_allocation", True),
    ("max_open_positions", 0), ("max_open_positions", 1.5), ("max_open_positions", True),
    ("allow_short_selling", True), ("allow_short_selling", 0),
    ("quantity_decimals", -1), ("quantity_decimals", 9), ("quantity_decimals", True),
])
def test_invalid_config_fails_clearly(field, value):
    api = import_module("stocklab_ml.backtesting")
    with pytest.raises(api.BacktestError, match=field):
        config(**{field: value})


@pytest.mark.parametrize("bad", ["raw_decisions", "raw_prices", "config", "signal", "confidence", "model",
                                "duplicate", "dtype", "date", "missing_session", "missing_symbol",
                                "market_order", "market_price", "market_report", "market_empty"])
def test_input_contract_tampering_rejected(bad):
    api = import_module("stocklab_ml.backtesting")
    source, prices = simple_decisions(), simple_prices()
    kwargs = {}
    if bad == "raw_decisions": source = source.signals
    elif bad == "raw_prices": prices = prices.dataframe
    elif bad == "config": kwargs["config"] = {}
    elif bad == "signal": source.signals.loc[0, "signal"] = "HOLD"
    elif bad == "confidence": source.signals.loc[0, "confidence"] = .99
    elif bad == "model": source.signals.loc[0, "model_name"] = "other"
    elif bad == "duplicate": source = replace(source, signals=pd.concat([source.signals, source.signals.iloc[:1]]))
    elif bad == "dtype": source = replace(source, signals=source.signals.astype({"confidence": "float32"}))
    elif bad == "date": source.signals.loc[0, "date"] += pd.Timedelta(hours=1)
    elif bad == "missing_session":
        source = decisions([("2024-01-06", "AAPL", .8)])
    elif bad == "missing_symbol": source = decisions([("2024-01-05", "NVDA", .8)])
    elif bad == "market_order": prices = replace(prices, dataframe=prices.dataframe.iloc[::-1].reset_index(drop=True))
    elif bad == "market_price": prices.dataframe.loc[0, "open"] = -1
    elif bad == "market_report": prices = replace(prices, report=replace(prices.report, output_rows=1))
    elif bad == "market_empty": prices = replace(prices, dataframe=prices.dataframe.iloc[:0])
    with pytest.raises(api.BacktestError):
        run(source, prices, **kwargs)


def test_historical_labels_and_audit_classes_do_not_drive_trades():
    rows = [("2024-01-05", "AAPL", .8), ("2024-01-09", "AAPL", .2)]
    expected = run(decisions(rows), simple_prices())
    for label in [0, 1, object()]:
        source = decisions(rows, actual_class=label, target_date=object())
        source.signals["predicted_class"] = 0
        actual = run(source, simple_prices())
        pd.testing.assert_frame_equal(actual.trades, expected.trades, check_exact=True)
        assert actual.report == expected.report


def test_real_logistic_and_random_forest_batches_need_no_models_during_backtest(monkeypatch):
    from sklearn.ensemble import RandomForestClassifier
    from sklearn.linear_model import LogisticRegression
    from stocklab_ml.modeling import predict_direction, predict_random_forest_direction, train_random_forest
    from stocklab_ml.signals import confidence, generator
    from test_model_dataset import feature_frame

    features = feature_frame()
    forest = train_random_forest(features)
    logistic = forest.baseline_result
    batches = []
    prices = clean_historical_dataset(features.iloc[:, :7], as_of_date="2100-01-01")
    for result, inference in [(logistic, predict_direction(logistic.pipeline, features)),
                              (forest, predict_random_forest_direction(forest.model, features))]:
        for frame in [result.predictions, inference]:
            batches.append(add_confidence_scores(generate_trading_signals(frame, model_name=result.report.model_name)))

    def forbidden(*args, **kwargs):
        raise AssertionError("Backtesting cannot train, infer, or regenerate decisions")

    for estimator in [LogisticRegression, RandomForestClassifier]:
        for method in ["fit", "predict", "predict_proba"]:
            monkeypatch.setattr(estimator, method, forbidden)
    monkeypatch.setattr(generator, "generate_trading_signals", forbidden)
    monkeypatch.setattr(confidence, "add_confidence_scores", forbidden)
    for source in batches:
        result = run(source, prices)
        assert result.report.model_name == source.report.model_name
        assert result.report.decision_count == len(source.signals)
        assert (result.trades.execution_date > result.trades.decision_date).all()


def test_other_symbols_open_influences_sizing_but_its_same_day_close_does_not():
    source = decisions([("2024-01-05", "AAPL", .8), ("2024-01-08", "MSFT", .8)])
    def prices(close):
        return market([("2024-01-05", "AAPL", 100, 100), ("2024-01-08", "AAPL", 100, 100),
                       ("2024-01-09", "AAPL", 200, close), ("2024-01-08", "MSFT", 100, 100),
                       ("2024-01-09", "MSFT", 100, 100)])
    expected = run(source, prices(200))
    changed = run(source, prices(20000))
    assert expected.trades.total_amount.tolist() == [10000, 11000]
    pd.testing.assert_frame_equal(expected.trades, changed.trades, check_exact=True)


def test_extreme_quantity_fails_clearly_instead_of_nonfinite_results():
    api = import_module("stocklab_ml.backtesting")
    prices = market([("2024-01-05", "AAPL", 1e-300, 1e-300),
                     ("2024-01-08", "AAPL", 1e-300, 1e-300)])
    with pytest.raises(api.BacktestError, match="precision|finite"):
        run(decisions([("2024-01-05", "AAPL", .8)]), prices)


def test_boolean_market_retention_ratio_is_rejected():
    api = import_module("stocklab_ml.backtesting")
    prices = simple_prices()
    prices = replace(prices, report=replace(prices.report, retention_ratio=True))
    with pytest.raises(api.BacktestError, match="report"):
        run(simple_decisions(), prices)


@pytest.mark.parametrize("additional_buy", [False, True])
@pytest.mark.parametrize("price", [10.14, 14.56, .991])
def test_flat_fractional_roundtrip_is_not_a_rounding_induced_win(price, additional_buy):
    source = decisions([("2024-01-05", "AAPL", .8),
                        ("2024-01-08", "AAPL", .8 if additional_buy else .5),
                        ("2024-01-09", "AAPL", .2)])
    prices = market([(day, "AAPL", price, price) for day in
                     ["2024-01-05", "2024-01-08", "2024-01-09", "2024-01-10"]])
    result = run(source, prices)
    assert result.trades.average_cost_after.iloc[0] == price
    assert result.trades.realized_pnl.iloc[-1] == 0
    assert result.report.winning_closed_trades == 0
    assert result.report.win_rate == 0


def test_unequal_price_weighted_break_even_is_not_a_win():
    source = decisions([("2024-01-05", "AAPL", .8), ("2024-01-08", "AAPL", .8),
                        ("2024-01-09", "AAPL", .2)])
    prices = market([("2024-01-05", "AAPL", 10, 10), ("2024-01-08", "AAPL", 10.12, 10.12),
                     ("2024-01-09", "AAPL", 10.14, 10.14), ("2024-01-10", "AAPL", 10.13, 10.13)])
    result = run(source, prices, config=config(initial_capital=1500, max_trade_allocation=.01, quantity_decimals=0))
    assert result.trades.quantity.tolist() == [1, 1, 2]
    assert result.trades.average_cost_after.iloc[1] == 10.13
    assert result.trades.realized_pnl.iloc[-1] == 0
    assert result.report.win_rate == 0


def test_multiple_finite_positions_overflowing_equity_raise_backtest_error():
    api = import_module("stocklab_ml.backtesting")
    prices = market([(day, symbol, 100, close) for symbol in ["AAPL", "MSFT"]
                     for day, close in [("2024-01-05", 100), ("2024-01-08", 1e306)]])
    source = decisions([("2024-01-05", symbol, .8) for symbol in ["AAPL", "MSFT"]])
    with pytest.raises(api.BacktestError, match="finite|precision"):
        run(source, prices)


@pytest.mark.parametrize("old_quantity,bought", [(2 ** 53, 1), (2 ** 53, 3), (1e50, 1)])
def test_accumulated_quantity_rejects_lost_or_invented_shares(old_quantity, bought):
    api = import_module("stocklab_ml.backtesting")
    from stocklab_ml.backtesting.accounting import _add_buy

    with pytest.raises(api.BacktestError, match="quantity|precision"):
        _add_buy(float(old_quantity), 1.0, float(bought), 1.0)


def test_representable_large_quantity_increment_is_preserved():
    from stocklab_ml.backtesting.accounting import _add_buy

    quantity, average_cost = _add_buy(9007199254740992.0, 1.0, 2.0, 1.0)
    assert quantity == 9007199254740994.0
    assert average_cost == 1.0


def test_backtest_rejects_unrepresentable_additional_buy():
    api = import_module("stocklab_ml.backtesting")
    source = decisions([("2024-01-05", "AAPL", .8), ("2024-01-08", "AAPL", .8)])
    prices = market([("2024-01-05", "AAPL", 1, 1), ("2024-01-08", "AAPL", 1, 1),
                     ("2024-01-09", "AAPL", .9999999999999998, .9999999999999998)])
    cfg = config(initial_capital=18014398509481984.0, max_trade_allocation=.5,
                 max_symbol_exposure=.5, quantity_decimals=0)
    with pytest.raises(api.BacktestError, match="quantity|precision"):
        run(source, prices, config=cfg)
