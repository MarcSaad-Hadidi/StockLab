"""Validate real #61/#62 reports and persisted #63 metrics without fitting."""

from dataclasses import asdict, fields
from datetime import date, datetime, timedelta
import hashlib
import json
import math
import re
from uuid import UUID

import numpy as np
import pandas as pd
from sklearn.ensemble import RandomForestClassifier
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import accuracy_score, f1_score, precision_score, recall_score
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler
from sklearn.utils.validation import check_is_fitted

from stocklab_ml.data.validation import SYMBOL_PATTERN
from stocklab_ml.features import FEATURE_COLUMNS
from stocklab_ml.modeling import evaluate_predictions
from stocklab_ml.modeling.contracts import (
    PREDICTION_TIMING, SPLIT_RULE, TARGET_DEFINITION, TARGET_NAME, HoldoutReport,
    LogisticRegressionReport, LogisticRegressionResult, ModelEvaluationMetrics, ModelingError,
    RandomForestReport, RandomForestResult, SupervisedDatasetReport,
)
from stocklab_ml.signals.contracts import BUY_THRESHOLD, SELL_THRESHOLD, TradingSignalError
from stocklab_ml.signals.validation import _validate_policy
from .contracts import ModelVersionError, ModelVersionRecord, ModelStatus

MODEL_CONTRACTS = {
    "logistic_regression": (LogisticRegressionReport, "logistic_regression_baseline"),
    "random_forest": (RandomForestReport, "random_forest_baseline"),
}


def validate_name(name: str) -> None:
    try:
        _validate_policy(name, BUY_THRESHOLD, SELL_THRESHOLD)
    except TradingSignalError as error:
        raise ModelVersionError("Invalid model_name: the #64 model identity contract is required.") from error


def version_string(number: int) -> str:
    if type(number) is not int or number < 1:
        raise ModelVersionError("version_number must be a positive integer.")
    return f"v{number:06d}"


def validate_version(version: str) -> None:
    if (not isinstance(version, str) or re.fullmatch(r"v[0-9]{6,}", version) is None
            or version_string(int(version[1:])) != version):
        raise ModelVersionError("model_version must be an exact canonical identity such as v000001.")


def validate_uuid(value: str) -> None:
    try:
        if not isinstance(value, str) or str(UUID(value)) != value or UUID(value).int == 0:
            raise ValueError
    except (ValueError, AttributeError):
        raise ModelVersionError("A nonzero canonical lowercase UUID is required.") from None


def validate_timestamp(value: datetime) -> None:
    if (not isinstance(value, datetime) or value.tzinfo is None
            or value.utcoffset() != timedelta(0) or pd.isna(value)):
        raise ModelVersionError("An explicit timezone-aware UTC datetime is required.")


def canonical(payload) -> str:
    try:
        return json.dumps(payload, sort_keys=True, ensure_ascii=False, allow_nan=False)
    except (TypeError, ValueError, OverflowError):
        raise ModelVersionError("Training metadata must contain finite JSON-safe values.") from None


def frame_digest(frame: pd.DataFrame, *, include_index: bool = False) -> str:
    return hashlib.sha256(frame.to_csv(index=include_index, date_format="%Y-%m-%d",
                                     lineterminator="\n").encode("utf-8")).hexdigest()


def _integer(value, *, positive=False) -> None:
    if type(value) is not int or value < int(positive):
        raise ModelVersionError("Metadata counts must be nonnegative integers (positive for row totals).")


def _rate(value) -> None:
    if type(value) not in (int, float) or not math.isfinite(value) or not 0 <= value <= 1:
        raise ModelVersionError("Metrics and rates must be finite real values in [0,1].")


def _day(value: str) -> date:
    if not isinstance(value, str) or re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}", value) is None:
        raise ModelVersionError("Training/holdout dates must be exact ISO daily dates.")
    try:
        return date.fromisoformat(value)
    except ValueError:
        raise ModelVersionError("Invalid training/holdout date.") from None


def _keys(payload: dict, contract) -> None:
    if not isinstance(payload, dict) or set(payload) != {field.name for field in fields(contract)}:
        raise ModelVersionError("Metadata fields do not match the versioned schema contract.")


def validate_metrics(metrics: ModelEvaluationMetrics, name: str) -> None:
    if not isinstance(metrics, ModelEvaluationMetrics) or metrics.model_name != name:
        raise ModelVersionError("Evaluation metrics must identify the registered model_name.")
    for metric in [metrics.accuracy, metrics.precision, metrics.recall, metrics.f1]:
        _rate(metric)
    counts = [metrics.true_positive, metrics.true_negative, metrics.false_positive, metrics.false_negative]
    for value in counts + [metrics.support_positive, metrics.support_negative]:
        _integer(value)
    _integer(metrics.total_rows, positive=True)
    tp, tn, fp, fn = counts
    if (sum(counts) != metrics.total_rows or tp + fn != metrics.support_positive
            or tn + fp != metrics.support_negative):
        raise ModelVersionError("Confusion counts/support must reconcile with total_rows.")
    # Four weighted cells validate persisted metrics with sklearn, without a second metric formula.
    actual, predicted = [1, 0, 0, 1], [1, 0, 1, 0]
    for name, scorer in [("accuracy", accuracy_score), ("precision", precision_score),
                         ("recall", recall_score), ("f1", f1_score)]:
        kwargs = {} if name == "accuracy" else {"pos_label": 1, "zero_division": 0}
        expected = scorer(actual, predicted, sample_weight=counts, **kwargs)
        if not math.isclose(getattr(metrics, name), expected, rel_tol=1e-12, abs_tol=1e-15):
            raise ModelVersionError("Stored classification metrics disagree with confusion counts.")


def validate_report(model_type: str, report: dict, metrics: ModelEvaluationMetrics) -> None:
    if model_type not in MODEL_CONTRACTS:
        raise ModelVersionError("Unsupported model_type.")
    report_class, model_name = MODEL_CONTRACTS[model_type]
    _keys(report, report_class)
    canonical(report)
    validate_name(report["model_name"])
    if report["model_name"] != model_name:
        raise ModelVersionError("Training report model_name does not match the existing model family.")
    expected = {"feature_columns": FEATURE_COLUMNS, "target_name": TARGET_NAME,
                "target_definition": TARGET_DEFINITION, "prediction_timing": PREDICTION_TIMING,
                "split_rule": SPLIT_RULE}
    if any(report[key] != value for key, value in expected.items()):
        raise ModelVersionError("Training feature order, target, timing, or split-rule contract differs.")
    if not isinstance(report["hyperparameters"], dict) or not report["hyperparameters"]:
        raise ModelVersionError("Real JSON-safe estimator hyperparameters are required.")
    validate_metrics(metrics, model_name)
    _rate(report["test_accuracy"])
    if metrics.accuracy != report["test_accuracy"]:
        raise ModelVersionError("Evaluation accuracy differs from training report test_accuracy.")
    split, dataset = report["split"], report["dataset"]
    _keys(split, HoldoutReport)
    _keys(dataset, SupervisedDatasetReport)
    train_start, train_end, test_start, test_end, boundary = (
        _day(split[key]) for key in ["train_start", "train_end", "test_start", "test_end", "split_date"])
    if not train_start <= train_end < boundary == test_start <= test_end:
        raise ModelVersionError("Training/holdout periods violate the chronological split.")
    _rate(split["test_fraction"])
    if not 0 < split["test_fraction"] < 1:
        raise ModelVersionError("Holdout test_fraction must lie strictly in (0,1).")
    for key in ["train_rows", "test_rows"]:
        _integer(split[key], positive=True)
    _integer(split["purged_boundary_rows"])
    for key in ["train_positive_rate", "test_positive_rate"]:
        _rate(split[key])
    if (not 0 < split["train_positive_rate"] < 1 or split["test_rows"] != metrics.total_rows
            or split["test_positive_rate"] != metrics.support_positive / metrics.total_rows):
        raise ModelVersionError("Holdout class support/count does not match evaluation metrics.")
    for key, total in [("train_rows_per_symbol", split["train_rows"]),
                       ("test_rows_per_symbol", split["test_rows"]),
                       ("unlabeled_rows_removed_per_symbol", None)]:
        counts = dataset[key] if total is None else split[key]
        if (not isinstance(counts, dict) or not counts
                or any(not isinstance(s, str) or re.fullmatch(SYMBOL_PATTERN, s) is None for s in counts)):
            raise ModelVersionError("Valid per-symbol training counts are required.")
        for count in counts.values():
            _integer(count)
        if total is not None and sum(counts.values()) != total:
            raise ModelVersionError("Per-symbol counts must sum to the partition row count.")
    _integer(dataset["input_rows"], positive=True)
    _integer(dataset["supervised_rows"], positive=True)
    if (dataset["input_rows"] != dataset["supervised_rows"] + sum(dataset["unlabeled_rows_removed_per_symbol"].values())
            or dataset["supervised_rows"] != split["train_rows"] + split["test_rows"] + split["purged_boundary_rows"]):
        raise ModelVersionError("Dataset and chronological partition counts do not reconcile.")
    if model_type == "logistic_regression":
        if (report["scaler"] != "StandardScaler" or report["converged"] is not True
                or not isinstance(report["iterations"], list) or not report["iterations"]):
            raise ModelVersionError("A converged Logistic Regression training report is required.")
        for iteration in report["iterations"]:
            _integer(iteration, positive=True)
    else:
        importances = report["feature_importances"]
        if (report["scaler"] is not None or not isinstance(importances, dict)
                or list(importances) != FEATURE_COLUMNS):
            raise ModelVersionError("Random Forest feature importances/scaler contract differs.")
        for value in importances.values():
            _rate(value)
        if not math.isclose(sum(importances.values()), 1.0, rel_tol=1e-9, abs_tol=1e-12):
            raise ModelVersionError("Random Forest importances must sum to one.")
        _rate(report["baseline_accuracy"])
        if (report["baseline_model_name"] != "logistic_regression_baseline"
                or report["random_state"] != report["hyperparameters"].get("random_state")
                or type(report["accuracy_delta"]) not in (int, float)
                or report["accuracy_delta"] != report["test_accuracy"] - report["baseline_accuracy"]):
            raise ModelVersionError("Random Forest comparison metadata is inconsistent.")


def training_payload(result) -> dict:
    """Snapshot the existing run; validate identity and evaluate only its supplied holdout."""
    try:
        if isinstance(result, LogisticRegressionResult):
            model_type, estimator, report_class = "logistic_regression", result.pipeline, LogisticRegressionReport
            if (not isinstance(estimator, Pipeline) or list(estimator.named_steps) != ["scaler", "classifier"]
                    or not isinstance(estimator.named_steps["scaler"], StandardScaler)
                    or not isinstance(estimator.named_steps["classifier"], LogisticRegression)):
                raise ModelVersionError("A fitted Logistic Regression pipeline is required.")
            classifier = estimator.named_steps["classifier"]
            check_is_fitted(estimator.named_steps["scaler"])
        elif isinstance(result, RandomForestResult):
            model_type, estimator, report_class = "random_forest", result.model, RandomForestReport
            if not isinstance(estimator, RandomForestClassifier):
                raise ModelVersionError("A fitted RandomForestClassifier is required.")
            classifier = estimator
        else:
            raise ModelVersionError("An existing LogisticRegressionResult or RandomForestResult is required.")
        if type(result.report) is not report_class:
            raise ModelVersionError("Training result and report model_type differ.")
        check_is_fitted(classifier)
        if (list(estimator.feature_names_in_) != FEATURE_COLUMNS
                or np.asarray(classifier.classes_).shape != (2,) or set(classifier.classes_) != {0, 1}):
            raise ModelVersionError("Fitted estimator feature/class contract differs.")
        report = asdict(result.report)
        if canonical(report["hyperparameters"]) != canonical(classifier.get_params(deep=False)):
            raise ModelVersionError("Training report hyperparameters differ from the supplied estimator.")
        if model_type == "logistic_regression":
            if report["iterations"] != classifier.n_iter_.tolist():
                raise ModelVersionError("Training report iterations differ from the supplied estimator.")
        elif report["feature_importances"] != dict(zip(FEATURE_COLUMNS, classifier.feature_importances_.tolist(), strict=True)):
            raise ModelVersionError("Training report feature importances differ from the supplied estimator.")
        metrics = evaluate_predictions(model_name=report["model_name"], predictions=result.predictions)
        validate_report(model_type, report, metrics)
        split, predictions = report["split"], result.predictions
        if (split["test_rows"] != len(predictions)
                or split["test_start"] != predictions.date.min().date().isoformat()
                or split["test_end"] != predictions.date.max().date().isoformat()
                or split["test_rows_per_symbol"] != predictions.groupby("symbol", sort=True).size().to_dict()):
            raise ModelVersionError("Holdout report does not describe the supplied historical predictions.")
        return {"model_name": report["model_name"], "model_type": model_type,
                "training_report": report, "metrics": asdict(metrics),
                "holdout_sha256": frame_digest(predictions)}
    except ModelVersionError:
        raise
    except (AttributeError, TypeError, ValueError, KeyError, OverflowError) as error:
        raise ModelVersionError("Invalid training result or estimator/report association.") from error
    except ModelingError as error:
        # Existing validators use their own typed errors; keep one public versioning error boundary.
        raise ModelVersionError("Training result validation/evaluation failed.") from error


def decode_record(payload: dict) -> ModelVersionRecord:
    try:
        _keys(payload, ModelVersionRecord)
        validate_name(payload["model_name"])
        validate_version(payload["model_version"])
        if version_string(payload["version_number"]) != payload["model_version"]:
            raise ModelVersionError("Version number and public identity disagree.")
        validate_uuid(payload["training_run_id"])
        timestamps = {}
        for key in ["trained_at_utc", "registered_at_utc"]:
            value = datetime.fromisoformat(payload[key])
            validate_timestamp(value)
            if payload[key] != value.isoformat():
                raise ModelVersionError("Registry timestamps must use canonical UTC ISO serialization.")
            timestamps[key] = value
        if timestamps["registered_at_utc"] < timestamps["trained_at_utc"]:
            raise ModelVersionError("Registration timestamp precedes training completion.")
        _keys(payload["metrics"], ModelEvaluationMetrics)
        metrics = ModelEvaluationMetrics(**payload["metrics"])
        validate_report(payload["model_type"], payload["training_report"], metrics)
        if payload["training_report"]["model_name"] != payload["model_name"]:
            raise ModelVersionError("Record and training report model_name differ.")
        if not isinstance(payload["holdout_sha256"], str) or re.fullmatch(r"[0-9a-f]{64}", payload["holdout_sha256"]) is None:
            raise ModelVersionError("Invalid holdout prediction digest.")
        return ModelVersionRecord(**(payload | timestamps | {"status": ModelStatus(payload["status"]), "metrics": metrics}))
    except ModelVersionError:
        raise
    except (TypeError, ValueError, KeyError, OverflowError) as error:
        raise ModelVersionError("Invalid model version record; no automatic registry repair.") from error
