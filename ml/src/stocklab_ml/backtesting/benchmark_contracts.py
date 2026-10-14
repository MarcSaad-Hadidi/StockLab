"""Auditable price-return benchmark contracts; no model selection policy."""

from dataclasses import dataclass
from pathlib import Path

import pandas as pd

BENCHMARK_METHOD = "equal_weight_buy_and_hold"
ENTRY_TIMING = "first observed session open strictly after start_date, at or before end_date"
VALUATION_TIMING = "EOD latest observed close <= equity date; no final liquidation"
PRICE_ADJUSTMENT_NOTE = "Prices as supplied by the cleaned dataset; no additional split adjustment."
ALLOCATION_COLUMNS = ["symbol", "target_allocation", "entry_date", "entry_price", "quantity",
    "invested_amount", "residual_cash", "final_mark_date", "final_mark_price", "final_market_value",
    "component_final_value", "component_return"]
BENCHMARK_POSITION_COLUMNS = ["date", "symbol", "quantity", "mark_date", "mark_price", "market_value"]


@dataclass(frozen=True)
class BuyAndHoldReport:
    initial_capital: float
    final_cash: float
    final_positions_value: float
    final_value: float
    total_return: float
    max_drawdown: float
    start_date: str
    end_date: str
    symbols: tuple[str, ...]
    symbol_count: int
    invested_symbols: tuple[str, ...]
    uninvested_symbols: tuple[str, ...]
    weighting_method: str = "equal_weight"
    entry_timing: str = ENTRY_TIMING
    valuation_timing: str = VALUATION_TIMING
    quantity_decimals: int = 8
    commission: float = 0.0
    slippage: float = 0.0
    dividends_included: bool = False
    price_adjustment_note: str = PRICE_ADJUSTMENT_NOTE


@dataclass(frozen=True)
class BenchmarkComparisonReport:
    model_name: str
    start_date: str
    end_date: str
    symbols: tuple[str, ...]
    equity_dates: tuple[str, ...]
    initial_capital: float
    ai_final_value: float
    ai_total_return: float
    ai_max_drawdown: float
    benchmark_final_value: float
    benchmark_total_return: float
    benchmark_max_drawdown: float
    final_value_delta: float
    return_delta: float
    same_initial_capital: bool = True
    same_period: bool = True
    same_symbols: bool = True
    same_equity_dates: bool = True
    benchmark_method: str = BENCHMARK_METHOD


@dataclass(frozen=True)
class BenchmarkComparisonResult:
    benchmark_allocations: pd.DataFrame
    benchmark_equity_curve: pd.DataFrame
    benchmark_report: BuyAndHoldReport
    comparison_report: BenchmarkComparisonReport
    benchmark_positions_history: pd.DataFrame


@dataclass(frozen=True)
class SavedBenchmarkComparison:
    allocations_path: Path
    equity_path: Path
    positions_path: Path
    comparison_path: Path
