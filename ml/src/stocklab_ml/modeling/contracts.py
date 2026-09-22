"""Deterministic supervised, holdout, and baseline result contracts."""

from dataclasses import dataclass
from pathlib import Path

import pandas as pd
from sklearn.pipeline import Pipeline

TARGET_NAME = "target_up_1d"
TARGET_DEFINITION = "1 if the next observed clean session close is higher; 0 if equal or lower, per symbol."
SPLIT_RULE = "Latest ceil(test_fraction * unique feature dates) dates are test; train feature and target dates must precede split_date."
PREDICTION_TIMING = "Available only after the feature session close; predicts the next observed session close direction."


class ModelingError(Exception):
    """Base error for local modeling operations."""


class ModelDatasetError(ModelingError):
    """Invalid feature/supervised data or unusable temporal partitions."""


class ModelTrainingError(ModelingError):
    """Cannot fit a converged binary classifier or use its predictions."""


@dataclass(frozen=True)
class SupervisedDatasetReport:
    input_rows: int
    supervised_rows: int
    unlabeled_rows_removed_per_symbol: dict[str, int]


@dataclass(frozen=True)
class SupervisedDatasetResult:
    dataframe: pd.DataFrame
    report: SupervisedDatasetReport


@dataclass(frozen=True)
class HoldoutReport:
    split_date: str
    test_fraction: float
    train_rows: int
    test_rows: int
    purged_boundary_rows: int
    train_start: str
    train_end: str
    test_start: str
    test_end: str
    train_rows_per_symbol: dict[str, int]
    test_rows_per_symbol: dict[str, int]
    train_positive_rate: float
    test_positive_rate: float


@dataclass(frozen=True)
class ChronologicalHoldout:
    train: pd.DataFrame
    test: pd.DataFrame
    report: HoldoutReport


@dataclass(frozen=True)
class LogisticRegressionReport:
    model_name: str
    feature_columns: list[str]
    target_name: str
    target_definition: str
    prediction_timing: str
    split_rule: str
    dataset: SupervisedDatasetReport
    split: HoldoutReport
    scaler: str
    hyperparameters: dict[str, object]
    test_accuracy: float
    converged: bool
    iterations: list[int]


@dataclass(frozen=True)
class LogisticRegressionResult:
    pipeline: Pipeline
    predictions: pd.DataFrame
    report: LogisticRegressionReport
    source_path: Path | None = None


@dataclass(frozen=True)
class SavedModelResults:
    report_path: Path
    predictions_path: Path
