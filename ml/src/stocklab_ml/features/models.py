"""Version-one feature contract, parameters, and deterministic results."""

from dataclasses import dataclass
from pathlib import Path

import pandas as pd

from stocklab_ml.data.validation import COLUMNS

RETURN_PERIOD = 1
MA_SHORT_WINDOW = 5
MA_LONG_WINDOW = 20
RSI_WINDOW = 14
MOMENTUM_WINDOW = 10
VOLATILITY_WINDOW = 20
VOLATILITY_DDOF = 0

FEATURE_COLUMNS = ["return_1d", "ma_5", "ma_20", "rsi_14", "volume", "momentum_10", "volatility_20"]
DERIVED_COLUMNS = [column for column in FEATURE_COLUMNS if column != "volume"]
OUTPUT_COLUMNS = COLUMNS + DERIVED_COLUMNS
WARMUP_PERIODS = {
    "return_1d": RETURN_PERIOD,
    "ma_5": MA_SHORT_WINDOW - 1,
    "ma_20": MA_LONG_WINDOW - 1,
    "rsi_14": RSI_WINDOW,
    "volume": 0,
    "momentum_10": MOMENTUM_WINDOW,
    "volatility_20": RETURN_PERIOD + VOLATILITY_WINDOW - 1,
}
MIN_OBSERVATIONS = max(WARMUP_PERIODS.values()) + 1
PARAMETERS = {
    "return_period": RETURN_PERIOD, "ma_short_window": MA_SHORT_WINDOW,
    "ma_long_window": MA_LONG_WINDOW, "rsi_window": RSI_WINDOW,
    "momentum_window": MOMENTUM_WINDOW, "volatility_window": VOLATILITY_WINDOW,
    "volatility_ddof": VOLATILITY_DDOF, "volatility_annualized": False,
}


class FeatureEngineeringError(Exception):
    """Invalid cleaned input or an incomplete/invalid feature computation."""


@dataclass(frozen=True)
class FeatureEngineeringReport:
    input_rows: int
    output_rows: int
    warmup_rows_removed: int
    symbols: list[str]
    date_min: str
    date_max: str
    feature_columns: list[str]
    parameters: dict[str, int | bool]
    rows_per_symbol: dict[str, int]
    warmup_removed_per_symbol: dict[str, int]


@dataclass(frozen=True)
class FeatureDatasetResult:
    dataframe: pd.DataFrame
    report: FeatureEngineeringReport
    output_path: Path | None = None
    report_path: Path | None = None
