"""Reconcile benchmark allocations, as-of holdings, equity, and comparison."""

from fractions import Fraction
import math
from numbers import Integral, Real
import re

import numpy as np
import pandas as pd

from stocklab_ml.data.validation import SYMBOL_PATTERN
from .accounting import _finite_sum, _product, _size_buy
from .benchmark_contracts import (
    ALLOCATION_COLUMNS, BENCHMARK_METHOD, BENCHMARK_POSITION_COLUMNS, ENTRY_TIMING,
    PRICE_ADJUSTMENT_NOTE, VALUATION_TIMING, BenchmarkComparisonReport,
    BenchmarkComparisonResult, BuyAndHoldReport,
)
from .contracts import BacktestError, EQUITY_COLUMNS
from .validation import _validate_table


def validate_benchmark_comparison(result: BenchmarkComparisonResult) -> None:
    """Check internal accounting; authentic source prices still require OHLCV.

    Historical marks permit independent daily holdings/equity reconciliation.
    No AI simulation, source cleaning, price repair, clock, or network access.
    """
    if (not isinstance(result, BenchmarkComparisonResult)
            or not isinstance(result.benchmark_report, BuyAndHoldReport)
            or not isinstance(result.comparison_report, BenchmarkComparisonReport)):
        raise BacktestError("A BenchmarkComparisonResult with both official reports is required.")
    report, comparison = result.benchmark_report, result.comparison_report
    for obj, names in [(report, ["initial_capital", "final_cash", "final_positions_value", "final_value",
            "total_return", "max_drawdown", "commission", "slippage"]),
            (comparison, ["initial_capital", "ai_final_value", "ai_total_return", "ai_max_drawdown",
            "benchmark_final_value", "benchmark_total_return", "benchmark_max_drawdown", "final_value_delta", "return_delta"])]:
        for name in names:
            value = getattr(obj, name)
            if isinstance(value, (bool, np.bool_)) or not isinstance(value, Real) or not math.isfinite(value):
                raise BacktestError(f"Invalid benchmark/comparison number: {name}.")
    symbols = report.symbols
    if (not isinstance(symbols, tuple) or not symbols
            or not all(isinstance(s, str) and re.fullmatch(SYMBOL_PATTERN, s) for s in symbols)
            or symbols != tuple(sorted(set(symbols)))
            or isinstance(report.symbol_count, (bool, np.bool_)) or not isinstance(report.symbol_count, Integral)
            or report.symbol_count != len(symbols) or report.initial_capital <= 0
            or isinstance(report.quantity_decimals, (bool, np.bool_)) or not isinstance(report.quantity_decimals, Integral)
            or report.quantity_decimals != 8 or report.weighting_method != "equal_weight"
            or report.entry_timing != ENTRY_TIMING or report.valuation_timing != VALUATION_TIMING
            or report.price_adjustment_note != PRICE_ADJUSTMENT_NOTE
            or report.commission != 0 or report.slippage != 0 or report.dividends_included is not False):
        raise BacktestError("Invalid benchmark universe or Buy & Hold assumptions.")
    allocations, equity, history = (result.benchmark_allocations, result.benchmark_equity_curve,
                                     result.benchmark_positions_history)
    _validate_table(allocations, ALLOCATION_COLUMNS, ("entry_date", "final_mark_date"), ("symbol",), ["symbol"],
                    ("entry_date", "entry_price", "final_mark_date", "final_mark_price"))
    _validate_table(equity, EQUITY_COLUMNS, ("date",), (), ["date"])
    _validate_table(history, BENCHMARK_POSITION_COLUMNS, ("date", "mark_date"), ("symbol",), ["date", "symbol"])
    if (equity.empty or tuple(allocations.symbol) != symbols
            or report.start_date != equity.date.iloc[0].date().isoformat()
            or report.end_date != equity.date.iloc[-1].date().isoformat()
            or comparison.start_date != report.start_date or comparison.end_date != report.end_date
            or comparison.initial_capital != report.initial_capital or comparison.symbols != symbols
            or not isinstance(comparison.equity_dates, tuple)
            or comparison.equity_dates != tuple(day.date().isoformat() for day in equity.date)
            or any(getattr(comparison, name) is not True for name in
                   ["same_initial_capital", "same_period", "same_symbols", "same_equity_dates"])
            or comparison.benchmark_method != BENCHMARK_METHOD
            or not isinstance(comparison.model_name, str)
            or re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}", comparison.model_name) is None
            or not history.date.isin(equity.date).all()):
        raise BacktestError("Benchmark comparison period, dates, capital, or universe mismatch.")

    capital = Fraction(str(report.initial_capital))
    budget = capital / len(symbols)
    start, end = equity.date.iloc[0], equity.date.iloc[-1]
    equity_dates = set(equity.date)
    entries = {}
    for row in allocations.itertuples(index=False):
        nullable = [row.entry_date, row.entry_price, row.final_mark_date, row.final_mark_price]
        if row.target_allocation != _finite_sum((budget,)) or row.quantity < 0:
            raise BacktestError("Benchmark target allocations must be equal, quantities nonnegative.")
        amount, value = Fraction(0), Fraction(0)
        if row.quantity:
            if (any(pd.isna(v) for v in nullable) or row.entry_price <= 0 or row.final_mark_price <= 0
                    or not start < row.entry_date <= row.final_mark_date <= end
                    or row.entry_date not in equity_dates or row.final_mark_date not in equity_dates):
                raise BacktestError("Invalid benchmark entry or final as-of mark.")
            quantity, amount = _size_buy(budget, row.entry_price, 8)
            if row.quantity != quantity or amount <= 0 or _finite_sum((amount,)) <= 0:
                raise BacktestError("Benchmark quantity must floor its own equal tranche to 8dp.")
            value = _product(quantity, row.final_mark_price)
            entries[row.symbol] = row
        elif not all(pd.isna(v) for v in nullable):
            raise BacktestError("Uninvested benchmark symbols must have null entry and mark fields.")
        component = budget - amount + value
        expected = {"invested_amount": _finite_sum((amount,)), "residual_cash": _finite_sum((budget - amount,)),
            "final_market_value": _finite_sum((value,)), "component_final_value": _finite_sum((component,)),
            "component_return": _finite_sum((component / budget - 1,))}
        if any(getattr(row, name) != v for name, v in expected.items()):
            raise BacktestError("Benchmark allocation accounting does not reconcile.")
    if (report.invested_symbols != tuple(sorted(entries))
            or report.uninvested_symbols != tuple(s for s in symbols if s not in entries)):
        raise BacktestError("Benchmark invested/uninvested symbol audit does not reconcile.")

    by_day = {day: rows for day, rows in history.groupby("date", sort=False)}
    cash, peak, last_marks = capital, report.initial_capital, {}
    for day in equity.itertuples(index=False):
        active = {s: entry for s, entry in entries.items() if entry.entry_date <= day.date}
        for entry in entries.values():
            if entry.entry_date == day.date:
                cash -= _product(entry.quantity, entry.entry_price)
        snapshots = by_day.get(day.date)
        if (set() if snapshots is None else set(snapshots.symbol)) != set(active):
            raise BacktestError("Benchmark snapshots must cover all held symbols only.")
        values = []
        if snapshots is not None:
            for snapshot in snapshots.itertuples(index=False):
                entry = active[snapshot.symbol]
                previous = last_marks.get(snapshot.symbol)
                if (snapshot.quantity != entry.quantity or snapshot.mark_price <= 0
                        or not entry.entry_date <= snapshot.mark_date <= snapshot.date
                        or snapshot.mark_date not in equity_dates
                        or (previous is not None and (snapshot.mark_date < previous[0]
                            or (snapshot.mark_date == previous[0] and snapshot.mark_price != previous[1])))):
                    raise BacktestError("Invalid or future benchmark historical mark/quantity.")
                value = _product(snapshot.quantity, snapshot.mark_price)
                if snapshot.market_value != _finite_sum((value,)):
                    raise BacktestError("Benchmark position value does not reconcile.")
                last_marks[snapshot.symbol] = snapshot.mark_date, snapshot.mark_price
                values.append(value)
        positions_value = sum(values, Fraction(0))
        total = _finite_sum((cash, positions_value))
        peak = max(peak, total)
        if (cash < 0 or total <= 0 or day.cash != _finite_sum((cash,))
                or day.positions_value != _finite_sum((positions_value,)) or day.total_value != total
                or day.drawdown != (peak - total) / peak):
            raise BacktestError("Benchmark daily cash, positions, equity, or drawdown does not reconcile.")
    for symbol, entry in entries.items():
        if last_marks[symbol] != (entry.final_mark_date, entry.final_mark_price):
            raise BacktestError("Benchmark final allocation marks must match final holdings.")

    final = equity.iloc[-1]
    expected_report = {"final_cash": final.cash, "final_positions_value": final.positions_value,
        "final_value": final.total_value, "total_return": final.total_value / report.initial_capital - 1,
        "max_drawdown": equity.drawdown.max()}
    expected_comparison = {"benchmark_final_value": report.final_value,
        "benchmark_total_return": report.total_return, "benchmark_max_drawdown": report.max_drawdown,
        "final_value_delta": _finite_sum((comparison.ai_final_value, -report.final_value)),
        "return_delta": _finite_sum((comparison.ai_total_return, -report.total_return))}
    if (any(getattr(report, name) != value for name, value in expected_report.items())
            or any(getattr(comparison, name) != value for name, value in expected_comparison.items())
            or comparison.ai_final_value <= 0 or not 0 <= comparison.ai_max_drawdown <= 1
            or comparison.ai_total_return != comparison.ai_final_value / report.initial_capital - 1):
        raise BacktestError("Benchmark metrics or AI comparison deltas do not reconcile.")
