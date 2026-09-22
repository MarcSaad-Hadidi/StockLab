"""Deterministic supervised, holdout, and baseline result contracts."""

from dataclasses import dataclass
from pathlib import Path

import pandas as pd
from sklearn.ensemble import RandomForestClassifier
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


class ModelEvaluationError(ModelingError):
    """Invalid predictions, inconsistent holdout, or conflicting model metadata."""


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
class RandomForestReport:
    model_name: str
    feature_columns: list[str]
    target_name: str
    target_definition: str
    prediction_timing: str
    split_rule: str
    dataset: SupervisedDatasetReport
    split: HoldoutReport
    scaler: None
    hyperparameters: dict[str, object]
    test_accuracy: float
    feature_importances: dict[str, float]
    random_state: int
    baseline_model_name: str
    baseline_accuracy: float
    accuracy_delta: float


@dataclass(frozen=True)
class RandomForestResult:
    model: RandomForestClassifier
    predictions: pd.DataFrame
    report: RandomForestReport
    source_path: Path | None = None
    baseline_result: LogisticRegressionResult | None = None


@dataclass(frozen=True)
class ModelEvaluationMetrics:
    model_name: str
    accuracy: float
    precision: float
    recall: float
    f1: float
    true_positive: int
    true_negative: int
    false_positive: int
    false_negative: int
    support_positive: int
    support_negative: int
    total_rows: int


@dataclass(frozen=True)
class ModelComparisonReport:
    target_name: str
    target_definition: str
    feature_columns: list[str]
    prediction_timing: str
    split_rule: str
    positive_class: int
    zero_division: int
    split_date: str
    test_fraction: float
    test_rows: int
    test_start: str
    test_end: str
    models: list[ModelEvaluationMetrics]
    metric_deltas: dict[str, float]


@dataclass(frozen=True)
class SavedModelResults:
    report_path: Path
    predictions_path: Path
