"""One causal implementation for future training and inference callers."""

import numpy as np
import pandas as pd

from stocklab_ml.data.cleaning import _validate_cleaned
from stocklab_ml.data.models import DataCleaningError
from stocklab_ml.data.validation import COLUMNS
from .models import (
    DERIVED_COLUMNS, FEATURE_COLUMNS, MA_LONG_WINDOW, MA_SHORT_WINDOW,
    MIN_OBSERVATIONS, MOMENTUM_WINDOW, OUTPUT_COLUMNS, PARAMETERS, RETURN_PERIOD,
    RSI_WINDOW, VOLATILITY_DDOF, VOLATILITY_WINDOW, WARMUP_PERIODS,
    FeatureDatasetResult, FeatureEngineeringError, FeatureEngineeringReport,
)


def _validate_input(frame: pd.DataFrame) -> None:
    if not isinstance(frame, pd.DataFrame):
        raise FeatureEngineeringError("Invalid cleaned input: expected an OHLCV DataFrame.")
    try:
        # #58 already enforces its explicit as-of cutoff. Do not introduce a
        # clock/timezone dependency here: check the same structural contract.
        _validate_cleaned(frame, pd.Timestamp.max.normalize())
    except DataCleaningError as error:
        raise FeatureEngineeringError("Invalid cleaned input: " + str(error)) from None


def _compute_return(close: pd.Series) -> pd.Series:
    """Decimal one-session price return, with no filling."""
    return close / close.shift(RETURN_PERIOD) - 1


def _compute_sma(close: pd.Series, window: int) -> pd.Series:
    return close.rolling(window, min_periods=window, center=False).mean()


def _compute_momentum(close: pd.Series) -> pd.Series:
    """Decimal rate of change over MOMENTUM_WINDOW observed sessions."""
    return close / close.shift(MOMENTUM_WINDOW) - 1


def _compute_volatility(returns: pd.Series) -> pd.Series:
    """Unannualized population standard deviation of observed daily returns."""
    return returns.rolling(VOLATILITY_WINDOW, min_periods=VOLATILITY_WINDOW,
                           center=False).std(ddof=VOLATILITY_DDOF)


def _compute_rsi_wilder(close: pd.Series) -> pd.Series:
    """Seed with the mean of the first 14 changes, then Wilder recursion.

    A flat history is neutral (50); only gains/losses produce 100/0.
    Preserving the same seed/history is necessary for exact inference parity.
    """
    delta = close.diff().to_numpy()
    gains, losses = np.maximum(delta, 0), np.maximum(-delta, 0)
    values = np.full(len(close), np.nan, dtype="float64")
    if len(close) <= RSI_WINDOW:
        return pd.Series(values, index=close.index)
    # Divide before summing/multiplying to avoid unnecessary intermediate overflow.
    avg_gain = (gains[1:RSI_WINDOW + 1] / RSI_WINDOW).sum()
    avg_loss = (losses[1:RSI_WINDOW + 1] / RSI_WINDOW).sum()
    for position in range(RSI_WINDOW, len(close)):
        if position > RSI_WINDOW:
            avg_gain = avg_gain * ((RSI_WINDOW - 1) / RSI_WINDOW) + gains[position] / RSI_WINDOW
            avg_loss = avg_loss * ((RSI_WINDOW - 1) / RSI_WINDOW) + losses[position] / RSI_WINDOW
        if avg_loss == 0:
            values[position] = 100.0 if avg_gain > 0 else 50.0
        else:
            with np.errstate(over="ignore"):
                values[position] = 100 - 100 / (1 + avg_gain / avg_loss)
    return pd.Series(values, index=close.index, dtype="float64")


def _validate_feature_values(frame: pd.DataFrame) -> None:
    values = frame[FEATURE_COLUMNS].to_numpy(dtype="float64")
    valid = (
        np.isfinite(values).all()
        and (frame[["ma_5", "ma_20"]] > 0).all().all()
        and frame.rsi_14.between(0, 100).all()
        and (frame.volatility_20 >= 0).all()
        and all(str(frame[column].dtype) == "float64" for column in DERIVED_COLUMNS)
    )
    if not valid:
        raise FeatureEngineeringError("Unexpected feature invalidity: nonfinite, out-of-range, or invalid dtype.")


def _validate_available_ranges(frame: pd.DataFrame) -> None:
    """Enforce bounds even before all other features finish their warm-up."""
    valid = (
        all((frame[column].dropna() > 0).all() for column in ("ma_5", "ma_20"))
        and frame.rsi_14.dropna().between(0, 100).all()
        and (frame.volatility_20.dropna() >= 0).all()
    )
    if not valid:
        raise FeatureEngineeringError("Unexpected feature invalidity: out-of-range feature.")


def _engineer_symbol(group: pd.DataFrame) -> tuple[pd.DataFrame, int]:
    frame = group.copy(deep=True).reset_index(drop=True)
    frame["return_1d"] = _compute_return(frame.close)
    frame["ma_5"] = _compute_sma(frame.close, MA_SHORT_WINDOW)
    frame["ma_20"] = _compute_sma(frame.close, MA_LONG_WINDOW)
    frame["rsi_14"] = _compute_rsi_wilder(frame.close)
    frame["momentum_10"] = _compute_momentum(frame.close)
    frame["volatility_20"] = _compute_volatility(frame.return_1d)
    # Validate every feature after its own warm-up, including rows another
    # feature will remove. Missingness must be exactly the expected prefix.
    for column, warmup in WARMUP_PERIODS.items():
        expected_missing = np.arange(len(frame)) < warmup
        if (not np.array_equal(frame[column].isna().to_numpy(), expected_missing)
                or not np.isfinite(frame[column].iloc[warmup:].to_numpy(dtype=float)).all()):
            raise FeatureEngineeringError(f"Unexpected feature invalidity in {column} for {frame.symbol.iloc[0]}.")
    _validate_available_ranges(frame)
    complete = frame[FEATURE_COLUMNS].notna().all(axis=1)
    retained = frame.loc[complete, OUTPUT_COLUMNS].reset_index(drop=True)
    _validate_feature_values(retained)
    return retained, int((~complete).sum())


def engineer_features(dataframe: pd.DataFrame) -> FeatureDatasetResult:
    """Require #58's exact cleaned contract; never mutate, repair, or fetch.

    Windows count observations per symbol, including the current session.
    Only mathematically expected leading warm-up rows can leave the dataset.
    """
    _validate_input(dataframe)
    sizes = dataframe.groupby("symbol", sort=True).size()
    insufficient = sizes[sizes < MIN_OBSERVATIONS].index.tolist()
    if insufficient:
        raise FeatureEngineeringError(
            f"At least {MIN_OBSERVATIONS} clean daily observations are required per symbol: {', '.join(insufficient)}."
        )
    frames, rows, removed = [], {}, {}
    for symbol, group in dataframe.groupby("symbol", sort=True):
        frame, warmup = _engineer_symbol(group)
        frames.append(frame)
        rows[symbol], removed[symbol] = len(frame), warmup
    output = pd.concat(frames, ignore_index=True).loc[:, OUTPUT_COLUMNS]
    _validate_input(output[COLUMNS])
    if output.empty or list(output.columns) != OUTPUT_COLUMNS:
        raise FeatureEngineeringError("Unexpected feature invalidity: empty dataset or output schema.")
    _validate_feature_values(output)
    report = FeatureEngineeringReport(
        input_rows=len(dataframe), output_rows=len(output), warmup_rows_removed=sum(removed.values()),
        symbols=list(rows), date_min=output.date.min().date().isoformat(),
        date_max=output.date.max().date().isoformat(), feature_columns=FEATURE_COLUMNS.copy(),
        parameters=PARAMETERS.copy(), rows_per_symbol=rows, warmup_removed_per_symbol=removed,
    )
    if report.input_rows - report.warmup_rows_removed != report.output_rows:
        raise FeatureEngineeringError("Unexpected feature invalidity: inconsistent report counts.")
    return FeatureDatasetResult(output, report)
