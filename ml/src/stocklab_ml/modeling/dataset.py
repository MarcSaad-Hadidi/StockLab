"""Reusable next-session labels and a purged global chronological holdout."""

import math
from numbers import Real

import numpy as np
import pandas as pd
from pandas.api.types import is_bool_dtype, is_complex_dtype, is_integer_dtype, is_numeric_dtype

from stocklab_ml.data.validation import COLUMNS, SYMBOL_PATTERN
from stocklab_ml.features import FEATURE_COLUMNS, FeatureEngineeringError
from stocklab_ml.features.engineering import _validate_feature_values, _validate_input
from stocklab_ml.features.models import OUTPUT_COLUMNS
from .contracts import (
    TARGET_NAME, ChronologicalHoldout, HoldoutReport, ModelDatasetError,
    ModelTrainingError, SupervisedDatasetReport, SupervisedDatasetResult,
)


def _validate_feature_dataset(frame: pd.DataFrame) -> None:
    """Reuse #60's structural/value checks; never repair or recompute features."""
    if not isinstance(frame, pd.DataFrame) or list(frame.columns) != OUTPUT_COLUMNS:
        raise ModelDatasetError("Invalid feature dataset: expected the exact #60 output columns.")
    try:
        _validate_input(frame[COLUMNS])
        _validate_feature_values(frame)
    except (FeatureEngineeringError, TypeError, ValueError):
        raise ModelDatasetError("Invalid feature dataset: must follow the finite, ordered #60 contract.") from None


def _feature_matrix(frame: pd.DataFrame) -> pd.DataFrame:
    """Select the single feature contract for training and target-free inference."""
    required = ["date", "symbol"] + FEATURE_COLUMNS
    if (not isinstance(frame, pd.DataFrame) or frame.empty
            or not frame.columns.is_unique or not set(required).issubset(frame.columns)):
        raise ModelDatasetError("Invalid feature dataset: nonempty rows, date, symbol, and FEATURE_COLUMNS required.")
    dates, symbols = frame.date, frame.symbol
    if (not isinstance(dates.dtype, np.dtypes.DateTime64DType) or dates.isna().any()
            or not dates.eq(dates.dt.normalize()).all()
            or not symbols.map(lambda value: isinstance(value, str)).all()
            or not symbols.str.fullmatch(SYMBOL_PATTERN).all()
            or frame.duplicated(["symbol", "date"]).any()):
        raise ModelDatasetError("Invalid feature dataset: unique daily dates and valid symbols required.")
    x = frame.loc[:, FEATURE_COLUMNS]
    if any(not is_numeric_dtype(dtype) or is_bool_dtype(dtype) or is_complex_dtype(dtype)
           for dtype in x.dtypes):
        raise ModelDatasetError("Invalid feature dataset: real numeric features required.")
    if not np.isfinite(x.to_numpy(dtype="float64", na_value=np.nan)).all():
        raise ModelDatasetError("Invalid feature dataset: NaN and infinity are forbidden.")
    return x


def build_supervised_dataset(feature_dataframe: pd.DataFrame) -> SupervisedDatasetResult:
    """Copy #60 features; label t using only its symbol's next observed session.

    Features become available after close[t]. The final observation per symbol
    has no observable label and is removed, never assigned a negative class.
    """
    _validate_feature_dataset(feature_dataframe)
    frame = feature_dataframe.copy(deep=True)
    groups = frame.groupby("symbol", sort=True)
    next_close = groups.close.shift(-1)
    frame["target_date"] = groups.date.shift(-1)
    labeled = frame.target_date.notna()
    removed = frame.loc[~labeled].groupby("symbol", sort=True).size().to_dict()
    frame = frame.loc[labeled].copy()
    frame[TARGET_NAME] = (next_close.loc[labeled] > frame.close).astype("int64")
    frame = frame.reset_index(drop=True)
    if frame.empty:
        raise ModelDatasetError("Insufficient supervised rows: no next observed session is available.")
    return SupervisedDatasetResult(frame, SupervisedDatasetReport(
        len(feature_dataframe), len(frame), removed,
    ))


def chronological_holdout_split(
    supervised_dataframe: pd.DataFrame, *, test_fraction: float = 0.20,
) -> ChronologicalHoldout:
    """Use the latest ceil(fraction * unique dates) dates as one global test period.

    A train row requires BOTH date < split_date AND target_date < split_date.
    Returned partitions are chronological (date, symbol), with fresh indices.
    This function and build_supervised_dataset are shared contracts for #62.
    """
    if (isinstance(test_fraction, (bool, np.bool_)) or not isinstance(test_fraction, Real)
            or not math.isfinite(test_fraction) or not 0 < test_fraction < 1):
        raise ModelDatasetError("test_fraction must be a finite number strictly between 0 and 1.")
    frame = supervised_dataframe
    if (not isinstance(frame, pd.DataFrame)
            or list(frame.columns) != OUTPUT_COLUMNS + ["target_date", TARGET_NAME]):
        raise ModelDatasetError("Invalid supervised dataset: expected feature columns, target_date, and binary target.")
    _validate_feature_dataset(frame[OUTPUT_COLUMNS])
    y = frame[TARGET_NAME]
    if (not is_integer_dtype(y.dtype) or is_bool_dtype(y.dtype) or y.isna().any()
            or not y.isin([0, 1]).all() or str(frame.target_date.dtype) != "datetime64[ns]"
            or frame.target_date.isna().any()
            or not frame.target_date.eq(frame.target_date.dt.normalize()).all()
            or not frame.target_date.gt(frame.date).all()):
        raise ModelDatasetError("Invalid supervised dataset: integer binary labels and later daily target_date required.")
    dates = frame.date.sort_values().unique()
    test_dates = math.ceil(len(dates) * test_fraction)
    if len(dates) < 2 or test_dates >= len(dates):
        raise ModelDatasetError("Insufficient supervised dates for a nonempty train and holdout.")
    boundary = pd.Timestamp(dates[-test_dates])
    before = frame.date < boundary
    purged = before & (frame.target_date >= boundary)
    train = frame.loc[before & ~purged].sort_values(["date", "symbol"]).reset_index(drop=True)
    test = frame.loc[~before].sort_values(["date", "symbol"]).reset_index(drop=True)
    if train.empty or test.empty:
        raise ModelDatasetError("Empty train or holdout after chronological boundary purge.")
    if train[TARGET_NAME].nunique() != 2:
        raise ModelTrainingError("Single training class: training requires both binary classes 0 and 1.")
    report = HoldoutReport(
        split_date=boundary.date().isoformat(), test_fraction=float(test_fraction),
        train_rows=len(train), test_rows=len(test), purged_boundary_rows=int(purged.sum()),
        train_start=train.date.min().date().isoformat(), train_end=train.date.max().date().isoformat(),
        test_start=test.date.min().date().isoformat(), test_end=test.date.max().date().isoformat(),
        train_rows_per_symbol=train.groupby("symbol", sort=True).size().to_dict(),
        test_rows_per_symbol=test.groupby("symbol", sort=True).size().to_dict(),
        train_positive_rate=float(train[TARGET_NAME].mean()),
        test_positive_rate=float(test[TARGET_NAME].mean()),
    )
    return ChronologicalHoldout(train, test, report)
