"""Daily, model-agnostic signal contracts; no risk or execution state."""

from dataclasses import dataclass
from enum import StrEnum
from pathlib import Path

import pandas as pd

BUY_THRESHOLD = 0.60
SELL_THRESHOLD = 0.40


class TradingSignal(StrEnum):
    BUY = "BUY"
    SELL = "SELL"
    HOLD = "HOLD"


class TradingSignalError(Exception):
    """Invalid predictions, model identity, or signal policy."""


@dataclass(frozen=True)
class TradingSignalsReport:
    model_name: str
    input_rows: int
    output_rows: int
    buy_count: int
    sell_count: int
    hold_count: int
    buy_threshold: float
    sell_threshold: float
    date_min: str
    date_max: str
    symbols: list[str]


@dataclass(frozen=True)
class TradingSignalsResult:
    signals: pd.DataFrame
    report: TradingSignalsReport


@dataclass(frozen=True)
class SavedTradingSignals:
    signals_path: Path
    report_path: Path


class ConfidenceScoreError(Exception):
    """Invalid signal result, upstream policy, or confidence score."""


@dataclass(frozen=True)
class ConfidenceScoresReport(TradingSignalsReport):
    """Descriptive alignment statistics; not model ranking or risk approval."""

    confidence_min: float
    confidence_max: float
    confidence_mean: float


@dataclass(frozen=True)
class ConfidenceSignalsResult:
    """ML decisions with confidence, a deterministic signal-alignment score.

    Not guaranteed to represent probability of trade success, expected return,
    probability of profit, or calibrated forecast accuracy.
    """

    signals: pd.DataFrame
    report: ConfidenceScoresReport
