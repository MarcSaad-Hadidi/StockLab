"""Shared validation for prediction rows and the unchanged signal policy."""

from numbers import Real
import re

import numpy as np
import pandas as pd
from pandas.api.types import is_bool_dtype, is_complex_dtype, is_integer_dtype, is_numeric_dtype

from stocklab_ml.data.validation import SYMBOL_PATTERN
from .contracts import TradingSignalError

REQUIRED_COLUMNS = ["date", "symbol", "predicted_class", "probability_up"]
OPTIONAL_COLUMNS = {"actual_class", "target_date"}


def _prediction_rows(predictions: pd.DataFrame) -> pd.DataFrame:
    if (not isinstance(predictions, pd.DataFrame) or predictions.empty
            or not predictions.columns.is_unique
            or not set(REQUIRED_COLUMNS).issubset(predictions.columns)
            or not set(predictions.columns).issubset(set(REQUIRED_COLUMNS) | OPTIONAL_COLUMNS)):
        raise TradingSignalError("Invalid prediction schema: date, symbol, predicted_class, probability_up required.")
    # Select first: optional historical values are never read, even for validation.
    frame = predictions.loc[:, REQUIRED_COLUMNS].copy(deep=True)
    dates = frame.date
    if (not isinstance(dates.dtype, np.dtypes.DateTime64DType) or dates.isna().any()
            or not dates.eq(dates.dt.normalize()).all()):
        raise TradingSignalError("Invalid prediction date: timezone-naive daily datetime values required.")
    symbols = frame.symbol
    if (not symbols.map(lambda value: isinstance(value, str)).all()
            or not symbols.str.fullmatch(SYMBOL_PATTERN).all()):
        raise TradingSignalError("Invalid prediction symbol: normalized symbols required.")
    classes = frame.predicted_class
    if (not is_integer_dtype(classes.dtype) or is_bool_dtype(classes.dtype)
            or classes.isna().any() or not classes.isin([0, 1]).all()):
        raise TradingSignalError("Invalid predicted class: integer binary values 0 or 1 required.")
    probability = frame.probability_up
    if (not is_numeric_dtype(probability.dtype) or is_bool_dtype(probability.dtype)
            or is_complex_dtype(probability.dtype) or probability.isna().any()
            or not np.isfinite(probability.to_numpy(dtype="float64")).all()
            or not probability.between(0, 1).all()):
        raise TradingSignalError("Invalid probability: finite real values in [0,1] required.")
    if frame.duplicated(["date", "symbol"]).any():
        raise TradingSignalError("Duplicate prediction row: each (date, symbol) must be unique within a model batch.")
    try:
        return frame.astype({"date": "datetime64[ns]", "symbol": "string",
                             "predicted_class": "int64", "probability_up": "float64"})
    except (ValueError, OverflowError):
        raise TradingSignalError("Invalid prediction date: must fit datetime64[ns].") from None


def _validate_policy(model_name: str, buy_threshold: float, sell_threshold: float) -> tuple[float, float]:
    if (not isinstance(model_name, str)
            or re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}", model_name) is None):
        raise TradingSignalError("Invalid model name: use 1-128 trimmed letters, digits, underscores, dots or hyphens.")
    # The bounded comparison also rejects NaN/infinity without coercing integers.
    if (any(isinstance(value, (bool, np.bool_)) or not isinstance(value, Real)
            for value in [buy_threshold, sell_threshold])
            or not 0 <= sell_threshold < buy_threshold <= 1):
        raise TradingSignalError("Invalid threshold: finite numeric 0 <= sell < buy <= 1 required; booleans forbidden.")
    buy_threshold, sell_threshold = float(buy_threshold), float(sell_threshold)
    if not sell_threshold < buy_threshold:
        raise TradingSignalError("Invalid threshold: buy and sell must remain distinct at float64 precision.")
    return buy_threshold, sell_threshold
