"""Real forests must reuse the baseline holdout and never fit test observations."""

from dataclasses import replace

import numpy as np
import pandas as pd
import pytest
from sklearn.ensemble import RandomForestClassifier
from sklearn.pipeline import Pipeline

from stocklab_ml.features import FEATURE_COLUMNS
from stocklab_ml.modeling import (
    ModelDatasetError, ModelTrainingError, build_supervised_dataset,
    chronological_holdout_split, predict_random_forest_direction,
    train_logistic_regression, train_random_forest,
)
from stocklab_ml.modeling import random_forest
from stocklab_ml.modeling.contracts import PREDICTION_TIMING, SPLIT_RULE, TARGET_DEFINITION, TARGET_NAME
from test_model_dataset import feature_frame

IDENTIFIERS = ["date", "target_date", "symbol", "actual_class"]


@pytest.fixture(scope="module")
def forest():
    return train_random_forest(feature_frame())


@pytest.mark.parametrize("fraction", [0.2, 0.3])
def test_same_sparse_multi_symbol_holdout_labels_and_baseline_accuracy(fraction):
    aapl = feature_frame()
    msft = feature_frame(symbol="MSFT").iloc[::3].reset_index(drop=True)
    frame = pd.concat([aapl, msft], ignore_index=True)
    before = frame.copy(deep=True)
    logistic = train_logistic_regression(frame, test_fraction=fraction)
    forest = train_random_forest(frame, test_fraction=fraction)
    assert forest.report.split == logistic.report.split
    assert forest.report.dataset == logistic.report.dataset
    pd.testing.assert_frame_equal(forest.predictions[IDENTIFIERS], logistic.predictions[IDENTIFIERS])
    assert forest.report.baseline_model_name == logistic.report.model_name
    assert forest.report.baseline_accuracy == logistic.report.test_accuracy
    assert forest.report.accuracy_delta == forest.report.test_accuracy - logistic.report.test_accuracy
    assert forest.report.test_accuracy == (forest.predictions.actual_class == forest.predictions.predicted_class).mean()
    assert forest.report.target_name == TARGET_NAME
    assert forest.report.target_definition == TARGET_DEFINITION
    assert forest.report.prediction_timing == PREDICTION_TIMING
    assert forest.report.split_rule == SPLIT_RULE
    pd.testing.assert_frame_equal(frame, before)


def test_real_classifier_fixed_configuration_no_scaler_and_feature_importances(forest):
    assert isinstance(forest.model, RandomForestClassifier)
    assert not isinstance(forest.model, Pipeline)
    expected = dict(n_estimators=300, criterion="gini", max_depth=None,
                    min_samples_split=2, min_samples_leaf=1, max_features="sqrt",
                    bootstrap=True, class_weight=None, random_state=42, n_jobs=1)
    for name, value in expected.items():
        assert forest.model.get_params()[name] == value
        assert forest.report.hyperparameters[name] == value
    assert len(forest.model.estimators_) == 300
    assert forest.report.random_state == 42 and forest.report.scaler is None
    assert forest.report.model_name == "random_forest_baseline"
    assert forest.report.feature_columns == FEATURE_COLUMNS
    assert forest.model.feature_names_in_.tolist() == FEATURE_COLUMNS
    importance = forest.report.feature_importances
    assert list(importance) == FEATURE_COLUMNS
    assert all(np.isfinite(value) and value >= 0 for value in importance.values())
    assert sum(importance.values()) == pytest.approx(1.0)
    np.testing.assert_array_equal(list(importance.values()), forest.model.feature_importances_)


def test_repeated_training_is_deterministic(forest):
    again = train_random_forest(feature_frame())
    pd.testing.assert_frame_equal(forest.predictions, again.predictions, check_exact=True)
    assert forest.report == again.report


def test_fit_receives_exact_unscaled_train_features_and_integer_labels(monkeypatch):
    frame = feature_frame()
    split = chronological_holdout_split(build_supervised_dataset(frame).dataframe)
    original = RandomForestClassifier.fit
    seen = []

    def inspect(self, x, y, **kwargs):
        pd.testing.assert_frame_equal(x, split.train[FEATURE_COLUMNS])
        pd.testing.assert_series_equal(y, split.train[TARGET_NAME])
        assert y.dtype == np.dtype("int64") and set(y) == {0, 1}
        assert not set(x.columns) & {"date", "symbol", "open", "high", "low", "close", "next_close", "target_date", TARGET_NAME}
        seen.append(len(x))
        return original(self, x, y, **kwargs)

    monkeypatch.setattr(RandomForestClassifier, "fit", inspect)
    train_random_forest(frame)
    assert seen == [len(split.train)]


def test_test_only_extremes_cannot_change_any_fitted_tree(forest):
    frame = feature_frame()
    boundary = pd.Timestamp(forest.report.split.split_date)
    frame.loc[frame.date >= boundary, "volume"] = 1_000_000_000
    frame.loc[frame.date >= boundary, "ma_5"] = 1_000_000.0
    changed = train_random_forest(frame)
    for original_tree, changed_tree in zip(forest.model.estimators_, changed.model.estimators_, strict=True):
        left, right = original_tree.tree_.__getstate__(), changed_tree.tree_.__getstate__()
        assert left["node_count"] == right["node_count"]
        assert left["max_depth"] == right["max_depth"]
        np.testing.assert_array_equal(left["nodes"], right["nodes"])
        np.testing.assert_array_equal(left["values"], right["values"])
    probe = feature_frame().iloc[:10][FEATURE_COLUMNS]
    np.testing.assert_array_equal(forest.model.predict_proba(probe), changed.model.predict_proba(probe))


def test_inference_matches_estimator_without_target_or_refit(forest, monkeypatch):
    rows = feature_frame().iloc[-4:][["date", "symbol"] + list(reversed(FEATURE_COLUMNS))]
    before = rows.copy(deep=True)

    def forbidden(*args, **kwargs):
        raise AssertionError("Inference must not fit")

    monkeypatch.setattr(RandomForestClassifier, "fit", forbidden)
    output = predict_random_forest_direction(forest.model, rows)
    assert list(output) == ["date", "symbol", "predicted_class", "probability_up"]
    assert output.predicted_class.dtype == np.dtype("int64")
    assert output.probability_up.dtype == np.dtype("float64")
    assert output.date.tolist() == rows.date.tolist()
    assert np.isfinite(output.probability_up).all() and output.probability_up.between(0, 1).all()
    np.testing.assert_array_equal(output.predicted_class, forest.model.predict(rows[FEATURE_COLUMNS]))
    index = forest.model.classes_.tolist().index(1)
    np.testing.assert_array_equal(output.probability_up, forest.model.predict_proba(rows[FEATURE_COLUMNS])[:, index])
    pd.testing.assert_frame_equal(rows, before)


def test_class_one_probability_is_looked_up_explicitly(forest, monkeypatch):
    monkeypatch.setattr(forest.model, "classes_", np.array([1, 0]))
    monkeypatch.setattr(forest.model, "predict_proba", lambda x: np.tile([0.75, 0.25], (len(x), 1)))
    output = predict_random_forest_direction(forest.model, feature_frame().tail(2))
    assert output.probability_up.tolist() == [0.75, 0.75]


@pytest.mark.parametrize("failure", ["split_date", "count", "date", "target_date", "symbol", "actual_class", "order"])
def test_comparison_refuses_different_holdout(failure, monkeypatch):
    frame = feature_frame()
    baseline = train_logistic_regression(frame)
    predictions = baseline.predictions.copy(deep=True)
    report = baseline.report
    if failure == "split_date":
        report = replace(report, split=replace(report.split, split_date="2000-01-01"))
    elif failure == "count":
        predictions = predictions.iloc[:-1].reset_index(drop=True)
    elif failure in {"date", "target_date"}:
        predictions.loc[0, failure] += pd.Timedelta(days=1)
    elif failure == "symbol":
        predictions.loc[0, "symbol"] = "MSFT"
    elif failure == "actual_class":
        predictions.loc[0, "actual_class"] = 1 - predictions.loc[0, "actual_class"]
    else:
        predictions = predictions.iloc[::-1].reset_index(drop=True)
    mismatched = replace(baseline, predictions=predictions, report=report)
    monkeypatch.setattr(random_forest, "train_logistic_regression", lambda *args, **kwargs: mismatched)
    with pytest.raises(ModelTrainingError, match="holdout"):
        train_random_forest(frame)


@pytest.mark.parametrize("bad", ["missing", "nan", "inf", "string", "bool", "date", "empty"])
def test_inference_reuses_feature_validation(forest, bad):
    rows = feature_frame().tail(2).copy()
    if bad == "missing":
        rows = rows.drop(columns="ma_5")
    elif bad in {"nan", "inf"}:
        rows.iloc[0, rows.columns.get_loc("ma_5")] = np.nan if bad == "nan" else np.inf
    elif bad in {"string", "bool"}:
        rows["ma_5"] = rows.ma_5.astype(str if bad == "string" else bool)
    elif bad == "date":
        rows["date"] = rows.date.astype(str)
    else:
        rows = rows.iloc[:0]
    with pytest.raises(ModelDatasetError):
        predict_random_forest_direction(forest.model, rows)


@pytest.mark.parametrize("bad", ["nan", "negative", "above_one", "wrong_count", "wrong_width", "classes", "features"])
def test_invalid_model_outputs_fail_clearly(forest, monkeypatch, bad):
    rows = feature_frame().tail(2)
    probabilities = np.tile([0.4, 0.6], (2, 1))
    if bad in {"nan", "negative", "above_one"}:
        probabilities[:, 1] = {"nan": np.nan, "negative": -0.1, "above_one": 1.1}[bad]
    elif bad == "wrong_count":
        probabilities = probabilities[:1]
    elif bad == "wrong_width":
        probabilities = probabilities[:, :1]
    elif bad == "classes":
        monkeypatch.setattr(forest.model, "classes_", np.array([0, 2]))
    else:
        monkeypatch.setattr(forest.model, "feature_names_in_", np.array(list(reversed(FEATURE_COLUMNS))))
    monkeypatch.setattr(forest.model, "predict", lambda x: np.ones(len(x), dtype="int64"))
    monkeypatch.setattr(forest.model, "predict_proba", lambda x: probabilities)
    with pytest.raises(ModelTrainingError):
        predict_random_forest_direction(forest.model, rows)


def test_unfitted_forest_has_clear_error():
    with pytest.raises(ModelTrainingError, match="fitted"):
        predict_random_forest_direction(RandomForestClassifier(), feature_frame())


def test_single_train_class_reuses_existing_error():
    with pytest.raises(ModelTrainingError, match="class"):
        train_random_forest(feature_frame(list(range(100, 140))))


def test_single_class_test_is_allowed():
    result = train_random_forest(feature_frame([100, 102, 101, 103, 102, 104, 103, 105, 106, 107, 108]))
    assert result.report.split.test_positive_rate == 1
    assert np.isfinite(result.report.test_accuracy)


def test_undefined_importances_are_not_fabricated():
    frame = feature_frame()
    for column in FEATURE_COLUMNS:
        frame[column] = frame[column].iloc[0]
    frame["volume"] = frame.volume.astype("Int64")
    with pytest.raises(ModelTrainingError, match="importance"):
        train_random_forest(frame)
