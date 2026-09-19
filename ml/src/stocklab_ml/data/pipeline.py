"""Prepare local daily OHLCV datasets for the later cleaning phase (#58)."""

from datetime import date
from pathlib import Path

from .models import HistoricalDatasetResult
from .storage import DEFAULT_DATA_DIR, check_collisions, output_paths, save_processed, save_raw
from .twelve_data import fetch_historical
from .validation import build_dataframe, validate_envelope, validate_request


def prepare_historical_dataset(
    symbol: str,
    start_date: str | date,
    end_date: str | date,
    *,
    data_dir: str | Path = DEFAULT_DATA_DIR,
    overwrite: bool = False,
) -> HistoricalDatasetResult:
    """Fetch at most once; retain valid-envelope raw data even if a bar is bad.

    Dates are inclusive calendar boundaries and end_date must precede today.
    Result dates describe actual returned sessions; no completeness of exchange
    calendars is inferred, no gaps are filled, and no retry is performed.
    """
    request = validate_request(symbol, start_date, end_date)
    paths = output_paths(request, Path(data_dir))
    check_collisions(paths, overwrite)
    payload = fetch_historical(request)
    metadata = validate_envelope(payload, request)
    raw_path, processed_path = paths
    save_raw(raw_path, payload, overwrite=overwrite)
    frame = build_dataframe(payload, request)
    save_processed(processed_path, frame, overwrite=overwrite)
    return HistoricalDatasetResult(
        symbol=request.symbol, rows=len(frame),
        start_date=frame.date.iloc[0].date(), end_date=frame.date.iloc[-1].date(),
        raw_path=raw_path, processed_path=processed_path, metadata=metadata, dataframe=frame,
    )
