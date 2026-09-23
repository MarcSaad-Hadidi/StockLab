"""Probability-based ML decisions and alignment confidence; no risk or execution."""

from .contracts import (
    BUY_THRESHOLD, SELL_THRESHOLD, SavedTradingSignals, TradingSignal,
    TradingSignalError, TradingSignalsReport, TradingSignalsResult,
    ConfidenceScoreError, ConfidenceScoresReport, ConfidenceSignalsResult,
)
from .confidence import add_confidence_scores
from .generator import generate_trading_signals
from .storage import save_confidence_signals, save_trading_signals

__all__ = [
    "BUY_THRESHOLD", "SELL_THRESHOLD", "TradingSignal", "TradingSignalError",
    "TradingSignalsReport", "TradingSignalsResult", "SavedTradingSignals",
    "generate_trading_signals", "save_trading_signals",
    "ConfidenceScoreError", "ConfidenceScoresReport", "ConfidenceSignalsResult", "add_confidence_scores",
    "save_confidence_signals",
]
