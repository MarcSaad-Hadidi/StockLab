"""Fixed, unscaled Random Forest and an identical-holdout Logistic comparison."""

import numpy as np
import pandas as pd
from sklearn.ensemble import RandomForestClassifier
from sklearn.exceptions import NotFittedError
from sklearn.metrics import accuracy_score
from sklearn.utils.validation import check_is_fitted

from stocklab_ml.features import FEATURE_COLUMNS
from .contracts import (
    PREDICTION_TIMING, SPLIT_RULE, TARGET_DEFINITION, TARGET_NAME,
    ModelTrainingError, RandomForestReport, RandomForestResult,
)
from .dataset import _feature_matrix, build_supervised_dataset, chronological_holdout_split
from .logistic import train_logistic_regression

HYPERPARAMETERS = {
    "n_estimators": 300, "criterion": "gini", "max_depth": None,
    "min_samples_split": 2, "min_samples_leaf": 1, "max_features": "sqrt",
    "bootstrap": True, "class_weight": None, "random_state": 42, "n_jobs": 1,
}


def predict_random_forest_direction(
    fitted_model: RandomForestClassifier, feature_dataframe: pd.DataFrame,
) -> pd.DataFrame:
    """Predict from raw FEATURE_COLUMNS, without a target, scaler, or refit."""
    x = _feature_matrix(feature_dataframe)
    if not isinstance(fitted_model, RandomForestClassifier):
        raise ModelTrainingError("A fitted RandomForestClassifier is required for inference.")
    try:
        check_is_fitted(fitted_model)
        if list(getattr(fitted_model, "feature_names_in_", [])) != FEATURE_COLUMNS:
            raise ModelTrainingError("Fitted forest does not use the exact FEATURE_COLUMNS contract.")
        classes = np.asarray(fitted_model.classes_)
        if classes.shape != (2,) or set(classes.tolist()) != {0, 1}:
            raise ModelTrainingError("Fitted forest must contain binary classes 0 and 1.")
        positive_index = int(np.flatnonzero(classes == 1)[0])
        predicted = np.asarray(fitted_model.predict(x))
        probabilities = np.asarray(fitted_model.predict_proba(x))
    except NotFittedError:
        raise ModelTrainingError("A fitted forest is required for inference.") from None
    except (ValueError, FloatingPointError):
        raise ModelTrainingError("Random Forest prediction failed; check the feature magnitudes.") from None
    if (predicted.shape != (len(x),) or not np.isin(predicted, [0, 1]).all()
            or probabilities.shape != (len(x), 2) or not np.isfinite(probabilities).all()
            or not ((probabilities >= 0) & (probabilities <= 1)).all()):
        raise ModelTrainingError("Forest returned invalid binary predictions or probabilities.")
    output = feature_dataframe[["date", "symbol"]].copy().reset_index(drop=True)
    output["predicted_class"] = predicted.astype("int64")
    output["probability_up"] = probabilities[:, positive_index].astype("float64")
    return output


def train_random_forest(
    feature_dataframe: pd.DataFrame, *, test_fraction: float = 0.20,
) -> RandomForestResult:
    """Fit raw purged train rows and compare against #61 on identical test rows.

    The Logistic baseline runs with its unchanged parameters on the same input
    and fraction. Any holdout discrepancy fails; no winner is selected or tuned.
    """
    supervised = build_supervised_dataset(feature_dataframe)
    split = chronological_holdout_split(supervised.dataframe, test_fraction=test_fraction)
    model = RandomForestClassifier(**HYPERPARAMETERS)
    try:
        model.fit(_feature_matrix(split.train), split.train[TARGET_NAME])
    except (ValueError, FloatingPointError):
        raise ModelTrainingError("Random Forest training failed; check the feature magnitudes and labels.") from None
    predictions = predict_random_forest_direction(model, split.test)
    predictions.insert(1, "target_date", split.test.target_date)
    predictions.insert(3, "actual_class", split.test[TARGET_NAME])

    importances = model.feature_importances_
    if (importances.shape != (len(FEATURE_COLUMNS),) or not np.isfinite(importances).all()
            or not (importances >= 0).all() or not np.isclose(importances.sum(), 1.0)):
        raise ModelTrainingError(
            "Invalid or undefined feature importances: the forest must learn impurity-reducing splits with importances summing to 1."
        )

    baseline = train_logistic_regression(feature_dataframe, test_fraction=test_fraction)
    identifiers = ["date", "target_date", "symbol", "actual_class"]
    if (split.report != baseline.report.split
            or not predictions[identifiers].equals(baseline.predictions[identifiers])):
        raise ModelTrainingError("Cannot compare models: Logistic and Random Forest holdout rows or labels differ.")
    accuracy = float(accuracy_score(predictions.actual_class, predictions.predicted_class))
    report = RandomForestReport(
        model_name="random_forest_baseline", feature_columns=FEATURE_COLUMNS.copy(),
        target_name=TARGET_NAME, target_definition=TARGET_DEFINITION,
        prediction_timing=PREDICTION_TIMING, split_rule=SPLIT_RULE,
        dataset=supervised.report, split=split.report, scaler=None,
        hyperparameters=model.get_params(deep=False), test_accuracy=accuracy,
        feature_importances=dict(zip(FEATURE_COLUMNS, importances.tolist(), strict=True)),
        random_state=model.random_state, baseline_model_name=baseline.report.model_name,
        baseline_accuracy=baseline.report.test_accuracy,
        accuracy_delta=accuracy - baseline.report.test_accuracy,
    )
    return RandomForestResult(model, predictions, report)
