"""Hand-computed classification metrics and strict, immutable holdout comparison."""

from dataclasses import asdict, replace
import warnings

import numpy as np
import pandas as pd
import pytest
from sklearn.ensemble import RandomForestClassifier
from sklearn.linear_model import LogisticRegression

from stocklab_ml.modeling import (
    ModelEvaluationError, compare_model_results, evaluate_model_baselines,
    evaluate_predictions, train_random_forest,
)
from test_model_dataset import feature_frame


def prediction_frame(actual, predicted):
    dates = pd.date_range("2024-01-01", periods=len(actual), freq="B").as_unit("ns")
    return pd.DataFrame({
        "date": dates, "target_date": dates + pd.offsets.BDay(1),
        "symbol": pd.array(["AAPL"] * len(actual), dtype="string"),
        "actual_class": np.array(actual, dtype="int64"),
        "predicted_class": np.array(predicted, dtype="int64"),
        # Deliberately unrelated to predicted_class: evaluation must not threshold.
        "probability_up": np.full(len(actual), 0.1),
    })


@pytest.mark.parametrize("actual,predicted,counts,expected", [
    ([1, 1, 1, 0, 0, 0], [1, 1, 0, 1, 0, 0], (2, 2, 1, 1), (4/6, 2/3, 2/3, 2/3)),
    ([1, 1, 1, 1, 0, 0], [1, 1, 0, 0, 1, 0], (2, 1, 1, 2), (3/6, 2/3, 1/2, 4/7)),
    ([1, 0, 1, 0], [1, 0, 1, 0], (2, 2, 0, 0), (1, 1, 1, 1)),
    ([1, 0, 1, 0], [0, 1, 0, 1], (0, 0, 2, 2), (0, 0, 0, 0)),
    ([1, 0, 1, 0], [0, 0, 0, 0], (0, 2, 0, 2), (1/2, 0, 0, 0)),
    ([0, 0, 0], [0, 0, 0], (0, 3, 0, 0), (1, 0, 0, 0)),
    ([0, 0, 0], [1, 1, 0], (0, 1, 2, 0), (1/3, 0, 0, 0)),
    ([1, 1, 1], [1, 1, 1], (3, 0, 0, 0), (1, 1, 1, 1)),
    ([1, 1, 1], [0, 0, 0], (0, 0, 0, 3), (0, 0, 0, 0)),
])
def test_manual_metrics_confusion_orientation_and_zero_division(actual, predicted, counts, expected):
    frame = prediction_frame(actual, predicted)
    before = frame.copy(deep=True)
    with warnings.catch_warnings():
        warnings.simplefilter("error")
        metrics = evaluate_predictions(model_name="future_classifier", predictions=frame)
    assert metrics.model_name == "future_classifier"
    assert (metrics.true_positive, metrics.true_negative, metrics.false_positive, metrics.false_negative) == counts
    assert (metrics.accuracy, metrics.precision, metrics.recall, metrics.f1) == pytest.approx(expected)
    assert sum(counts) == metrics.total_rows == len(actual)
    assert metrics.support_positive == counts[0] + counts[3] == sum(actual)
    assert metrics.support_negative == counts[1] + counts[2]
    assert all(np.isfinite(value) and 0 <= value <= 1 for value in expected)
    assert evaluate_predictions(model_name="future_classifier", predictions=frame) == metrics
    pd.testing.assert_frame_equal(frame, before)


@pytest.mark.parametrize("bad", [
    "not_frame", "empty", "missing", "extra", "duplicate_column", "duplicate_key",
    "actual_value", "predicted_value", "actual_float", "predicted_bool", "actual_string",
    "actual_null", "predicted_null", "prob_nan", "prob_inf", "prob_negative", "prob_high",
    "prob_string", "prob_bool", "prob_complex", "date_string", "date_null", "date_timezone",
    "date_intraday", "target_string", "target_null", "target_equal", "target_before",
    "symbol_empty", "symbol_null", "symbol_number", "symbol_lowercase",
])
def test_invalid_predictions_raise_evaluation_error(bad):
    frame = prediction_frame([1, 0, 1], [1, 0, 0])
    if bad == "not_frame":
        frame = []
    elif bad == "empty":
        frame = frame.iloc[:0]
    elif bad == "missing":
        frame = frame.drop(columns="probability_up")
    elif bad == "extra":
        frame["unrelated"] = 1
    elif bad == "duplicate_column":
        frame = pd.concat([frame, frame[["date"]]], axis=1)
    elif bad == "duplicate_key":
        frame = pd.concat([frame, frame.iloc[:1]], ignore_index=True)
    elif bad in {"actual_value", "predicted_value"}:
        frame.loc[0, "actual_class" if bad.startswith("actual") else "predicted_class"] = 2
    elif bad in {"actual_float", "predicted_bool", "actual_string"}:
        column, dtype = {"actual_float": ("actual_class", float), "predicted_bool": ("predicted_class", bool),
                         "actual_string": ("actual_class", str)}[bad]
        frame[column] = frame[column].astype(dtype)
    elif bad in {"actual_null", "predicted_null"}:
        column = "actual_class" if bad.startswith("actual") else "predicted_class"
        frame[column] = frame[column].astype("Int64")
        frame.loc[0, column] = pd.NA
    elif bad.startswith("prob_"):
        values = {"prob_nan": np.nan, "prob_inf": np.inf, "prob_negative": -0.1, "prob_high": 1.1}
        if bad in values:
            frame.loc[0, "probability_up"] = values[bad]
        else:
            frame["probability_up"] = frame.probability_up.astype({"prob_string": str, "prob_bool": bool, "prob_complex": complex}[bad])
    elif bad in {"date_string", "target_string"}:
        column = "date" if bad.startswith("date") else "target_date"
        frame[column] = frame[column].astype(str)
    elif bad in {"date_null", "target_null"}:
        frame.loc[0, "date" if bad.startswith("date") else "target_date"] = pd.NaT
    elif bad == "date_timezone":
        frame["date"] = frame.date.dt.tz_localize("UTC")
    elif bad == "date_intraday":
        frame.loc[0, "date"] += pd.Timedelta(hours=1)
    elif bad in {"target_equal", "target_before"}:
        frame.loc[0, "target_date"] = frame.date.iloc[0] - pd.Timedelta(days=int(bad == "target_before"))
    else:
        frame["symbol"] = {"symbol_empty": "", "symbol_null": pd.NA, "symbol_number": 12, "symbol_lowercase": "aapl"}[bad]
    with pytest.raises(ModelEvaluationError):
        evaluate_predictions(model_name="classifier", predictions=frame)


@pytest.mark.parametrize("name", [None, "", "  ", 42])
def test_model_name_is_required(name):
    with pytest.raises(ModelEvaluationError):
        evaluate_predictions(model_name=name, predictions=prediction_frame([1, 0], [1, 0]))


@pytest.fixture(scope="module")
def baseline_pair():
    features = pd.concat([feature_frame(), feature_frame(symbol="MSFT")], ignore_index=True)
    forest = train_random_forest(features)
    return forest.baseline_result, forest


def test_comparison_is_deterministic_immutable_and_never_fits(baseline_pair, monkeypatch):
    logistic, forest = baseline_pair
    snapshots = [result.predictions.copy(deep=True) for result in baseline_pair]
    original_reports = [asdict(result.report) for result in baseline_pair]

    def forbidden(*args, **kwargs):
        raise AssertionError("Evaluation must not fit a model")

    monkeypatch.setattr(LogisticRegression, "fit", forbidden)
    monkeypatch.setattr(RandomForestClassifier, "fit", forbidden)
    report = compare_model_results(logistic, forest)
    assert [metric.model_name for metric in report.models] == ["logistic_regression_baseline", "random_forest_baseline"]
    assert report.positive_class == 1 and report.zero_division == 0
    assert report.test_rows == logistic.report.split.test_rows
    assert report.test_start == logistic.report.split.test_start
    assert report.test_end == logistic.report.split.test_end
    for metrics, result in zip(report.models, baseline_pair, strict=True):
        assert metrics.accuracy == result.report.test_accuracy
        assert metrics.total_rows == metrics.true_positive + metrics.true_negative + metrics.false_positive + metrics.false_negative
    for metric in ["accuracy", "precision", "recall", "f1"]:
        assert report.metric_deltas[metric + "_delta"] == getattr(report.models[1], metric) - getattr(report.models[0], metric)
        assert -1 <= report.metric_deltas[metric + "_delta"] <= 1
    assert report == compare_model_results(logistic, forest)
    for result, before, original_report in zip(baseline_pair, snapshots, original_reports, strict=True):
        pd.testing.assert_frame_equal(result.predictions, before)
        assert asdict(result.report) == original_report


@pytest.mark.parametrize("field,value", [
    ("split_date", "2024-01-01"), ("test_fraction", 0.3), ("test_rows", 999),
    ("train_rows", 999), ("purged_boundary_rows", 99), ("train_start", "2000-01-01"),
    ("train_end", "2000-01-02"), ("test_start", "2000-01-03"), ("test_end", "2000-01-04"),
    ("train_positive_rate", 0.123), ("test_positive_rate", 0.123),
    ("train_rows_per_symbol", {}), ("test_rows_per_symbol", {}),
])
def test_any_holdout_metadata_difference_fails(baseline_pair, field, value):
    logistic, forest = baseline_pair
    report = replace(forest.report, split=replace(forest.report.split, **{field: value}))
    with pytest.raises(ModelEvaluationError, match="holdout"):
        compare_model_results(logistic, replace(forest, report=report))


@pytest.mark.parametrize("field,value", [
    ("target_name", "different"), ("target_definition", "different"),
    ("feature_columns", ["close"]), ("prediction_timing", "before close"),
    ("split_rule", "different"),
])
def test_contract_mismatches_fail(baseline_pair, field, value):
    logistic, forest = baseline_pair
    with pytest.raises(ModelEvaluationError, match="contract"):
        compare_model_results(logistic, replace(forest, report=replace(forest.report, **{field: value})))


@pytest.mark.parametrize("change", ["date", "target_date", "symbol", "actual_class", "order"])
def test_ordered_identifiers_and_actual_labels_must_match(baseline_pair, change):
    logistic, forest = baseline_pair
    predictions = forest.predictions.copy(deep=True)
    if change == "date":
        predictions.loc[0, "date"] -= pd.Timedelta(days=1)
    elif change == "target_date":
        predictions.loc[0, "target_date"] += pd.Timedelta(days=1)
    elif change == "symbol":
        predictions.loc[0, "symbol"] = "NVDA"
    elif change == "actual_class":
        predictions.loc[0, "actual_class"] = 1 - predictions.loc[0, "actual_class"]
    else:
        predictions = predictions.iloc[::-1].reset_index(drop=True)
    with pytest.raises(ModelEvaluationError, match="holdout"):
        compare_model_results(logistic, replace(forest, predictions=predictions))


@pytest.mark.parametrize("which", ["logistic", "forest"])
def test_report_accuracy_must_match_predictions(baseline_pair, which):
    logistic, forest = baseline_pair
    if which == "logistic":
        logistic = replace(logistic, report=replace(logistic.report, test_accuracy=0.12345))
    else:
        forest = replace(forest, report=replace(forest.report, test_accuracy=0.12345))
    with pytest.raises(ModelEvaluationError, match="accuracy"):
        compare_model_results(logistic, forest)


@pytest.mark.parametrize("field,value", [
    ("test_rows", 999), ("test_start", "2000-01-01"), ("test_end", "2000-01-01"),
    ("test_positive_rate", 0.123), ("test_rows_per_symbol", {}), ("test_fraction", np.nan),
    ("split_date", "2026-01-01"),
])
def test_matching_but_false_holdout_metadata_is_rejected(baseline_pair, field, value):
    logistic, forest = baseline_pair
    split = replace(logistic.report.split, **{field: value})
    logistic = replace(logistic, report=replace(logistic.report, split=split))
    forest = replace(forest, report=replace(forest.report, split=split))
    with pytest.raises(ModelEvaluationError):
        compare_model_results(logistic, forest)


def test_end_to_end_fits_each_estimator_once_and_preserves_input(baseline_pair, monkeypatch):
    counts = {"logistic": 0, "forest": 0}
    logistic_fit, forest_fit = LogisticRegression.fit, RandomForestClassifier.fit

    def fit_logistic(self, *args, **kwargs):
        counts["logistic"] += 1
        return logistic_fit(self, *args, **kwargs)

    def fit_forest(self, *args, **kwargs):
        counts["forest"] += 1
        return forest_fit(self, *args, **kwargs)

    monkeypatch.setattr(LogisticRegression, "fit", fit_logistic)
    monkeypatch.setattr(RandomForestClassifier, "fit", fit_forest)
    frame = pd.concat([feature_frame(), feature_frame(symbol="MSFT")], ignore_index=True)
    before = frame.copy(deep=True)
    comparison = evaluate_model_baselines(frame)
    assert counts == {"logistic": 1, "forest": 1}
    assert comparison == compare_model_results(*baseline_pair)
    pd.testing.assert_frame_equal(frame, before)
