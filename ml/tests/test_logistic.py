"""Real sklearn training, holdout isolation, and target-free inference."""

import warnings

import numpy as np
import pandas as pd
import pytest
from sklearn.exceptions import ConvergenceWarning
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler

from stocklab_ml.features import FEATURE_COLUMNS
from stocklab_ml.modeling import (
    ModelDatasetError, ModelTrainingError, build_supervised_dataset,
    chronological_holdout_split, predict_direction, train_logistic_regression,
)
from test_model_dataset import feature_frame


def test_real_fit_accuracy_determinism_and_immutable_input():
    frame = pd.concat([feature_frame(), feature_frame(symbol="MSFT")], ignore_index=True)
    before = frame.copy(deep=True)
    with warnings.catch_warnings():
        warnings.simplefilter("error", ConvergenceWarning)
        result = train_logistic_regression(frame)
        again = train_logistic_regression(frame)
    assert isinstance(result.pipeline, Pipeline)
    assert isinstance(result.pipeline.named_steps["scaler"], StandardScaler)
    model = result.pipeline.named_steps["classifier"]
    assert isinstance(model, LogisticRegression)
    assert model.classes_.tolist() == [0, 1]
    assert result.report.converged
    assert result.report.iterations == model.n_iter_.tolist()
    pred = result.predictions
    assert list(pred) == ["date", "target_date", "symbol", "actual_class", "predicted_class", "probability_up"]
    assert set(pred.predicted_class) <= {0, 1}
    assert pred.predicted_class.dtype == np.dtype("int64")
    assert np.isfinite(pred.probability_up).all() and pred.probability_up.between(0, 1).all()
    assert len(pred) == result.report.split.test_rows
    assert result.report.test_accuracy == (pred.actual_class == pred.predicted_class).mean()
    pd.testing.assert_frame_equal(frame, before)
    pd.testing.assert_frame_equal(pred, again.predictions)
    assert result.report == again.report


def test_scaler_and_estimator_fit_only_train_with_extreme_test_values():
    frame = feature_frame()
    split = chronological_holdout_split(build_supervised_dataset(frame).dataframe)
    boundary = pd.Timestamp(split.report.split_date)
    frame.loc[frame.date >= boundary, "volume"] = 1_000_000
    frame.loc[frame.date >= boundary, "ma_5"] = 1_000_000.0
    trained = train_logistic_regression(frame)
    scaler = trained.pipeline.named_steps["scaler"]
    np.testing.assert_allclose(scaler.mean_, split.train[FEATURE_COLUMNS].mean().to_numpy(dtype=float))
    assert not np.allclose(scaler.mean_, frame[FEATURE_COLUMNS].mean().to_numpy(dtype=float))
    original = train_logistic_regression(feature_frame())
    for attribute in ["mean_", "scale_", "var_"]:
        np.testing.assert_array_equal(getattr(scaler, attribute), getattr(original.pipeline.named_steps["scaler"], attribute))
    for attribute in ["coef_", "intercept_", "n_iter_"]:
        np.testing.assert_array_equal(getattr(trained.pipeline.named_steps["classifier"], attribute),
                                      getattr(original.pipeline.named_steps["classifier"], attribute))


def test_exact_training_matrix_and_inference_consistency(monkeypatch):
    frame = feature_frame()
    fit = Pipeline.fit
    seen = []

    def inspect_fit(self, x, y, *args, **kwargs):
        seen.append(list(x.columns))
        assert list(x.columns) == FEATURE_COLUMNS
        assert not set(x.columns) & {"date", "symbol", "open", "high", "low", "close", "next_close", "target_date", "target_up_1d"}
        assert y.dtype == np.dtype("int64") and set(y) == {0, 1}
        return fit(self, x, y, *args, **kwargs)

    monkeypatch.setattr(Pipeline, "fit", inspect_fit)
    trained = train_logistic_regression(frame)
    rows = frame.iloc[-4:][["date", "symbol"] + list(reversed(FEATURE_COLUMNS))].copy()
    before = rows.copy(deep=True)
    inferred = predict_direction(trained.pipeline, rows)
    np.testing.assert_array_equal(inferred.predicted_class, trained.pipeline.predict(rows[FEATURE_COLUMNS]))
    classes = trained.pipeline.named_steps["classifier"].classes_.tolist()
    np.testing.assert_allclose(inferred.probability_up, trained.pipeline.predict_proba(rows[FEATURE_COLUMNS])[:, classes.index(1)])
    assert inferred.date.tolist() == rows.date.tolist()
    pd.testing.assert_frame_equal(rows, before)
    assert seen == [FEATURE_COLUMNS]  # inference did not fit anything


def test_probability_column_is_looked_up_by_class(monkeypatch):
    trained = train_logistic_regression(feature_frame())
    rows = feature_frame().iloc[-2:]
    model = trained.pipeline.named_steps["classifier"]
    model.classes_ = np.array([1, 0])
    monkeypatch.setattr(trained.pipeline, "predict_proba", lambda x: np.tile([0.8, 0.2], (len(x), 1)))
    assert predict_direction(trained.pipeline, rows).probability_up.tolist() == [0.8, 0.8]


def test_single_class_test_is_allowed():
    frame = feature_frame([100, 102, 101, 103, 102, 104, 103, 105, 106, 107, 108])
    result = train_logistic_regression(frame)
    assert result.report.split.test_positive_rate == 1
    assert np.isfinite(result.report.test_accuracy)


def test_nonconvergence_is_a_training_error(monkeypatch):
    original = LogisticRegression.fit

    def one_iteration(self, x, y, **kwargs):
        self.max_iter = 1
        return original(self, x, y, **kwargs)

    monkeypatch.setattr(LogisticRegression, "fit", one_iteration)
    with pytest.raises(ModelTrainingError, match="converge"):
        train_logistic_regression(feature_frame())


@pytest.mark.parametrize("bad", ["missing", "nan", "inf", "string", "bool", "complex", "date", "symbol", "duplicate", "empty"])
def test_inference_rejects_invalid_rows(bad):
    trained = train_logistic_regression(feature_frame())
    rows = feature_frame().iloc[-2:][["date", "symbol"] + FEATURE_COLUMNS].copy()
    if bad == "missing":
        rows = rows.drop(columns="return_1d")
    elif bad in {"nan", "inf"}:
        rows.iloc[0, rows.columns.get_loc("ma_5")] = np.nan if bad == "nan" else np.inf
    elif bad in {"string", "bool", "complex"}:
        rows["ma_5"] = rows.ma_5.astype({"string": str, "bool": bool, "complex": complex}[bad])
    elif bad == "date":
        rows["date"] = rows.date.astype(str)
    elif bad == "symbol":
        rows["symbol"] = ""
    elif bad == "duplicate":
        rows = pd.concat([rows, rows.iloc[:1]])
    else:
        rows = rows.iloc[:0]
    with pytest.raises(ModelDatasetError):
        predict_direction(trained.pipeline, rows)


def test_unfitted_pipeline_has_clear_error():
    pipeline = Pipeline([("scaler", StandardScaler()), ("classifier", LogisticRegression())])
    with pytest.raises(ModelTrainingError, match="fitted"):
        predict_direction(pipeline, feature_frame())
