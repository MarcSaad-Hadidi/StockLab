"""Risk-only isotonic fits retain causal partitions and the original benchmark."""

import numpy as np
import pandas as pd
import pytest

from stocklab_ml import swing_research as swing


LABEL, KNOWN = "risk_label", "risk_known"


def predict(evaluation, panel, *, kind="calibrated_boosted", weighted=True):
    return swing._walk_forward_predict(evaluation, "isotonic_boosted_risk", "2024-01-02", "2024-01-03",
        kind, LABEL, KNOWN, ["risk_feature"], "probability_downside",
        training_frame=panel, date_weighted=weighted)


def partition_panel():
    rows = [
        ("2019-12-31", "EXPIRED", 1., "2020-01-30"),
        ("2020-01-02", "AAA", 0., "2020-01-30"),
        ("2022-11-01", "AAA", 1., "2022-11-29"),
        ("2022-11-01", "BBB", 1., "2022-11-29"),
        ("2022-11-02", "AAA", 0., "2022-11-30"),
        ("2022-11-02", "BBB", 0., "2022-11-30"),
        ("2022-11-02", "CCC", 1., "2022-11-30"),
        ("2022-12-01", "CROSS", 1., "2023-01-02"),
        ("2023-01-02", "AAA", 0., "2023-01-30"),
        ("2023-02-01", "AAA", 1., "2023-03-01"),
        ("2023-02-01", "BBB", 0., "2023-03-01"),
        ("2023-02-01", "CCC", 1., "2023-03-01"),
        ("2023-02-02", "AAA", 1., "2023-03-02"),
        ("2023-02-02", "BBB", 1., "2023-03-02"),
        ("2023-12-01", "BOUNDARY", 1., "2024-01-02"),
        ("2023-12-01", "FUTURE", 0., "2024-01-03"),
        ("2023-12-04", "CENSORED", np.nan, None),
        ("2023-12-05", "UNKNOWN", np.nan, "2023-12-29"),
    ]
    frame = pd.DataFrame(rows, columns=["date", "symbol", LABEL, KNOWN])
    frame = frame.astype({"date": "datetime64[ns]", KNOWN: "datetime64[ns]"})
    frame["risk_feature"] = np.sin(np.arange(len(frame)))
    return frame


@pytest.fixture
def observed_fits(monkeypatch):
    calls = {"base": [], "calibration": []}
    base_fit, calibration_fit = swing.HistGradientBoostingClassifier.fit, swing.CalibratedClassifierCV.fit

    def fit_base(model, x, y, **kwargs):
        calls["base"].append((model, x.copy(), y.copy(), kwargs.copy()))
        return base_fit(model, x, y, **kwargs)

    def fit_calibration(model, x, y, **kwargs):
        calls["calibration"].append((model, x.copy(), y.copy(), kwargs.copy()))
        return calibration_fit(model, x, y, **kwargs)

    monkeypatch.setattr(swing.HistGradientBoostingClassifier, "fit", fit_base)
    monkeypatch.setattr(swing.CalibratedClassifierCV, "fit", fit_calibration)
    return calls


def evaluation():
    return pd.DataFrame({"date": pd.to_datetime(["2024-01-02", "2024-01-03"]),
                         "symbol": ["AAPL", "AAPL"], "risk_feature": [-.5, .5]})


def test_base_and_calibration_use_separate_purged_date_weights_and_original_reference(observed_fits):
    panel = partition_panel()
    scores, (audit,) = predict(evaluation(), panel)
    assert scores[["date", "symbol"]].equals(evaluation()[["date", "symbol"]])
    assert len(observed_fits["base"]) == len(observed_fits["calibration"]) == 1
    model, base, base_labels, base_options = observed_fits["base"][0]
    calibrated, calibration, calibration_labels, calibration_options = observed_fits["calibration"][0]
    assert base.index.tolist() == [1, 2, 3, 4, 5, 6]
    assert calibration.index.tolist() == [8, 9, 10, 11, 12, 13]
    assert base_labels.dtype == calibration_labels.dtype == np.dtype("int64")
    np.testing.assert_allclose(base_options["sample_weight"], [2., 1., 1., 2 / 3, 2 / 3, 2 / 3])
    np.testing.assert_allclose(calibration_options["sample_weight"], [2., 2 / 3, 2 / 3, 2 / 3, 1., 1.])
    assert calibrated.method == "isotonic"
    assert isinstance(calibrated.estimator, swing.FrozenEstimator)
    assert calibrated.estimator.estimator is model
    assert {key: model.get_params()[key] for key in ("max_iter", "learning_rate", "max_leaf_nodes",
        "l2_regularization", "early_stopping", "random_state")} == {
        "max_iter": 200, "learning_rate": .05, "max_leaf_nodes": 15,
        "l2_regularization": 1., "early_stopping": False, "random_state": 42}
    assert audit["training_positive_rate"] == pytest.approx(4 / 9)
    assert audit["training_unweighted_positive_rate"] == .5
    assert (audit["training_rows"], audit["training_dates"], audit["training_identities"]) == (6, 3, 3)
    expected = {"forecast_reference_positive_rate": 4 / 7,
        "forecast_reference_unweighted_positive_rate": 8 / 13,
        "forecast_reference_first_fit": "2020-01-02", "forecast_reference_last_fit": "2023-02-02",
        "forecast_reference_last_target": "2023-03-02", "forecast_reference_rows": 13,
        "forecast_reference_dates": 7, "forecast_reference_identities": 4,
        "forecast_reference_date_weighted": True, "base_last_target": "2022-11-30",
        "calibration_start": "2023-01-02", "calibration_last_fit": "2023-02-02",
        "calibration_last_target": "2023-03-02", "last_target": "2023-03-02",
        "calibration_method": "isotonic", "calibration_rows": 6, "calibration_dates": 3,
        "calibration_identities": 3, "calibration_date_weighted": True,
        "calibration_unweighted_positive_rate": 2 / 3, "calibration_weighted_positive_rate": 5 / 9,
        "calibration_weight_sum": 6., "calibration_weight_min": 2 / 3, "calibration_weight_max": 2.}
    for key, value in expected.items():
        assert audit[key] == (pytest.approx(value) if isinstance(value, float) else value)


@pytest.mark.parametrize("boundary", ["base", "prediction"])
def test_excluded_labels_cannot_change_calibrated_predictions(boundary):
    panel = partition_panel()
    first, (audit,) = predict(evaluation(), panel)
    changed = panel.copy()
    excluded = changed.symbol.eq("CROSS") if boundary == "base" else changed.symbol.isin([
        "EXPIRED", "BOUNDARY", "FUTURE"])
    changed.loc[excluded, LABEL] = 1 - changed.loc[excluded, LABEL]
    second, (repeated,) = predict(evaluation(), changed)
    pd.testing.assert_frame_equal(first, second)
    assert repeated["training_positive_rate"] == audit["training_positive_rate"]
    assert repeated["calibration_weighted_positive_rate"] == audit["calibration_weighted_positive_rate"]
    if boundary == "prediction":
        assert repeated == audit


@pytest.mark.parametrize("partition", ["base", "calibration", "empty_calibration"])
def test_each_partition_requires_both_classes_before_fitting(partition, observed_fits):
    panel = partition_panel()
    if partition == "empty_calibration":
        panel = panel.loc[panel.date.lt("2023-01-02")]
    else:
        selected = panel.date.lt("2023-01-02") if partition == "base" else panel.date.ge("2023-01-02")
        panel.loc[selected, LABEL] = 0.
    with pytest.raises(ValueError, match="calibration|Base fit"):
        predict(evaluation(), panel)
    assert observed_fits == {"base": [], "calibration": []}


def test_real_calibration_keeps_finite_nondecreasing_risk_scores(observed_fits):
    dates = pd.bdate_range("2020-01-02", "2023-12-29").astype("datetime64[ns]")
    feature = np.sin(np.arange(len(dates)) / 7)
    panel = pd.DataFrame({"date": dates, "symbol": "AAA", "risk_feature": feature,
        LABEL: (feature > .1).astype(float), KNOWN: dates + pd.offsets.BDay(20)})
    grid = pd.DataFrame({"date": pd.Timestamp("2024-01-02"),
        "symbol": [f"S{i}" for i in range(101)], "risk_feature": np.linspace(-2., 2., 101)})
    scores, _ = predict(grid, panel)
    base = observed_fits["base"][0][0]
    raw = base.predict_proba(grid[["risk_feature"]])[:, 1]
    ordered = scores.probability_downside.to_numpy()[np.argsort(raw)]
    assert np.isfinite(ordered).all() and ((ordered >= 0) & (ordered <= 1)).all()
    assert (np.diff(ordered) >= 0).all() and len(np.unique(ordered)) > 1
    assert len(observed_fits["base"]) == 1


def test_raw_boosted_defaults_keep_full_history_scores_and_audits(observed_fits):
    panel = partition_panel()
    scores, (audit,) = predict(evaluation(), panel, kind="boosted")
    model, x, labels, options = observed_fits["base"][0]
    np.testing.assert_array_equal(scores.probability_downside,
        model.predict_proba(evaluation()[["risk_feature"]])[:, 1])
    assert x.index.tolist() == list(range(1, 14))
    assert audit == {"recipe": "isotonic_boosted_risk", "label": LABEL,
        "first_fit": "2020-01-02", "last_fit": "2023-02-02", "last_target": "2023-03-02",
        "first_prediction": "2024-01-02", "train_rows": 13,
        "training_positive_rate": pytest.approx(4 / 7), "date_weighted": True,
        "training_unweighted_positive_rate": 8 / 13,
        "training_weighted_positive_rate": pytest.approx(4 / 7),
        "training_dates": 7, "training_identities": 4, "training_rows": 13}
    assert observed_fits["calibration"] == []


def test_unweighted_calibrated_fit_preserves_explicit_population_priors(observed_fits):
    _, (audit,) = predict(evaluation(), partition_panel(), weighted=False)
    assert observed_fits["base"][0][3] == {}
    assert observed_fits["calibration"][0][3] == {}
    assert audit["forecast_reference_positive_rate"] == 8 / 13
    assert audit["training_positive_rate"] == .5
    assert audit["calibration_weighted_positive_rate"] == 2 / 3
    assert audit["calibration_date_weighted"] is False
    assert (audit["calibration_weight_sum"], audit["calibration_weight_min"], audit["calibration_weight_max"]) == (6., 1., 1.)
