"""Offline V1 confidence: deterministic signal alignment, without calibration."""

from dataclasses import asdict
from numbers import Integral, Real

import numpy as np
import pandas as pd

from .contracts import (
    ConfidenceScoreError, ConfidenceScoresReport, ConfidenceSignalsResult,
    TradingSignalError, TradingSignalsReport, TradingSignalsResult,
)
from .validation import REQUIRED_COLUMNS, _prediction_rows, _validate_policy

SIGNAL_COLUMNS = ["date", "symbol", "model_name", "predicted_class", "probability_up", "signal"]
SIGNAL_DTYPES = ["datetime64[ns]", "string", "string", "int64", "float64", "string"]


def _validate_signal_batch(frame: pd.DataFrame, report: TradingSignalsReport) -> None:
    if not isinstance(report, TradingSignalsReport):
        raise ConfidenceScoreError("A valid TradingSignalsReport is required.")
    if not isinstance(frame, pd.DataFrame) or list(frame.columns) != SIGNAL_COLUMNS:
        raise ConfidenceScoreError("Invalid signal schema: exactly the six #64 columns are required.")
    try:
        buy, sell = _validate_policy(report.model_name, report.buy_threshold, report.sell_threshold)
        _prediction_rows(frame.loc[:, REQUIRED_COLUMNS])
    except TradingSignalError as error:
        raise ConfidenceScoreError(str(error)) from error
    if [str(dtype) for dtype in frame.dtypes] != SIGNAL_DTYPES:
        raise ConfidenceScoreError("Invalid signal dtypes: the standardized #64 contract is required.")
    if frame.model_name.isna().any() or not frame.model_name.eq(report.model_name).all():
        raise ConfidenceScoreError("Report model_name must match every signal row.")
    if frame.signal.isna().any() or not frame.signal.isin(["BUY", "SELL", "HOLD"]).all():
        raise ConfidenceScoreError("Invalid signal: BUY, SELL, or HOLD required.")
    p, signal = frame.probability_up, frame.signal
    coherent = ((signal.eq("BUY") & p.ge(buy)) | (signal.eq("SELL") & p.le(sell))
                | (signal.eq("HOLD") & p.gt(sell) & p.lt(buy)))
    if not coherent.all():
        raise ConfidenceScoreError("Signal/probability coherence failed for the report thresholds.")
    expected_counts = {
        "input_rows": len(frame), "output_rows": len(frame),
        "buy_count": int(signal.eq("BUY").sum()), "sell_count": int(signal.eq("SELL").sum()),
        "hold_count": int(signal.eq("HOLD").sum()),
    }
    for name, expected in expected_counts.items():
        value = getattr(report, name)
        if isinstance(value, (bool, np.bool_)) or not isinstance(value, Integral) or value != expected:
            raise ConfidenceScoreError(f"Invalid report {name}: must match signal rows.")
    if (not isinstance(report.symbols, list)
            or not all(isinstance(symbol, str) for symbol in report.symbols)
            or report.symbols != sorted(frame.symbol.unique().tolist())
            or not isinstance(report.date_min, str) or not isinstance(report.date_max, str)
            or report.date_min != frame.date.min().date().isoformat()
            or report.date_max != frame.date.max().date().isoformat()):
        raise ConfidenceScoreError("Report symbols and date bounds must match signal rows.")


def _confidence_values(frame: pd.DataFrame) -> np.ndarray:
    p = frame.probability_up.to_numpy()
    return np.where(frame.signal.eq("BUY"), p,
                    np.where(frame.signal.eq("SELL"), 1 - p, 1 - 2 * np.abs(p - 0.5)))


def _validate_confidence(values: pd.Series | np.ndarray) -> None:
    if (values.dtype != np.dtype("float64") or not np.isfinite(values).all()
            or not ((values >= 0) & (values <= 1)).all()):
        raise ConfidenceScoreError("Invalid confidence: finite float64 in [0,1] required; no clipping.")


def _confidence_summary(values: pd.Series) -> dict[str, float]:
    return {"confidence_min": float(values.min()), "confidence_max": float(values.max()),
            "confidence_mean": float(values.mean())}


def _report_metadata(report: TradingSignalsReport) -> dict:
    """Copy validated metadata into native JSON-compatible numeric scalars."""
    metadata = asdict(report)
    for name, value in metadata.items():
        if isinstance(value, Integral):
            metadata[name] = int(value)
        elif isinstance(value, Real):
            metadata[name] = float(value)
    return metadata


def add_confidence_scores(signals_result: TradingSignalsResult) -> ConfidenceSignalsResult:
    """Add confidence without changing any existing decision or input metadata.

    `confidence` is a deterministic signal-alignment score in [0,1], NOT
    guaranteed to represent probability of trade success, expected return,
    probability of profit, or calibrated forecast accuracy. BUY uses p, SELL
    uses 1-p, and HOLD uses 1-2*abs(p-0.5), the strength of model neutrality.
    HOLD is not a probability of no trade or of an unchanged price.

    The existing signal is the source of truth. Its report's thresholds validate
    coherence; they never rescale confidence. Invalid decisions raise instead of
    being regenerated. No model, future outcome, minimum acceptance, or risk rule
    is consulted. Row order, index, all six columns, and source report are preserved.
    """
    if not isinstance(signals_result, TradingSignalsResult):
        raise ConfidenceScoreError("A TradingSignalsResult is required for confidence scoring.")
    if type(signals_result.report) is not TradingSignalsReport:
        raise ConfidenceScoreError("The upstream TradingSignalsReport is required, without confidence fields.")
    _validate_signal_batch(signals_result.signals, signals_result.report)
    frame = signals_result.signals.copy(deep=True)
    values = _confidence_values(frame)
    _validate_confidence(values)
    frame["confidence"] = values
    # Metadata copying also isolates the mutable symbols list in the frozen report.
    report = ConfidenceScoresReport(**_report_metadata(signals_result.report), **_confidence_summary(frame.confidence))
    return ConfidenceSignalsResult(frame, report)


def _validate_confidence_result(result: ConfidenceSignalsResult) -> None:
    """Reject malformed or tampered decisions before any storage write."""
    if (not isinstance(result, ConfidenceSignalsResult)
            or not isinstance(result.report, ConfidenceScoresReport)
            or not isinstance(result.signals, pd.DataFrame)
            or list(result.signals.columns) != SIGNAL_COLUMNS + ["confidence"]):
        raise ConfidenceScoreError("A valid ConfidenceSignalsResult is required for confidence storage.")
    _validate_signal_batch(result.signals.loc[:, SIGNAL_COLUMNS], result.report)
    values = result.signals.confidence
    _validate_confidence(values)
    if not np.array_equal(values.to_numpy(), _confidence_values(result.signals)):
        raise ConfidenceScoreError("Stored confidence must match the V1 signal-alignment formula.")
    for name, expected in _confidence_summary(values).items():
        value = getattr(result.report, name)
        if (isinstance(value, (bool, np.bool_)) or not isinstance(value, Real)
                or not 0 <= value <= 1 or value != expected):
            raise ConfidenceScoreError(f"Invalid report {name}: must match confidence values.")
