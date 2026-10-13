"""Chronological, shared-cash simulation; opens execute, closes mark holdings."""

from collections import defaultdict
import math

import numpy as np
import pandas as pd

from stocklab_ml.data.models import CleanedDatasetResult
from stocklab_ml.signals.contracts import ConfidenceSignalsResult
from .accounting import _add_buy, _finite_sum, _product, _size_buy
from .contracts import (
    BacktestConfig, BacktestError, BacktestReport, BacktestResult,
    EQUITY_COLUMNS, POSITION_COLUMNS, TRADE_COLUMNS,
)
from .validation import validate_inputs


def _table(rows: list, columns: list[str], dates: tuple[str, ...], strings: tuple[str, ...] = ()) -> pd.DataFrame:
    return pd.DataFrame(rows, columns=columns).astype({
        name: "datetime64[ns]" if name in dates else "string" if name in strings else "float64"
        for name in columns
    })


def run_backtest(decisions: ConfidenceSignalsResult, market_data: CleanedDatasetResult, *,
                 config: BacktestConfig | None = None) -> BacktestResult:
    """Use supplied decisions once, after close, at the same symbol's next open.

    Period: first decision date through the last observed session of decision
    symbols. Orders execute by date then symbol. Open risk marks use today's
    observed opens, otherwise strictly earlier closes. EOD uses latest closes
    <= date. No final liquidation, fees, slippage, training, network, or clock.
    """
    config = BacktestConfig() if config is None else config
    validate_inputs(decisions, market_data, config)
    signals = decisions.signals
    symbols = tuple(decisions.report.symbols)
    prices = market_data.dataframe[market_data.dataframe.symbol.isin(symbols)]
    sessions = {symbol: group.date.tolist() for symbol, group in prices.groupby("symbol", sort=True)}
    next_session = {(day, symbol): dates[i + 1]
                    for symbol, dates in sessions.items() for i, day in enumerate(dates[:-1])}
    skipped = dict.fromkeys(["skipped_low_confidence", "skipped_no_position", "skipped_max_positions",
                             "skipped_exposure", "skipped_buying_power", "no_next_session"], 0)
    scheduled = defaultdict(list)
    for row in signals.sort_values(["date", "symbol"]).itertuples(index=False):
        if row.signal == "HOLD":
            continue
        if row.confidence < config.minimum_confidence:
            skipped["skipped_low_confidence"] += 1
        elif (row.date, row.symbol) not in next_session:
            skipped["no_next_session"] += 1
        else:
            scheduled[next_session[row.date, row.symbol]].append(row)

    cash, peak = config.initial_capital, config.initial_capital
    positions, close_marks = {}, {}
    trades, history, equity = [], [], []
    start = signals.date.min()
    for day, bars in prices[prices.date >= start].groupby("date", sort=True):
        opens = dict(zip(bars.symbol, bars.open))
        # All today's opens are known at this phase; today's closes are not.
        open_marks = close_marks | opens
        for row in sorted(scheduled[day], key=lambda row: row.symbol):
            symbol, price = row.symbol, opens[row.symbol]
            old_quantity, old_cost = positions.get(symbol, (0.0, 0.0))
            if row.signal == "SELL" and symbol not in positions:
                skipped["skipped_no_position"] += 1
                continue
            if row.signal == "BUY" and symbol not in positions and len(positions) >= config.max_open_positions:
                skipped["skipped_max_positions"] += 1
                continue
            total = _finite_sum([cash, *(_product(qty, open_marks[sym]) for sym, (qty, _) in positions.items())])
            if not math.isfinite(total) or total <= 0:
                raise BacktestError("Portfolio arithmetic exceeds finite float64 precision.")
            pnl = np.nan
            if row.signal == "SELL":
                quantity = old_quantity
                amount = _product(quantity, price)
                pnl = (price - old_cost) * quantity
                cash = _finite_sum((cash, amount))
                new_quantity, average_cost = 0.0, 0.0
                del positions[symbol]
            else:
                remaining = _finite_sum((_product(total, config.max_symbol_exposure), -_product(old_quantity, price)))
                if not math.isfinite(remaining):
                    raise BacktestError("Portfolio arithmetic exceeds finite float64 precision.")
                if remaining <= 0:
                    skipped["skipped_exposure"] += 1
                    continue
                budget = min(cash, _product(total, config.max_trade_allocation), remaining)
                quantity, amount = _size_buy(budget, price, config.quantity_decimals)
                if quantity <= 0 or amount <= 0 or amount > budget:
                    skipped["skipped_buying_power"] += 1
                    continue
                new_quantity, average_cost = _add_buy(old_quantity, old_cost, quantity, price)
                cash = _finite_sum((cash, -amount))
                positions[symbol] = (new_quantity, average_cost)
            if not all(math.isfinite(v) for v in [cash, new_quantity, average_cost, amount]) or cash < 0:
                raise BacktestError("Nonfinite or negative portfolio accounting.")
            trades.append((row.date, day, symbol, row.model_name, row.signal, row.confidence,
                           quantity, price, amount, cash, new_quantity, average_cost, pnl, total))

        close_marks.update(zip(bars.symbol, bars.close))
        values = []
        for symbol, (quantity, average_cost) in sorted(positions.items()):
            mark = close_marks[symbol]
            value, unrealized = _product(quantity, mark), (mark - average_cost) * quantity
            if not math.isfinite(value) or not math.isfinite(unrealized):
                raise BacktestError("Nonfinite end-of-day position valuation.")
            values.append(value)
            history.append((day, symbol, quantity, average_cost, mark, value, unrealized, open_marks[symbol]))
        positions_value = _finite_sum(values)
        total_value = _finite_sum((cash, positions_value))
        if not math.isfinite(total_value) or total_value <= 0:
            raise BacktestError("Nonfinite or nonpositive portfolio equity.")
        peak = max(peak, total_value)
        equity.append((day, cash, positions_value, total_value, (peak - total_value) / peak))

    trade_frame = _table(trades, TRADE_COLUMNS, ("decision_date", "execution_date"), ("symbol", "model_name", "signal"))
    position_frame = _table(history, POSITION_COLUMNS, ("date",), ("symbol",))
    equity_frame = _table(equity, EQUITY_COLUMNS, ("date",))
    buys = int(trade_frame.signal.eq("BUY").sum())
    sells = len(trades) - buys
    wins = int(trade_frame.realized_pnl.gt(0).sum())
    final = equity_frame.iloc[-1]
    total_return = final.total_value / config.initial_capital - 1
    if not math.isfinite(total_return):
        raise BacktestError("Return exceeds finite float64 precision.")
    report = BacktestReport(
        model_name=decisions.report.model_name, initial_capital=config.initial_capital,
        final_cash=float(final.cash), final_positions_value=float(final.positions_value),
        final_value=float(final.total_value), total_return=float(total_return),
        win_rate=wins / sells if sells else 0.0, max_drawdown=float(equity_frame.drawdown.max()),
        decision_count=len(signals), buy_decisions=int(decisions.report.buy_count),
        sell_decisions=int(decisions.report.sell_count), hold_decisions=int(decisions.report.hold_count),
        executed_trades=len(trades), executed_buys=buys, executed_sells=sells,
        winning_closed_trades=wins, losing_or_flat_closed_trades=sells - wins,
        open_positions_final=len(positions), start_date=start.date().isoformat(),
        end_date=equity_frame.date.iloc[-1].date().isoformat(), symbols=symbols, config=config,
        buy_threshold=float(decisions.report.buy_threshold), sell_threshold=float(decisions.report.sell_threshold),
        **skipped,
    )
    return BacktestResult(trade_frame, equity_frame, position_frame, report)
