"""Local deterministic ML portfolio backtesting."""

from .contracts import BacktestConfig, BacktestError, BacktestReport, BacktestResult, SavedBacktest
from .engine import run_backtest
from .storage import save_backtest_result
from .benchmark import compare_to_buy_and_hold
from .benchmark_contracts import (
    BenchmarkComparisonReport, BenchmarkComparisonResult, BuyAndHoldReport, SavedBenchmarkComparison,
)
from .benchmark_storage import save_benchmark_comparison
from .benchmark_validation import validate_benchmark_comparison

__all__ = ["BacktestConfig", "BacktestError", "BacktestReport", "BacktestResult", "SavedBacktest",
           "run_backtest", "save_backtest_result", "compare_to_buy_and_hold", "BuyAndHoldReport",
           "BenchmarkComparisonReport", "BenchmarkComparisonResult", "SavedBenchmarkComparison",
           "save_benchmark_comparison", "validate_benchmark_comparison"]
