"""Buy once at each symbol's first causal open, hold, and compare to #73."""

from fractions import Fraction

import pandas as pd

from stocklab_ml.data.models import CleanedDatasetResult
from .accounting import _finite_sum, _product, _size_buy
from .benchmark_contracts import (
    ALLOCATION_COLUMNS, BENCHMARK_POSITION_COLUMNS, BenchmarkComparisonReport,
    BenchmarkComparisonResult, BuyAndHoldReport,
)
from .contracts import BacktestError, BacktestResult, EQUITY_COLUMNS
from .engine import _table
from .validation import validate_market_data, validate_result


def _matching_prices(backtest: BacktestResult, market_data: CleanedDatasetResult) -> pd.DataFrame:
    """Check observable #73 price evidence without regenerating AI decisions."""
    report = backtest.report
    frame = market_data.dataframe
    if not set(report.symbols).issubset(frame.symbol):
        raise BacktestError("Every backtest report symbol must exist in market data.")
    prices = frame[frame.symbol.isin(report.symbols)
                   & frame.date.between(report.start_date, report.end_date)]
    dates = prices.date.drop_duplicates().sort_values().tolist()
    if dates != backtest.equity_curve.date.tolist():
        raise BacktestError("Market sessions must match the backtest equity dates within its exact period.")
    bars = {(row.date, row.symbol): row for row in prices.itertuples(index=False)}
    by_symbol = {symbol: group.set_index("date") for symbol, group in prices.groupby("symbol", sort=True)}
    for trade in backtest.trades.itertuples(index=False):
        bar = bars.get((trade.execution_date, trade.symbol))
        if bar is None:
            raise BacktestError("Market data is missing an AI execution session.")
        sessions = by_symbol[trade.symbol].index
        eligible = sessions[sessions > trade.decision_date]
        if (bar is None or not len(eligible) or eligible[0] != trade.execution_date
                or bar.open != trade.execution_price):
            raise BacktestError("Market data does not match the AI execution sessions/prices.")
    for snapshot in backtest.positions_history.itertuples(index=False):
        observed = by_symbol[snapshot.symbol]
        eod = observed.loc[observed.index <= snapshot.date]
        today = bars.get((snapshot.date, snapshot.symbol))
        earlier = observed.loc[observed.index < snapshot.date]
        opening_mark = today.open if today is not None else earlier.close.iloc[-1] if len(earlier) else None
        if (eod.empty or eod.close.iloc[-1] != snapshot.mark_price
                or opening_mark != snapshot.opening_mark_price):
            raise BacktestError("Market data does not match the AI historical position marks.")
    return prices


def compare_to_buy_and_hold(backtest: BacktestResult,
                            market_data: CleanedDatasetResult) -> BenchmarkComparisonResult:
    """Consume validated #73 metrics and the same supplied local cleaned OHLCV.

    Equal rational tranches, 8dp floored shares, zero costs/dividends/interest.
    Cash stays idle until the first observed open strictly after start_date;
    unavailable tranches are never redistributed. No AI replay or network.
    """
    validate_result(backtest)
    validate_market_data(market_data)
    prices = _matching_prices(backtest, market_data)
    ai = backtest.report
    capital = Fraction(str(ai.initial_capital))
    budget = capital / len(ai.symbols)
    start = pd.Timestamp(ai.start_date)
    allocations, entries = [], {}
    for symbol in ai.symbols:
        observed = prices[prices.symbol.eq(symbol)]
        eligible = observed[observed.date > start]
        quantity, amount = 0.0, Fraction(0)
        entry_date, entry_price, mark_date, mark_price = pd.NaT, float("nan"), pd.NaT, float("nan")
        if not eligible.empty:
            first = eligible.iloc[0]
            quantity, amount = _size_buy(budget, float(first.open), 8)
            if quantity > 0:
                if _finite_sum((amount,)) <= 0:
                    raise BacktestError("Benchmark notional cannot be preserved at float64 precision.")
                entry_date, entry_price = first.date, float(first.open)
                mark_date, mark_price = observed.date.iloc[-1], float(observed.close.iloc[-1])
                entries[symbol] = (entry_date, quantity, amount)
        value = _product(quantity, mark_price) if quantity else Fraction(0)
        residual = budget - amount
        component = residual + value
        allocations.append((symbol, _finite_sum((budget,)), entry_date, entry_price, quantity,
            _finite_sum((amount,)), _finite_sum((residual,)), mark_date, mark_price,
            _finite_sum((value,)), _finite_sum((component,)), _finite_sum((component / budget - 1,))))

    cash, peak = capital, ai.initial_capital
    marks, equity, positions = {}, [], []
    daily = {day: bars for day, bars in prices.groupby("date", sort=False)}
    for day in backtest.equity_curve.date:
        bars = daily[day]
        marks.update({row.symbol: (day, row.close) for row in bars.itertuples(index=False)})
        for entry_date, quantity, amount in entries.values():
            if day == entry_date:
                cash -= amount
        values = []
        for symbol, (entry_date, quantity, _) in sorted(entries.items()):
            if day < entry_date:
                continue
            mark_date, mark_price = marks[symbol]
            value = _product(quantity, mark_price)
            values.append(value)
            positions.append((day, symbol, quantity, mark_date, mark_price, _finite_sum((value,))))
        total_positions = sum(values, Fraction(0))
        total = _finite_sum((cash, total_positions))
        if cash < 0 or total <= 0:
            raise BacktestError("Invalid benchmark cash or portfolio value.")
        peak = max(peak, total)
        equity.append((day, _finite_sum((cash,)), _finite_sum((total_positions,)), total, (peak - total) / peak))

    allocation_frame = _table(allocations, ALLOCATION_COLUMNS, ("entry_date", "final_mark_date"), ("symbol",))
    equity_frame = _table(equity, EQUITY_COLUMNS, ("date",))
    position_frame = _table(positions, BENCHMARK_POSITION_COLUMNS, ("date", "mark_date"), ("symbol",))
    final = equity_frame.iloc[-1]
    benchmark = BuyAndHoldReport(
        initial_capital=ai.initial_capital, final_cash=float(final.cash),
        final_positions_value=float(final.positions_value), final_value=float(final.total_value),
        total_return=_finite_sum((final.total_value / ai.initial_capital - 1,)),
        max_drawdown=float(equity_frame.drawdown.max()), start_date=ai.start_date,
        end_date=ai.end_date, symbols=ai.symbols, symbol_count=len(ai.symbols),
        invested_symbols=tuple(sorted(entries)), uninvested_symbols=tuple(s for s in ai.symbols if s not in entries))
    comparison = BenchmarkComparisonReport(
        model_name=ai.model_name, start_date=ai.start_date, end_date=ai.end_date, symbols=ai.symbols,
        equity_dates=tuple(day.date().isoformat() for day in backtest.equity_curve.date),
        initial_capital=ai.initial_capital, ai_final_value=ai.final_value,
        ai_total_return=ai.total_return, ai_max_drawdown=ai.max_drawdown,
        benchmark_final_value=benchmark.final_value, benchmark_total_return=benchmark.total_return,
        benchmark_max_drawdown=benchmark.max_drawdown,
        final_value_delta=_finite_sum((ai.final_value, -benchmark.final_value)),
        return_delta=_finite_sum((ai.total_return, -benchmark.total_return)))
    return BenchmarkComparisonResult(allocation_frame, equity_frame, benchmark, comparison, position_frame)
