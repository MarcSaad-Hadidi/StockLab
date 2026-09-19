"""Strict local cleaned CSV decoding and shared atomic persistence."""

from dataclasses import asdict
from decimal import InvalidOperation
from pathlib import Path
import re

import pandas as pd

from stocklab_ml.data.cleaning import _daily_date, _number, _validate_schema
from stocklab_ml.data.models import DataCleaningError
from stocklab_ml.data.storage import (
    DEFAULT_DATA_DIR, check_collisions, cleaning_paths, load_cleaning_csv,
    save_processed, save_raw,
)
from stocklab_ml.data.validation import COLUMNS
from .engineering import engineer_features
from .models import FeatureDatasetResult, FeatureEngineeringError

DEFAULT_FEATURE_DIR = DEFAULT_DATA_DIR / "processed" / "features"


def _load_cleaned_csv(path: Path) -> pd.DataFrame:
    """Decode #58 serialization without sorting, repairing, or dropping rows."""
    try:
        frame = load_cleaning_csv(path)
        _validate_schema(frame.columns)
        if list(frame.columns) != COLUMNS:
            raise ValueError("Incorrect column order")
        if not frame.date.map(lambda value: bool(re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}", value))).all():
            raise ValueError("Expected ISO calendar dates")
        frame["date"] = pd.Series([_daily_date(value) for value in frame.date], dtype="datetime64[ns]")
        frame["symbol"] = frame.symbol.astype("string")
        for column in COLUMNS[2:6]:
            frame[column] = pd.Series([_number(value) for value in frame[column]], dtype="float64")
        volumes = [_number(value, volume=True) for value in frame.volume]
        if any(not 0 <= value < 2**63 or value != int(value) for value in volumes):
            raise ValueError("Expected nonnegative Int64 volume")
        frame["volume"] = pd.array([int(value) for value in volumes], dtype="Int64")
        return frame
    except (DataCleaningError, ValueError, TypeError, OverflowError, InvalidOperation):
        raise FeatureEngineeringError("Invalid cleaned input: CSV must follow the exact cleaned daily OHLCV contract.") from None


def engineer_cleaned_dataset(
    input_path: str | Path, *, output_path: str | Path | None = None,
    report_path: str | Path | None = None, overwrite: bool = False,
) -> FeatureDatasetResult:
    """Save separate features and a deterministic report without touching input.

    Each file is atomic using #56's writer; the pair is not a transaction.
    A successful returned result means both files have been published.
    """
    source = Path(input_path)
    output = Path(output_path) if output_path is not None else DEFAULT_FEATURE_DIR / source.name
    report = Path(report_path) if report_path is not None else output.with_suffix(".features.json")
    output, report = cleaning_paths(source, output, report)
    check_collisions((output, report), overwrite)
    result = engineer_features(_load_cleaned_csv(source))
    save_processed(output, result.dataframe, overwrite=overwrite)
    save_raw(report, asdict(result.report), overwrite=overwrite)
    return FeatureDatasetResult(result.dataframe, result.report, output, report)
