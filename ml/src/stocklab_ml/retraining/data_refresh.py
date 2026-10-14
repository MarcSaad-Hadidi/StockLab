"""One bounded #56 refresh per symbol, shared by all selected model families."""

from datetime import date, timedelta
from pathlib import Path

import pandas as pd

from stocklab_ml.data.cleaning import clean_historical_dataset
from stocklab_ml.data.pipeline import prepare_historical_dataset
from stocklab_ml.data.models import DataCleaningError, DuplicateConflictError
from stocklab_ml.features.storage import _load_cleaned_csv
from stocklab_ml.versioning.validation import frame_digest
from .contracts import RetrainingDataError


def data_dates(frame: pd.DataFrame) -> dict[str, str]:
    return {symbol: group.date.max().date().isoformat()
            for symbol, group in frame.groupby("symbol", sort=True)}


def load_dataset(path: Path, symbols, cutoff: date) -> pd.DataFrame:
    try:
        frame = _load_cleaned_csv(path)
        cleaned = clean_historical_dataset(frame, as_of_date=cutoff).dataframe
        if not frame.equals(cleaned) or sorted(frame.symbol.unique().tolist()) != list(symbols):
            raise RetrainingDataError("Bootstrap must be a complete clean dataset for exactly the configured symbols.")
        return frame
    except RetrainingDataError:
        raise
    except Exception:
        raise RetrainingDataError("A previously prepared, valid historical dataset is required; bootstrap explicitly with #56/#58.") from None


def refresh_dataset(config, existing: pd.DataFrame, *, cutoff: date, local_data, run_dir: Path,
                    before_request) -> pd.DataFrame:
    frames = [existing]
    local = None
    if local_data is not None:
        try:
            local = clean_historical_dataset(local_data, as_of_date=cutoff).dataframe
        except DuplicateConflictError:
            raise RetrainingDataError("DATA_REVISION_CONFLICT") from None
        except DataCleaningError:
            raise RetrainingDataError("Invalid local refresh data.") from None
        if not set(local.symbol.unique()).issubset(config.symbols):
            raise RetrainingDataError("Refresh contains unconfigured symbols.")
    for symbol, group in existing.groupby("symbol", sort=True):
        # shortcut: revisions outside the overlap are unseen; reconcile full history explicitly after corporate actions.
        start = max(group.date.min().date(), group.date.max().date() - timedelta(days=config.overlap_days))
        if start >= cutoff:
            continue
        if (cutoff - start).days + 1 > config.maximum_refresh_days:
            raise RetrainingDataError("REFRESH_RANGE_EXCEEDED: perform an explicit bounded historical catch-up.")
        if local is not None:
            incoming = local[(local.symbol == symbol) & (local.date >= pd.Timestamp(start))
                             & (local.date <= pd.Timestamp(cutoff))]
        else:
            before_request()
            incoming = prepare_historical_dataset(symbol, start, cutoff,
                data_dir=run_dir, overwrite=True).dataframe
        if not incoming.empty:
            frames.append(incoming)
    try:
        return clean_historical_dataset(pd.concat(frames, ignore_index=True), as_of_date=cutoff).dataframe
    except DuplicateConflictError:
        raise RetrainingDataError("DATA_REVISION_CONFLICT") from None


def session_counts(frame, watermarks) -> dict[str, int]:
    return {symbol: int((group.date > pd.Timestamp(watermarks[symbol])).sum())
            for symbol, group in frame.groupby("symbol", sort=True)}


def snapshot_metadata(frame, cutoff) -> dict:
    return {"symbols": sorted(frame.symbol.unique().tolist()), "date_min": frame.date.min().date().isoformat(),
            "date_max": frame.date.max().date().isoformat(), "rows": len(frame),
            "fingerprint": frame_digest(frame), "data_dates": data_dates(frame),
            "cutoff": cutoff.isoformat(), "preparation": "#56 daily/splits; #58 clean; #60 features unchanged"}
