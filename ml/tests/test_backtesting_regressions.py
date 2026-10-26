"""Hand-calculated multi-asset accounting, causal prefixes, and reproducibility."""

from dataclasses import replace

import pandas as pd
import pytest

from stocklab_ml.backtesting import BacktestConfig, run_backtest, save_backtest_result
from test_backtesting import decisions, market


@pytest.fixture
def portfolio_case():
    signals = decisions([
        ("2024-01-05", "AAPL", .8), ("2024-01-05", "MSFT", .8),
        ("2024-01-08", "AAPL", .2), ("2024-01-08", "MSFT", .5),
        ("2024-01-09", "AAPL", .8), ("2024-01-09", "MSFT", .2),
        ("2024-01-10", "AAPL", .5),
    ])
    prices = market([
        ("2024-01-05", "AAPL", 100, 100), ("2024-01-05", "MSFT", 50, 50),
        ("2024-01-08", "AAPL", 100, 120), ("2024-01-08", "MSFT", 50, 40),
        ("2024-01-09", "AAPL", 130, 125), ("2024-01-09", "MSFT", 45, 30),
        ("2024-01-10", "AAPL", 50, 60), ("2024-01-10", "MSFT", 40, 40),
        ("2024-01-11", "AAPL", 55, 45), ("2024-01-11", "MSFT", 40, 40),
    ])
    policy = BacktestConfig(initial_capital=1000, max_trade_allocation=.5,
                            max_symbol_exposure=1, quantity_decimals=2)
    return signals, prices, policy


def test_multi_asset_ledger_reconciles_shared_cash_and_closed_trade_pnl(portfolio_case):
    signals, prices, policy = portfolio_case
    result = run_backtest(signals, prices, config=policy)
    trades = result.trades

    assert trades.symbol.tolist() == ["AAPL", "MSFT", "AAPL", "AAPL", "MSFT"]
    assert trades.signal.tolist() == ["BUY", "BUY", "SELL", "BUY", "SELL"]
    assert trades.execution_date.tolist() == list(pd.to_datetime([
        "2024-01-08", "2024-01-08", "2024-01-09", "2024-01-10", "2024-01-10",
    ]))
    assert trades.execution_price.tolist() == [100, 50, 130, 50, 40]
    assert trades.quantity.tolist() == [5, 10, 5, 10.5, 10]
    assert trades.total_amount.tolist() == [500, 500, 650, 525, 400]
    assert trades.cash_after.tolist() == [500, 0, 650, 125, 525]
    assert trades.portfolio_value_before.tolist() == [1000, 1000, 1100, 1050, 1050]
    assert trades.position_quantity_after.tolist() == [5, 10, 0, 10.5, 0]
    assert trades.average_cost_after.tolist() == [100, 50, 0, 50, 0]
    assert trades.realized_pnl[trades.signal.eq("BUY")].isna().all()
    assert trades.realized_pnl[trades.signal.eq("SELL")].tolist() == [150, -100]
    assert result.report.executed_buys == 3
    assert result.report.executed_sells == 2
    assert result.report.executed_trades == 5
    assert result.report.winning_closed_trades == 1
    assert result.report.losing_or_flat_closed_trades == 1
    assert result.report.win_rate == .5


def test_multi_asset_eod_values_include_cash_and_unrealized_losses(portfolio_case):
    signals, prices, policy = portfolio_case
    result = run_backtest(signals, prices, config=policy)

    assert result.equity_curve.cash.tolist() == [1000, 0, 650, 525, 525]
    assert result.equity_curve.positions_value.tolist() == [0, 1000, 300, 630, 472.5]
    assert result.equity_curve.total_value.tolist() == [1000, 1000, 950, 1155, 997.5]
    assert result.equity_curve.drawdown.tolist() == pytest.approx([0, 0, .05, 0, 3 / 22])
    assert result.positions_history.symbol.tolist() == ["AAPL", "MSFT", "MSFT", "AAPL", "AAPL"]
    assert result.positions_history.market_value.tolist() == [600, 400, 300, 630, 472.5]
    assert result.positions_history.unrealized_pnl.tolist() == [100, -100, -200, 105, -52.5]
    assert result.report.final_cash == 525
    assert result.report.final_positions_value == 472.5
    assert result.report.final_value == 997.5
    assert result.report.total_return == pytest.approx(-.0025)
    assert result.report.max_drawdown == pytest.approx(3 / 22)
    assert result.report.open_positions_final == 1


def test_drawdown_tracks_recovered_peaks_and_keeps_the_worst_historical_loss():
    prices = market([
        ("2024-01-05", "AAPL", 100, 100), ("2024-01-08", "AAPL", 100, 100),
        ("2024-01-09", "AAPL", 100, 80), ("2024-01-10", "AAPL", 100, 120),
        ("2024-01-11", "AAPL", 100, 90), ("2024-01-12", "AAPL", 100, 130),
        ("2024-01-15", "AAPL", 100, 104),
    ])
    result = run_backtest(decisions([("2024-01-05", "AAPL", .8)]), prices,
                          config=BacktestConfig(initial_capital=1000,
                                                max_trade_allocation=1, max_symbol_exposure=1))

    assert result.equity_curve.total_value.tolist() == [1000, 1000, 800, 1200, 900, 1300, 1040]
    assert result.equity_curve.drawdown.tolist() == pytest.approx([0, 0, .2, 0, .25, 0, .2])
    assert result.report.max_drawdown == .25
    assert result.report.total_return == pytest.approx(.04)


@pytest.mark.parametrize("future_price,probability", [(1, .8), (10000, .2)])
def test_future_prices_and_decisions_cannot_rewrite_any_historical_table(
        portfolio_case, future_price, probability):
    signals, prices, policy = portfolio_case
    expected = run_backtest(signals, prices, config=policy)
    signal_rows = list(signals.signals[["date", "symbol", "probability_up"]].itertuples(index=False, name=None))
    price_rows = list(prices.dataframe[["date", "symbol", "open", "close"]].itertuples(index=False, name=None))
    extended_signals = decisions(signal_rows + [
        ("2024-01-11", "AAPL", probability), ("2024-01-11", "MSFT", probability),
        ("2024-01-12", "AAPL", 1 - probability),
    ])
    extended_prices = market(price_rows + [
        ("2024-01-12", "AAPL", future_price, future_price),
        ("2024-01-12", "MSFT", future_price, future_price),
        ("2024-01-15", "AAPL", future_price, future_price),
        ("2024-01-15", "MSFT", future_price, future_price),
    ])
    actual = run_backtest(extended_signals, extended_prices, config=policy)

    for name, date_column in [("trades", "execution_date"), ("equity_curve", "date"),
                              ("positions_history", "date")]:
        frame = getattr(actual, name)
        prefix = frame.loc[frame[date_column] <= pd.Timestamp("2024-01-11")].reset_index(drop=True)
        pd.testing.assert_frame_equal(prefix, getattr(expected, name), check_exact=True)


@pytest.mark.parametrize("seed", [7, 29])
def test_repeated_and_reordered_decisions_produce_identical_tables_and_exports(
        portfolio_case, tmp_path, seed):
    signals, prices, policy = portfolio_case
    expected = run_backtest(signals, prices, config=policy)
    original_signals = signals.signals.copy(deep=True)
    original_prices = prices.dataframe.copy(deep=True)
    shuffled = replace(signals, signals=signals.signals.sample(frac=1, random_state=seed))
    expected_paths = save_backtest_result(expected, output_dir=tmp_path / "expected")

    for index, source in enumerate([signals, shuffled]):
        actual = run_backtest(source, prices, config=policy)
        for name in ["trades", "equity_curve", "positions_history"]:
            pd.testing.assert_frame_equal(getattr(actual, name), getattr(expected, name), check_exact=True)
        assert actual.report == expected.report
        actual_paths = save_backtest_result(actual, output_dir=tmp_path / str(index))
        for name in ["trades_path", "equity_path", "positions_path", "report_path"]:
            assert getattr(actual_paths, name).read_bytes() == getattr(expected_paths, name).read_bytes()

    pd.testing.assert_frame_equal(signals.signals, original_signals, check_exact=True)
    pd.testing.assert_frame_equal(prices.dataframe, original_prices, check_exact=True)
