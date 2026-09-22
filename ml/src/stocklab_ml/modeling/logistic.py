"""Train-only scaling, real logistic regression, and shared local inference."""

import warnings

import numpy as np
import pandas as pd
from sklearn.exceptions import ConvergenceWarning, NotFittedError
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import accuracy_score
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler
from sklearn.utils.validation import check_is_fitted

from stocklab_ml.features import FEATURE_COLUMNS
from .contracts import (
    PREDICTION_TIMING, SPLIT_RULE, TARGET_DEFINITION, TARGET_NAME,
    LogisticRegressionReport, LogisticRegressionResult, ModelTrainingError,
)
from .dataset import _feature_matrix, build_supervised_dataset, chronological_holdout_split

HYPERPARAMETERS = {"solver": "lbfgs", "C": 1.0, "max_iter": 1000, "class_weight": None}


def predict_direction(fitted_model: Pipeline, feature_dataframe: pd.DataFrame) -> pd.DataFrame:
    """Reuse the fitted scaler and exact feature order, without requiring labels.

    Identifiers preserve caller row order; outputs use a fresh integer index.
    probability_up is the raw sklearn probability for class 1, not confidence.
    """
    x = _feature_matrix(feature_dataframe)
    if (not isinstance(fitted_model, Pipeline)
            or "classifier" not in fitted_model.named_steps or "scaler" not in fitted_model.named_steps):
        raise ModelTrainingError("A fitted modeling pipeline with scaler and classifier is required.")
    classifier = fitted_model.named_steps["classifier"]
    try:
        check_is_fitted(classifier)
        check_is_fitted(fitted_model.named_steps["scaler"])
        if list(fitted_model.feature_names_in_) != FEATURE_COLUMNS:
            raise ModelTrainingError("Fitted model does not use the exact FEATURE_COLUMNS contract.")
        classes = np.asarray(classifier.classes_)
        if classes.shape != (2,) or set(classes.tolist()) != {0, 1}:
            raise ModelTrainingError("Fitted model must contain binary classes 0 and 1.")
        positive_index = int(np.flatnonzero(classes == 1)[0])
        predicted = fitted_model.predict(x)
        probability = fitted_model.predict_proba(x)[:, positive_index]
    except NotFittedError:
        raise ModelTrainingError("A fitted model is required for inference.") from None
    except (ValueError, FloatingPointError):
        raise ModelTrainingError("Model prediction failed for the supplied feature rows.") from None
    if (not np.isin(predicted, [0, 1]).all() or not np.isfinite(probability).all()
            or not ((probability >= 0) & (probability <= 1)).all()):
        raise ModelTrainingError("Model returned invalid binary predictions or probabilities.")
    output = feature_dataframe[["date", "symbol"]].copy().reset_index(drop=True)
    output["predicted_class"] = np.asarray(predicted, dtype="int64")
    output["probability_up"] = np.asarray(probability, dtype="float64")
    return output


def train_logistic_regression(
    feature_dataframe: pd.DataFrame, *, test_fraction: float = 0.20,
) -> LogisticRegressionResult:
    """Fit only the purged train partition; evaluate one chronological holdout."""
    supervised = build_supervised_dataset(feature_dataframe)
    split = chronological_holdout_split(supervised.dataframe, test_fraction=test_fraction)
    pipeline = Pipeline([
        ("scaler", StandardScaler()),
        ("classifier", LogisticRegression(**HYPERPARAMETERS)),
    ])
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("error", ConvergenceWarning)
            pipeline.fit(_feature_matrix(split.train), split.train[TARGET_NAME])
    except ConvergenceWarning:
        raise ModelTrainingError("Logistic Regression training did not converge.") from None
    except (ValueError, FloatingPointError):
        raise ModelTrainingError("Logistic Regression training failed; check the feature magnitudes and labels.") from None
    predictions = predict_direction(pipeline, split.test)
    predictions.insert(1, "target_date", split.test.target_date)
    predictions.insert(3, "actual_class", split.test[TARGET_NAME])
    report = LogisticRegressionReport(
        model_name="logistic_regression_baseline", feature_columns=FEATURE_COLUMNS.copy(),
        target_name=TARGET_NAME, target_definition=TARGET_DEFINITION,
        prediction_timing=PREDICTION_TIMING, split_rule=SPLIT_RULE,
        dataset=supervised.report, split=split.report, scaler="StandardScaler",
        hyperparameters=pipeline.named_steps["classifier"].get_params(deep=False),
        test_accuracy=float(accuracy_score(predictions.actual_class, predictions.predicted_class)),
        converged=True, iterations=pipeline.named_steps["classifier"].n_iter_.tolist(),
    )
    return LogisticRegressionResult(pipeline, predictions, report)
