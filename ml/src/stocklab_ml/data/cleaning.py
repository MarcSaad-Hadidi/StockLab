"""Local daily OHLCV cleaning: discard invalid rows, never invent observations."""

from datetime import date, datetime
from decimal import Decimal, InvalidOperation
import math
from numbers import Real
from pathlib import Path
import re

import numpy as np
import pandas as pd

from .models import (
    CleanedDatasetResult, DataCleaningError, DataCleaningReport,
    DuplicateConflictError, SchemaValidationError,
)
from .validation import COLUMNS, SYMBOL_PATTERN
from .storage import (
    check_collisions, cleaning_paths, load_cleaning_csv, save_cleaning_report, save_processed,
)

PRICE_COLUMNS = COLUMNS[2:6]
REMOVAL_REASONS = (
    "missing_value", "invalid_date", "invalid_symbol", "invalid_numeric",
    "invalid_price", "invalid_ohlc", "invalid_volume", "future_date",
    "duplicate_exact_removed",
)
_DAILY_TEXT = re.compile(r"[0-9]{4}-[0-9]{2}-[0-9]{2}(?:[T ]00:00:00(?:\.0{1,9})?)?")
_NUMBER_TEXT = re.compile(r"[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?")


def _validate_schema(columns) -> None:
    if len(columns) != len(COLUMNS) or set(columns) != set(COLUMNS):
        raise SchemaValidationError("Required columns are exactly date,symbol,open,high,low,close,volume; no duplicates.")


def _missing(value: object) -> bool:
    if isinstance(value, str):
        return not value.strip()
    if isinstance(value, Decimal):
        return value.is_nan()  # pandas.isna raises on signaling Decimal NaN
    return pd.api.types.is_scalar(value) and bool(pd.isna(value))


def _daily_date(value: object) -> pd.Timestamp:
    if isinstance(value, str):
        value = value.strip()
        if not _DAILY_TEXT.fullmatch(value):
            raise ValueError("Not a daily date")
    elif not isinstance(value, (date, datetime, pd.Timestamp, np.datetime64)):
        raise ValueError("Not a date representation")
    stamp = pd.Timestamp(value)
    if pd.isna(stamp) or stamp.tzinfo is not None or stamp != stamp.normalize():
        raise ValueError("Not a timezone-naive calendar date")
    return stamp.as_unit("ns")  # reject dates outside datetime64[ns], never wrap


def _number(value: object, *, volume: bool = False) -> Real | Decimal:
    if isinstance(value, (bool, np.bool_)) or not isinstance(value, (str, Real, Decimal)):
        raise ValueError("Not a numeric representation")
    if isinstance(value, str):
        value = value.strip()
        if not _NUMBER_TEXT.fullmatch(value):
            raise ValueError("Not a numeric string")
    # Parse textual volume exactly; preserve the represented value of numeric
    # inputs (str(large_float) can round to a different integer).
    if volume:
        number = Decimal(value) if isinstance(value, str) else value
    else:
        number = float(value)
    if not (number.is_finite() if isinstance(number, Decimal) else math.isfinite(number)):
        raise ValueError("Not a finite number")
    return number


def _normalize_row(values: tuple, cutoff: pd.Timestamp) -> tuple[dict | None, str | None]:
    if any(_missing(value) for value in values):
        return None, "missing_value"
    row = dict(zip(COLUMNS, values))
    try:
        row["date"] = _daily_date(row["date"])
    except (ValueError, TypeError, OverflowError):
        return None, "invalid_date"
    symbol = row["symbol"]
    if not isinstance(symbol, str) or not re.fullmatch(SYMBOL_PATTERN, symbol.strip().upper()):
        return None, "invalid_symbol"
    row["symbol"] = symbol.strip().upper()
    try:
        for column in PRICE_COLUMNS:
            row[column] = _number(row[column])
        row["volume"] = _number(row["volume"], volume=True)
    except (ValueError, TypeError, OverflowError, InvalidOperation):
        return None, "invalid_numeric"
    if any(row[column] <= 0 for column in PRICE_COLUMNS):
        return None, "invalid_price"
    if not row["low"] <= min(row["open"], row["close"]) <= max(row["open"], row["close"]) <= row["high"]:
        return None, "invalid_ohlc"
    volume = row["volume"]
    # Check the exact power-of-two boundary before allocating an integer for
    # a potentially huge Decimal exponent. It is also exact for binary floats.
    if not 0 <= volume < 2**63:
        return None, "invalid_volume"
    integer_volume = int(volume)
    if volume != integer_volume:
        return None, "invalid_volume"
    row["volume"] = integer_volume
    if row["date"] > cutoff:
        return None, "future_date"
    return row, None


def _validate_cleaned(frame: pd.DataFrame, cutoff: pd.Timestamp) -> None:
    """Explicit final contract check, independent of removal/report bookkeeping."""
    _validate_schema(frame.columns)
    prices = frame[PRICE_COLUMNS]
    valid = (
        not frame.empty and list(frame.columns) == COLUMNS
        and not frame.isna().any().any()
        and str(frame.date.dtype) == "datetime64[ns]"
        and str(frame.symbol.dtype) == "string"
        and str(frame.volume.dtype) == "Int64"
        and all(str(prices[c].dtype) == "float64" for c in PRICE_COLUMNS)
        and np.isfinite(prices.to_numpy()).all() and (prices > 0).all().all()
        and (frame.low <= prices[["open", "close"]].min(axis=1)).all()
        and (frame.high >= prices[["open", "close"]].max(axis=1)).all()
        and (frame.volume >= 0).all()
        and frame.symbol.str.fullmatch(SYMBOL_PATTERN).all()
        and (frame.date == frame.date.dt.normalize()).all()
        and (frame.date <= cutoff).all()
        and not frame.duplicated(["symbol", "date"]).any()
        and frame.index.equals(pd.RangeIndex(len(frame)))
        and frame.equals(frame.sort_values(["symbol", "date"]).reset_index(drop=True))
    )
    if not valid:
        raise DataCleaningError("Cleaned dataset failed the final daily OHLCV contract.")


def clean_historical_dataset(
    dataframe: pd.DataFrame, *, as_of_date: str | date | None = None,
) -> CleanedDatasetResult:
    """Copy and clean existing rows; never impute, scale, or infer sessions.

    Reason priority is REMOVAL_REASONS order. Invalid and future rows leave
    before duplicate checks; conflicting valid keys fail the entire operation.
    Dates accept ISO calendar dates or naive midnight datetime representations.
    as_of_date is inclusive, defaults to the local date, and should be supplied
    explicitly for reproducible runs. No credential or network is needed.
    """
    if not isinstance(dataframe, pd.DataFrame):
        raise SchemaValidationError("Expected an OHLCV DataFrame.")
    _validate_schema(dataframe.columns)
    try:
        cutoff = _daily_date(date.today() if as_of_date is None else as_of_date)
    except (ValueError, TypeError, OverflowError):
        raise DataCleaningError("as_of_date must be a supported timezone-naive calendar date.") from None
    source = dataframe.loc[:, COLUMNS].copy(deep=True)
    counts = dict.fromkeys(REMOVAL_REASONS, 0)
    rows = []
    for values in source.itertuples(index=False, name=None):
        row, reason = _normalize_row(values, cutoff)
        if reason is not None:
            counts[reason] += 1
        else:
            rows.append(row)
    if not rows:
        raise DataCleaningError("No valid rows remain after cleaning.")
    # Do not let the constructor infer volume as float before enforcing Int64.
    frame = pd.DataFrame(rows, columns=COLUMNS, dtype=object).astype({
        "date": "datetime64[ns]", "symbol": "string", "volume": "Int64",
        **{column: "float64" for column in PRICE_COLUMNS},
    })
    unique = frame.drop_duplicates()
    if unique.duplicated(["symbol", "date"]).any():
        raise DuplicateConflictError("Conflicting observations for a normalized symbol/date key.")
    counts["duplicate_exact_removed"] = len(frame) - len(unique)
    frame = unique.sort_values(["symbol", "date"]).reset_index(drop=True)
    _validate_cleaned(frame, cutoff)
    report = DataCleaningReport(
        input_rows=len(source), output_rows=len(frame), rows_removed=len(source) - len(frame),
        removal_reasons=counts, date_min=frame.date.min().date().isoformat(),
        date_max=frame.date.max().date().isoformat(), symbols=frame.symbol.unique().tolist(),
        retention_ratio=len(frame) / len(source),
    )
    if report.rows_removed != sum(counts.values()):
        raise DataCleaningError("Cleaning report row counts are inconsistent.")
    return CleanedDatasetResult(dataframe=frame, report=report)


def clean_processed_dataset(
    input_path: str | Path, *, output_path: str | Path | None = None,
    report_path: str | Path | None = None, overwrite: bool = False,
    as_of_date: str | date | None = None,
) -> CleanedDatasetResult:
    """Clean a local CSV into a separate CSV and deterministic JSON report.

    Default destinations are input.parent/cleaned/input.name and the matching
    .report.json. All three paths must be distinct, even with overwrite=True.
    Each output is atomic; the CSV/report pair is not a filesystem transaction.
    Only a successful returned result denotes a completed cleaning run.
    """
    source = Path(input_path)
    output, report = cleaning_paths(
        source, Path(output_path) if output_path is not None else None,
        Path(report_path) if report_path is not None else None,
    )
    check_collisions((output, report), overwrite)
    result = clean_historical_dataset(load_cleaning_csv(source), as_of_date=as_of_date)
    save_processed(output, result.dataframe, overwrite=overwrite)
    save_cleaning_report(report, result.report, overwrite=overwrite)
    return CleanedDatasetResult(result.dataframe, result.report, output, report)
