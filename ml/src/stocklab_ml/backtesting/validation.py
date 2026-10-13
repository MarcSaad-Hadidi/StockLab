"""Reuse upstream contracts without cleaning, repairing, or consulting a clock."""

from dataclasses import asdict
from fractions import Fraction
import math
from numbers import Integral, Real
import re

import numpy as np
import pandas as pd

from stocklab_ml.data.cleaning import _validate_cleaned
from stocklab_ml.data.models import CleanedDatasetResult, DataCleaningError, DataCleaningReport
from stocklab_ml.data.validation import SYMBOL_PATTERN
from stocklab_ml.signals.confidence import _validate_confidence_result
from stocklab_ml.signals.contracts import ConfidenceScoreError, ConfidenceSignalsResult, TradingSignalError
from stocklab_ml.signals.validation import _validate_policy
from .accounting import _add_buy, _buy_limits, _finite_sum, _opening_value, _product
from .contracts import (
    BacktestConfig, BacktestError, BacktestReport, BacktestResult, EXECUTION_TIMING,
    EQUITY_COLUMNS, POSITION_COLUMNS, TRADE_COLUMNS,
)


def validate_inputs(decisions: ConfidenceSignalsResult, market_data: CleanedDatasetResult,
                    config: BacktestConfig) -> None:
    if not isinstance(config, BacktestConfig):
        raise BacktestError("A BacktestConfig is required.")
    BacktestConfig(**asdict(config))
    try:
        _validate_confidence_result(decisions)
    except ConfidenceScoreError as error:
        raise BacktestError(str(error)) from error
    if (not isinstance(market_data, CleanedDatasetResult)
            or not isinstance(market_data.dataframe, pd.DataFrame)
            or not isinstance(market_data.report, DataCleaningReport)):
        raise BacktestError("A CleanedDatasetResult with its DataCleaningReport is required.")
    frame, report = market_data.dataframe, market_data.report
    try:
        _validate_cleaned(frame, pd.Timestamp.max.normalize())
    except DataCleaningError as error:
        raise BacktestError(str(error)) from error
    for name in ["input_rows", "output_rows", "rows_removed"]:
        value = getattr(report, name)
        if isinstance(value, (bool, np.bool_)) or not isinstance(value, Integral) or value < 0:
            raise BacktestError(f"Invalid market report {name}.")
    reasons = report.removal_reasons
    if (not isinstance(reasons, dict)
            or any(isinstance(v, (bool, np.bool_)) or not isinstance(v, Integral) or v < 0 for v in reasons.values())
            or isinstance(report.retention_ratio, (bool, np.bool_)) or not isinstance(report.retention_ratio, Real)
            or report.output_rows != len(frame) or report.input_rows != len(frame) + report.rows_removed
            or report.rows_removed != sum(reasons.values())
            or report.retention_ratio != len(frame) / report.input_rows
            or report.symbols != frame.symbol.unique().tolist()
            or report.date_min != frame.date.min().date().isoformat()
            or report.date_max != frame.date.max().date().isoformat()):
        raise BacktestError("Market report must match the cleaned OHLCV dataset.")
    observed = set(zip(frame.date, frame.symbol))
    for row in decisions.signals.itertuples(index=False):
        if (row.date, row.symbol) not in observed:
            raise BacktestError(f"Decision session missing from market data: {row.symbol} {row.date.date()}.")


def _validate_table(frame: pd.DataFrame, columns: list[str], dates: tuple[str, ...],
                    strings: tuple[str, ...], order: list[str], nullable: tuple[str, ...] = ()) -> None:
    if not isinstance(frame, pd.DataFrame) or list(frame.columns) != columns:
        raise BacktestError("Invalid backtest table schema.")
    for name in columns:
        values = frame[name]
        dtype = "datetime64[ns]" if name in dates else "string" if name in strings else "float64"
        if str(values.dtype) != dtype or (name not in nullable and values.isna().any()):
            raise BacktestError(f"Invalid backtest table dtype or nulls: {name}.")
        if name in dates and not values.eq(values.dt.normalize()).all():
            raise BacktestError("Daily timezone-naive backtest dates required.")
        if name not in dates + strings and not np.isfinite(values.dropna().to_numpy()).all():
            raise BacktestError(f"Nonfinite backtest values: {name}.")
    if (not frame.index.equals(pd.RangeIndex(len(frame))) or frame.duplicated(order).any()
            or not frame.equals(frame.sort_values(order).reset_index(drop=True))):
        raise BacktestError("Backtest table order, index, or unique keys invalid.")


def validate_result(result: BacktestResult) -> None:
    """Replay ledger accounting and reconcile EOD holdings, equity, and report.

    Checks internal consistency; source market-price authenticity requires the
    original OHLCV. No prices or decisions are regenerated during storage.
    """
    if not isinstance(result, BacktestResult) or not isinstance(result.report, BacktestReport):
        raise BacktestError("A BacktestResult with its BacktestReport is required.")
    report = result.report
    if not isinstance(report.config, BacktestConfig):
        raise BacktestError("Backtest report config is required.")
    cfg = BacktestConfig(**asdict(report.config))
    try:
        _validate_policy(report.model_name, report.buy_threshold, report.sell_threshold)
    except TradingSignalError as error:
        raise BacktestError(str(error)) from error
    if (not isinstance(report.symbols, tuple) or not report.symbols
            or not all(isinstance(s, str) and re.fullmatch(SYMBOL_PATTERN, s) for s in report.symbols)
            or report.symbols != tuple(sorted(set(report.symbols)))
            or report.execution_timing != EXECUTION_TIMING or report.commission != 0 or report.slippage != 0):
        raise BacktestError("Invalid backtest report symbols or execution policy.")
    count_names = ["decision_count", "buy_decisions", "sell_decisions", "hold_decisions", "executed_trades",
                   "executed_buys", "executed_sells", "winning_closed_trades", "losing_or_flat_closed_trades",
                   "open_positions_final", "skipped_low_confidence", "skipped_no_position",
                   "skipped_max_positions", "skipped_exposure", "skipped_buying_power", "no_next_session"]
    for name in count_names:
        value = getattr(report, name)
        if isinstance(value, (bool, np.bool_)) or not isinstance(value, Integral) or value < 0:
            raise BacktestError(f"Invalid backtest report count: {name}.")
    for name in ["initial_capital", "final_cash", "final_positions_value", "final_value", "total_return",
                 "win_rate", "max_drawdown", "commission", "slippage"]:
        value = getattr(report, name)
        if isinstance(value, (bool, np.bool_)) or not isinstance(value, Real) or not math.isfinite(value):
            raise BacktestError(f"Invalid backtest report number: {name}.")
    trades, equity, history = result.trades, result.equity_curve, result.positions_history
    _validate_table(trades, TRADE_COLUMNS, ("decision_date", "execution_date"),
                    ("symbol", "model_name", "signal"), ["execution_date", "symbol"], ("realized_pnl",))
    _validate_table(equity, EQUITY_COLUMNS, ("date",), (), ["date"])
    _validate_table(history, POSITION_COLUMNS, ("date",), ("symbol",), ["date", "symbol"])
    if (equity.empty or not isinstance(report.start_date, str) or not isinstance(report.end_date, str)
            or report.start_date != equity.date.iloc[0].date().isoformat()
            or report.end_date != equity.date.iloc[-1].date().isoformat()
            or report.initial_capital != cfg.initial_capital
            or equity.total_value.iloc[0] != cfg.initial_capital
            or not history.date.isin(equity.date).all()
            or not trades.execution_date.isin(equity.date).all()
            or not trades.decision_date.isin(equity.date).all()
            or not trades.execution_date.gt(trades.decision_date).all()
            or trades.duplicated(["decision_date", "symbol"]).any()
            or not trades.symbol.isin(report.symbols).all() or not history.symbol.isin(report.symbols).all()
            or not trades.model_name.eq(report.model_name).all()
            or not trades.signal.isin(["BUY", "SELL"]).all()
            or not trades.confidence.between(cfg.minimum_confidence, 1).all()
            or not trades.quantity.gt(0).all() or not trades.execution_price.gt(0).all()
            or not trades.total_amount.gt(0).all() or not trades.portfolio_value_before.gt(0).all()
            or not history.opening_mark_price.gt(0).all()):
        raise BacktestError("Invalid backtest timing, policy, or initial state.")

    trades_by_day = {day: group for day, group in trades.groupby("execution_date", sort=False)}
    history_by_day = {day: group for day, group in history.groupby("date", sort=False)}
    cash, peak = cfg.initial_capital, cfg.initial_capital
    positions = {}
    for eod in equity.itertuples(index=False):
        snapshots = history_by_day.get(eod.date)
        opening_marks = {} if snapshots is None else dict(zip(snapshots.symbol, snapshots.opening_mark_price))
        orders = trades_by_day.get(eod.date)
        if orders is not None:
            for trade in orders.itertuples(index=False):
                if opening_marks.get(trade.symbol, trade.execution_price) != trade.execution_price:
                    raise BacktestError("Opening mark must match the same-day execution price.")
                opening_marks[trade.symbol] = trade.execution_price
            for trade in orders.itertuples(index=False):
                old_quantity, old_cost, old_basis = positions.get(trade.symbol, (0.0, 0.0, Fraction(0)))
                amount = _product(trade.quantity, trade.execution_price)
                if not set(positions).issubset(opening_marks):
                    raise BacktestError("Opening marks must cover every held position.")
                opening_value = _opening_value(cash, positions, opening_marks)
                if trade.portfolio_value_before != _finite_sum((opening_value,)):
                    raise BacktestError("Opening portfolio value does not reconcile with all held positions.")
                if trade.signal == "BUY":
                    if not pd.isna(trade.realized_pnl):
                        raise BacktestError("BUY cannot have realized P&L.")
                    if round(trade.quantity, cfg.quantity_decimals) != trade.quantity:
                        raise BacktestError("BUY quantity exceeds configured precision.")
                    _, budget = _buy_limits(cash, opening_value, old_quantity, trade.execution_price, cfg)
                    if Fraction(str(amount)) > budget:
                        raise BacktestError("BUY amount exceeds reported cash, allocation, or exposure policy.")
                    new_quantity, average = _add_buy(old_quantity, old_cost, trade.quantity, trade.execution_price)
                    cash = _finite_sum((cash, -amount))
                    positions[trade.symbol] = (new_quantity, average, old_basis + Fraction(str(amount)))
                else:
                    if trade.symbol not in positions or trade.quantity != old_quantity:
                        raise BacktestError("SELL must close one entire existing position.")
                    pnl = _finite_sum((amount, -old_basis))
                    if trade.realized_pnl != pnl:
                        raise BacktestError("SELL realized P&L does not match accounted cost basis.")
                    cash = _finite_sum((cash, amount))
                    new_quantity, average = 0.0, 0.0
                    del positions[trade.symbol]
                if (cash < 0 or len(positions) > cfg.max_open_positions
                        or trade.total_amount != amount or trade.cash_after != cash
                        or trade.position_quantity_after != new_quantity or trade.average_cost_after != average):
                    raise BacktestError("Trade ledger accounting does not reconcile.")
        snapshot_symbols = set() if snapshots is None else set(snapshots.symbol)
        if snapshot_symbols != set(positions):
            raise BacktestError("Historical snapshots must contain every open position only.")
        values = []
        if snapshots is not None:
            for snapshot in snapshots.itertuples(index=False):
                quantity, cost, cost_basis = positions[snapshot.symbol]
                value = _product(quantity, snapshot.mark_price)
                if (snapshot.quantity != quantity or snapshot.average_cost != cost or snapshot.mark_price <= 0
                        or snapshot.market_value != value
                        or snapshot.unrealized_pnl != _finite_sum((value, -cost_basis))):
                    raise BacktestError("Historical position accounting does not reconcile.")
                values.append(value)
        positions_value = _finite_sum(values)
        total = _finite_sum((cash, positions_value))
        peak = max(peak, total)
        if (total <= 0 or eod.cash != cash or eod.positions_value != positions_value or eod.total_value != total
                or eod.drawdown != (peak - total) / peak):
            raise BacktestError("End-of-day equity or drawdown does not reconcile.")

    buys = int(trades.signal.eq("BUY").sum())
    sells, wins = len(trades) - buys, int(trades.realized_pnl.gt(0).sum())
    final = equity.iloc[-1]
    expected = {"final_cash": final.cash, "final_positions_value": final.positions_value,
                "final_value": final.total_value, "total_return": final.total_value / cfg.initial_capital - 1,
                "win_rate": wins / sells if sells else 0.0, "max_drawdown": equity.drawdown.max(),
                "executed_trades": len(trades), "executed_buys": buys, "executed_sells": sells,
                "winning_closed_trades": wins, "losing_or_flat_closed_trades": sells - wins,
                "open_positions_final": len(positions)}
    skipped = sum(getattr(report, name) for name in count_names if name.startswith("skipped_") or name == "no_next_session")
    if (any(getattr(report, name) != value for name, value in expected.items())
            or report.decision_count != report.buy_decisions + report.sell_decisions + report.hold_decisions
            or report.decision_count != len(trades) + report.hold_decisions + skipped
            or report.executed_buys > report.buy_decisions or report.executed_sells > report.sell_decisions
            or report.skipped_no_position > report.sell_decisions - sells
            or report.skipped_max_positions + report.skipped_exposure + report.skipped_buying_power > report.buy_decisions - buys):
        raise BacktestError("Backtest report metrics and decision audit do not reconcile.")
