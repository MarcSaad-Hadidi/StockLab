"""Offline binary metrics and strict comparison of the existing model results."""

from numbers import Real

import numpy as np
import pandas as pd
from pandas.api.types import is_bool_dtype, is_complex_dtype, is_integer_dtype, is_numeric_dtype
from sklearn.metrics import accuracy_score, confusion_matrix, f1_score, precision_score, recall_score

from stocklab_ml.data.validation import SYMBOL_PATTERN
from stocklab_ml.features import FEATURE_COLUMNS
from .contracts import (
    PREDICTION_TIMING, SPLIT_RULE, TARGET_DEFINITION, TARGET_NAME,
    LogisticRegressionResult, ModelComparisonReport, ModelEvaluationError,
    ModelEvaluationMetrics, RandomForestResult,
)
from .random_forest import train_random_forest

PREDICTION_COLUMNS = ["date", "target_date", "symbol", "actual_class", "predicted_class", "probability_up"]


def _validate_predictions(frame: pd.DataFrame) -> None:
    """Require complete daily predictions; never coerce, sort, or repair input."""
    if (not isinstance(frame, pd.DataFrame) or frame.empty
            or list(frame.columns) != PREDICTION_COLUMNS or frame.isna().any().any()):
        raise ModelEvaluationError("Invalid prediction schema: nonempty complete six-column predictions required.")
    for column in ["actual_class", "predicted_class"]:
        values = frame[column]
        if (not is_integer_dtype(values.dtype) or is_bool_dtype(values.dtype)
                or not values.isin([0, 1]).all()):
            raise ModelEvaluationError("Actual and predicted classes must be integer binary values 0 or 1.")
    probability = frame.probability_up
    if (not is_numeric_dtype(probability.dtype) or is_bool_dtype(probability.dtype)
            or is_complex_dtype(probability.dtype)
            or not np.isfinite(probability.to_numpy(dtype="float64")).all()
            or not probability.between(0, 1).all()):
        raise ModelEvaluationError("probability_up must contain finite real probabilities in [0,1].")
    for column in ["date", "target_date"]:
        dates = frame[column]
        if (not isinstance(dates.dtype, np.dtypes.DateTime64DType)
                or not dates.eq(dates.dt.normalize()).all()):
            raise ModelEvaluationError("Prediction dates must be timezone-naive daily datetime values.")
    if not frame.target_date.gt(frame.date).all():
        raise ModelEvaluationError("Every target_date must be strictly later than its feature date.")
    if (not frame.symbol.map(lambda value: isinstance(value, str)).all()
            or not frame.symbol.str.fullmatch(SYMBOL_PATTERN).all()
            or frame.duplicated(["date", "symbol"]).any()):
        raise ModelEvaluationError("Valid symbols and unique (date, symbol) prediction keys are required.")


def evaluate_predictions(*, model_name: str, predictions: pd.DataFrame) -> ModelEvaluationMetrics:
    """Evaluate existing classes with positive class 1 and zero_division=0.

    No model or fit is required. Probabilities are validated, never thresholded
    or recalibrated. Confusion matrix order is [[TN, FP], [FN, TP]].
    """
    if not isinstance(model_name, str) or not model_name.strip():
        raise ModelEvaluationError("A nonempty model_name is required.")
    _validate_predictions(predictions)
    actual = predictions.actual_class.to_numpy(dtype="int64")
    predicted = predictions.predicted_class.to_numpy(dtype="int64")
    metrics = {
        "accuracy": float(accuracy_score(actual, predicted)),
        "precision": float(precision_score(actual, predicted, pos_label=1, zero_division=0)),
        "recall": float(recall_score(actual, predicted, pos_label=1, zero_division=0)),
        "f1": float(f1_score(actual, predicted, pos_label=1, zero_division=0)),
    }
    if not all(np.isfinite(value) and 0 <= value <= 1 for value in metrics.values()):
        raise ModelEvaluationError("Classification metrics must be finite and in [0,1].")
    tn, fp, fn, tp = (int(value) for value in confusion_matrix(actual, predicted, labels=[0, 1]).ravel())
    if tp + tn + fp + fn != len(predictions):
        raise ModelEvaluationError("Confusion counts do not match the prediction count.")
    return ModelEvaluationMetrics(
        model_name=model_name, **metrics, true_positive=tp, true_negative=tn,
        false_positive=fp, false_negative=fn, support_positive=tp + fn,
        support_negative=tn + fp, total_rows=len(predictions),
    )


def _verify_same_holdout(logistic: LogisticRegressionResult, forest: RandomForestResult) -> None:
    if not isinstance(logistic, LogisticRegressionResult) or not isinstance(forest, RandomForestResult):
        raise ModelEvaluationError("LogisticRegressionResult and RandomForestResult are required.")
    expected = {
        "target_name": TARGET_NAME, "target_definition": TARGET_DEFINITION,
        "feature_columns": FEATURE_COLUMNS, "prediction_timing": PREDICTION_TIMING,
        "split_rule": SPLIT_RULE,
    }
    for result, name in [(logistic, "logistic_regression_baseline"), (forest, "random_forest_baseline")]:
        if (result.report.model_name != name
                or any(getattr(result.report, field) != value for field, value in expected.items())):
            raise ModelEvaluationError("Model feature, target, timing, split-rule, or name contract differs.")
        _validate_predictions(result.predictions)
    split = logistic.report.split
    identifiers = ["date", "target_date", "symbol", "actual_class"]
    if (split != forest.report.split
            or not logistic.predictions[identifiers].equals(forest.predictions[identifiers])):
        raise ModelEvaluationError("Models must use exactly the same ordered holdout identifiers, labels, and split report.")
    predictions = logistic.predictions
    # Equality alone cannot validate two identically stale/misreported holdouts.
    start = predictions.date.min().date().isoformat()
    end = predictions.date.max().date().isoformat()
    if (isinstance(split.test_fraction, (bool, np.bool_)) or not isinstance(split.test_fraction, Real)
            or not np.isfinite(split.test_fraction) or not 0 < split.test_fraction < 1
            or split.test_rows != len(predictions) or split.test_start != start or split.test_end != end
            or split.split_date != start
            or split.test_rows_per_symbol != predictions.groupby("symbol", sort=True).size().to_dict()
            or split.test_positive_rate != float(predictions.actual_class.mean())):
        raise ModelEvaluationError("Holdout metadata does not describe the supplied prediction rows.")


def compare_model_results(
    logistic_result: LogisticRegressionResult, random_forest_result: RandomForestResult,
) -> ModelComparisonReport:
    """Compare previously trained results; strict row order, no fitting or selection."""
    _verify_same_holdout(logistic_result, random_forest_result)
    models = []
    for result in [logistic_result, random_forest_result]:
        metrics = evaluate_predictions(model_name=result.report.model_name, predictions=result.predictions)
        recorded_accuracy = result.report.test_accuracy
        if (isinstance(recorded_accuracy, (bool, np.bool_)) or not isinstance(recorded_accuracy, Real)
                or recorded_accuracy != metrics.accuracy):
            raise ModelEvaluationError("Evaluated accuracy differs from the model report test_accuracy.")
        models.append(metrics)
    deltas = {
        name + "_delta": getattr(models[1], name) - getattr(models[0], name)
        for name in ["accuracy", "precision", "recall", "f1"]
    }
    if not all(np.isfinite(value) and -1 <= value <= 1 for value in deltas.values()):
        raise ModelEvaluationError("Metric deltas must be finite and in [-1,1].")
    split = logistic_result.report.split
    return ModelComparisonReport(
        target_name=TARGET_NAME, target_definition=TARGET_DEFINITION,
        feature_columns=FEATURE_COLUMNS.copy(), prediction_timing=PREDICTION_TIMING,
        split_rule=SPLIT_RULE, positive_class=1, zero_division=0,
        split_date=split.split_date, test_fraction=split.test_fraction,
        test_rows=split.test_rows, test_start=split.test_start, test_end=split.test_end,
        models=models, metric_deltas=deltas,
    )


def evaluate_model_baselines(
    feature_dataframe: pd.DataFrame, *, test_fraction: float = 0.20,
) -> ModelComparisonReport:
    """Fit each existing estimator once, reusing RF's already-computed baseline."""
    forest = train_random_forest(feature_dataframe, test_fraction=test_fraction)
    if forest.baseline_result is None:
        raise ModelEvaluationError("The Random Forest run did not retain its Logistic baseline result.")
    return compare_model_results(forest.baseline_result, forest)
