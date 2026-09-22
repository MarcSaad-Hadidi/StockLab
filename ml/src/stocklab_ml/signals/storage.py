"""Local signal CSV and report JSON using the shared per-file atomic writer."""

from dataclasses import asdict
from pathlib import Path

from stocklab_ml.data.models import StorageError
from stocklab_ml.data.storage import check_collisions, cleaning_paths, save_processed, save_raw
from .confidence import _report_metadata, _validate_confidence_result
from .contracts import ConfidenceSignalsResult, SavedTradingSignals, TradingSignalError, TradingSignalsResult

DEFAULT_SIGNALS_DIR = Path(__file__).resolve().parents[3] / "results" / "signals"
DEFAULT_CONFIDENCE_DIR = Path(__file__).resolve().parents[3] / "results" / "confidence"


def save_trading_signals(
    result: TradingSignalsResult, *, signals_path: str | Path | None = None,
    report_path: str | Path | None = None, source_path: str | Path | None = None,
    overwrite: bool = False,
) -> SavedTradingSignals:
    """Save one model batch, with overwrite opt-in and optional source protection.

    Each file is atomic, not the pair: a report failure may leave a complete CSV.
    Callers who loaded predictions from a file should supply source_path.
    """
    if not isinstance(result, TradingSignalsResult):
        raise TradingSignalError("A TradingSignalsResult is required for signal storage.")
    signals = Path(signals_path) if signals_path is not None else DEFAULT_SIGNALS_DIR / "signals.csv"
    report = Path(report_path) if report_path is not None else DEFAULT_SIGNALS_DIR / "report.json"
    return _save_signal_files(result, signals, report, source_path, overwrite)


def save_confidence_signals(
    result: ConfidenceSignalsResult, *, signals_path: str | Path | None = None,
    report_path: str | Path | None = None, source_path: str | Path | None = None,
    overwrite: bool = False,
) -> SavedTradingSignals:
    """Save validated decisions.csv and report.json in results/confidence by default.

    Scores remain decimal float64 values. Replacement is opt-in; each file is
    atomic, not the pair. Pass source_path to protect a loaded signal CSV.
    """
    _validate_confidence_result(result)
    signals = Path(signals_path) if signals_path is not None else DEFAULT_CONFIDENCE_DIR / "decisions.csv"
    report = Path(report_path) if report_path is not None else DEFAULT_CONFIDENCE_DIR / "report.json"
    return _save_signal_files(result, signals, report, source_path, overwrite)


def _save_signal_files(
    result: TradingSignalsResult | ConfidenceSignalsResult, signals: Path, report: Path,
    source_path: str | Path | None, overwrite: bool,
) -> SavedTradingSignals:
    payload = (_report_metadata(result.report) if isinstance(result, ConfidenceSignalsResult)
               else asdict(result.report))
    if source_path is not None:
        cleaning_paths(Path(source_path), signals, report)
    else:
        try:
            if (signals.resolve() == report.resolve()
                    or (signals.exists() and report.exists() and signals.samefile(report))):
                raise StorageError("Signal CSV and report paths must be distinct.")
        except OSError:
            raise StorageError("Could not resolve local signal paths.") from None
    check_collisions((signals, report), overwrite)
    save_processed(signals, result.signals, overwrite=overwrite)
    save_raw(report, payload, overwrite=overwrite)
    return SavedTradingSignals(signals, report)
