"""Offline backtest policy, accounting tables, and immutable report."""

from dataclasses import dataclass
from numbers import Integral, Real
from pathlib import Path

import numpy as np
import pandas as pd

EXECUTION_TIMING = "decision after close[t]; next observed session open for the same symbol"
TRADE_COLUMNS = ["decision_date", "execution_date", "symbol", "model_name", "signal", "confidence",
                 "quantity", "execution_price", "total_amount", "cash_after", "position_quantity_after",
                 "average_cost_after", "realized_pnl", "portfolio_value_before"]
POSITION_COLUMNS = ["date", "symbol", "quantity", "average_cost", "mark_price", "market_value", "unrealized_pnl", "opening_mark_price"]
EQUITY_COLUMNS = ["date", "cash", "positions_value", "total_value", "drawdown"]


class BacktestError(Exception):
    """Invalid backtest input, policy, or accounting; nothing is repaired."""


@dataclass(frozen=True)
class BacktestConfig:
    initial_capital: float = 100000.0
    minimum_confidence: float = 0.70
    max_symbol_exposure: float = 0.20
    max_open_positions: int = 10
    max_trade_allocation: float = 0.10
    allow_short_selling: bool = False
    quantity_decimals: int = 8

    def __post_init__(self) -> None:
        for name in ["initial_capital", "minimum_confidence", "max_symbol_exposure", "max_trade_allocation"]:
            value = getattr(self, name)
            if isinstance(value, (bool, np.bool_)) or not isinstance(value, Real):
                raise BacktestError(f"Invalid {name}: a non-boolean real number is required.")
            try:
                value = float(value)
            except (OverflowError, ValueError):
                raise BacktestError(f"Invalid {name}: finite float64 value required.") from None
            if (not np.isfinite(value) or value < 0
                    or (name != "minimum_confidence" and value == 0)
                    or (name != "initial_capital" and value > 1)):
                raise BacktestError(f"Invalid {name}: finite real within the policy range required.")
            object.__setattr__(self, name, value)
        for name in ["max_open_positions", "quantity_decimals"]:
            value = getattr(self, name)
            if (isinstance(value, (bool, np.bool_)) or not isinstance(value, Integral)
                    or (name == "max_open_positions" and value <= 0)
                    or (name == "quantity_decimals" and not 0 <= value <= 8)):
                raise BacktestError(f"Invalid {name}: positive integer positions or precision 0..8 required.")
            object.__setattr__(self, name, int(value))
        if self.allow_short_selling is not False:
            raise BacktestError("Invalid allow_short_selling: V1 requires False.")


@dataclass(frozen=True)
class BacktestReport:
    model_name: str
    initial_capital: float
    final_cash: float
    final_positions_value: float
    final_value: float
    total_return: float
    win_rate: float
    max_drawdown: float
    decision_count: int
    buy_decisions: int
    sell_decisions: int
    hold_decisions: int
    executed_trades: int
    executed_buys: int
    executed_sells: int
    winning_closed_trades: int
    losing_or_flat_closed_trades: int
    open_positions_final: int
    start_date: str
    end_date: str
    symbols: tuple[str, ...]
    config: BacktestConfig
    buy_threshold: float
    sell_threshold: float
    skipped_low_confidence: int
    skipped_no_position: int
    skipped_max_positions: int
    skipped_exposure: int
    skipped_buying_power: int
    no_next_session: int
    execution_timing: str = EXECUTION_TIMING
    commission: float = 0.0
    slippage: float = 0.0


@dataclass(frozen=True)
class BacktestResult:
    trades: pd.DataFrame
    equity_curve: pd.DataFrame
    positions_history: pd.DataFrame
    report: BacktestReport


@dataclass(frozen=True)
class SavedBacktest:
    trades_path: Path
    equity_path: Path
    positions_path: Path
    report_path: Path
