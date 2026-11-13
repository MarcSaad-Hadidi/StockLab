"""Separate training histories, horizon purges and causal date-balanced priors."""

import numpy as np
import pandas as pd
import pytest

from stocklab_ml import downside_research as downside, swing_research as swing


def features(rows):
    frame = pd.DataFrame(rows, columns=["date", "symbol"])
    frame["date"] = pd.to_datetime(frame.date).astype("datetime64[ns]")
    for i, column in enumerate(downside.DOWNSIDE_FEATURES):
        frame[column] = np.sin(np.arange(len(frame)) / (i + 2))
    return frame


def predict(head, frame, start="2024-01-02", end="2024-01-03", **kwargs):
    return (downside.predict(frame, start, end, **kwargs) if head == "downside"
            else swing.walk_forward_predict(frame, "rf_swing20", start, end, **kwargs))


@pytest.fixture
def fitted_rows(monkeypatch):
    calls = []
    original = swing.RandomForestClassifier.fit

    def fit(model, x, y, **kwargs):
        calls.append((x.copy(), y.copy(), kwargs.copy()))
        return original(model, x, y, **kwargs)

    monkeypatch.setattr(swing.RandomForestClassifier, "fit", fit)
    return calls


@pytest.mark.parametrize("head,expected_symbols,last_fit,last_target", [
    ("upside", ["EARLY", "AAA", "BBB", "CCC"], "2023-11-22", "2023-12-21"),
    ("downside", ["EARLY", "AAA", "BBB", "CCC", "EDGE"], "2023-12-04", "2024-01-01"),
])
def test_only_separate_training_rows_with_fully_known_head_specific_targets_fit(
        head, expected_symbols, last_fit, last_target, fitted_rows):
    panel = features([
        ("2019-12-31", "EXPIRED"), ("2020-01-02", "EARLY"),
        ("2023-11-20", "AAA"), ("2023-11-21", "BBB"), ("2023-11-22", "CCC"),
        ("2023-12-04", "EDGE"), ("2023-12-05", "FUTURE"),
        ("2023-12-06", "CENSORED"), ("2023-12-07", "UNKNOWN"),
    ])
    for label, known, endpoints in [
        ("target_20", "target_date_20", ["2020-01-31", "2020-02-04", "2023-12-19",
         "2023-12-20", "2023-12-21", "2024-01-02", "2024-01-03", None, "2023-12-29"]),
        (downside.LABEL, downside.KNOWN_DATE, ["2020-01-30", "2020-02-03", "2023-12-18",
         "2023-12-19", "2023-12-20", "2024-01-01", "2024-01-02", None, "2023-12-28"]),
    ]:
        panel[known] = pd.to_datetime(endpoints).astype("datetime64[ns]")
        panel[label] = [1., 0., 1., 0., 1., 0., 1., np.nan, np.nan]
    evaluation = features([("2024-01-02", "AAPL"), ("2024-01-03", "AAPL")])
    scores, audits = predict(head, evaluation, training_frame=panel)
    assert scores[["date", "symbol"]].equals(evaluation[["date", "symbol"]])
    assert len(fitted_rows) == 1
    expected_index = panel.index[panel.symbol.isin(expected_symbols)]
    pd.testing.assert_frame_equal(fitted_rows[0][0], panel.loc[expected_index, fitted_rows[0][0].columns])
    assert fitted_rows[0][2] == {}
    audit = audits[0]
    assert audit["first_fit"] == "2020-01-02"
    assert audit["last_fit"] == last_fit
    assert audit["last_target"] == last_target
    assert audit["train_rows"] == len(expected_symbols)
    assert audit["training_rows"] == len(expected_symbols)
    assert audit["training_dates"] == len(expected_symbols)
    assert audit["training_identities"] == len(expected_symbols)
    label = downside.LABEL if head == "downside" else "target_20"
    changed = panel.copy()
    changed.loc[~changed.index.isin(expected_index), label] = 1 - changed.loc[
        ~changed.index.isin(expected_index), label]
    repeated, repeated_audits = predict(head, evaluation, training_frame=changed)
    pd.testing.assert_frame_equal(scores, repeated)
    assert audits == repeated_audits


def uneven_panel():
    panel = features([
        ("2023-11-01", "AAA"),
        ("2023-11-02", "AAA"), ("2023-11-02", "BBB"),
        ("2023-11-03", "AAA"), ("2023-11-03", "BBB"), ("2023-11-03", "CCC"),
    ])
    for label, known in [("target_20", "target_date_20"), (downside.LABEL, downside.KNOWN_DATE)]:
        panel[known] = pd.Timestamp("2023-12-05")
        panel[label] = [0., 1., 1., 1., 0., 0.]
    return panel


@pytest.mark.parametrize("head", ["upside", "downside"])
def test_each_purged_feature_date_has_equal_weight_and_weighted_causal_prevalence(head, fitted_rows):
    panel = uneven_panel()
    excluded = panel.iloc[[0, 1]].copy()
    excluded["symbol"] = ["FUTURE", "CENSORED"]
    for label, known in [("target_20", "target_date_20"), (downside.LABEL, downside.KNOWN_DATE)]:
        excluded[known] = [pd.Timestamp("2024-01-02"), pd.NaT]
        excluded[label] = [1., np.nan]
    panel = pd.concat([panel, excluded], ignore_index=True)
    evaluation = features([("2024-01-02", "AAPL")])
    scores, audits = predict(head, evaluation, training_frame=panel, date_weighted=True)
    weights = fitted_rows[0][2]["sample_weight"]
    np.testing.assert_allclose(weights, [2., 1., 1., 2 / 3, 2 / 3, 2 / 3], rtol=0, atol=1e-15)
    assert np.mean(weights) == pytest.approx(1.)
    totals = pd.Series(np.asarray(weights), index=panel.date.iloc[:6]).groupby(level=0).sum()
    np.testing.assert_allclose(totals, [2., 2., 2.], rtol=0, atol=1e-15)
    audit = audits[0]
    assert audit["training_positive_rate"] == pytest.approx(4 / 9)
    assert audit["training_weighted_positive_rate"] == pytest.approx(4 / 9)
    assert audit["training_unweighted_positive_rate"] == .5
    assert audit["training_dates"] == 3
    assert audit["training_identities"] == 3
    assert audit["training_rows"] == 6
    assert audit["date_weighted"] is True
    panel["catalog_status"] = np.where(np.arange(len(panel)) % 2, "active", "delisted")
    repeated, repeated_audits = predict(head, evaluation, training_frame=panel, date_weighted=True)
    pd.testing.assert_frame_equal(scores, repeated)
    assert audits == repeated_audits


@pytest.mark.parametrize("head", ["upside", "downside"])
def test_unweighted_separate_panel_keeps_ordinary_fitted_prevalence(head, fitted_rows):
    _, audits = predict(head, features([("2024-01-02", "AAPL")]), training_frame=uneven_panel())
    assert fitted_rows[0][2] == {}
    for field in ("training_positive_rate", "training_unweighted_positive_rate", "training_weighted_positive_rate"):
        assert audits[0][field] == .5
    assert audits[0]["date_weighted"] is False


def test_uniform_weight_roundoff_still_preserves_original_bootstrap_path(fitted_rows):
    panel = features([(day, symbol) for day in pd.bdate_range("2023-11-01", periods=7)
                      for symbol in ("AAA", "BBB", "CCC")])
    panel[downside.KNOWN_DATE] = pd.Timestamp("2023-12-05")
    panel[downside.LABEL] = (np.arange(len(panel)) % 2).astype(float)
    evaluation = features([("2024-01-02", "AAPL")])
    original, _ = predict("downside", evaluation, training_frame=panel)
    weighted, _ = predict("downside", evaluation, training_frame=panel, date_weighted=True)
    pd.testing.assert_frame_equal(original, weighted)
    assert all(kwargs == {} for _, _, kwargs in fitted_rows)


def equal_date_frame():
    dates = pd.bdate_range("2023-10-02", "2024-02-16").astype("datetime64[ns]")
    frame = features([(day, symbol) for day in dates for symbol in ("AAPL", "MSFT")])
    for label, known, horizon in [("target_20", "target_date_20", 21),
                                  (downside.LABEL, downside.KNOWN_DATE, 20)]:
        frame[known] = frame.date + pd.offsets.BDay(horizon)
        frame[label] = (np.sin(np.arange(len(frame)) / 3) > .1).astype(float)
        frame.loc[frame[known] > frame.date.max(), [known, label]] = [pd.NaT, np.nan]
    return frame


@pytest.mark.parametrize("head", ["upside", "downside"])
@pytest.mark.parametrize("frequency", ["annual", "monthly"])
def test_explicit_no_panel_defaults_preserve_predictions_audits_and_fit_call(head, frequency, fitted_rows):
    frame = equal_date_frame()
    original, original_audits = predict(head, frame, end="2024-02-16", refit_frequency=frequency)
    explicit, explicit_audits = predict(head, frame, end="2024-02-16", refit_frequency=frequency,
                                        training_frame=None, date_weighted=False)
    pd.testing.assert_frame_equal(original, explicit)
    assert original_audits == explicit_audits
    assert all(kwargs == {} for _, _, kwargs in fitted_rows)
    assert all("training_dates" not in audit for audit in original_audits)
    assert ("training_positive_rate" in original_audits[0]) == (head == "downside")


@pytest.mark.parametrize("head", ["upside", "downside"])
@pytest.mark.parametrize("frequency", ["annual", "monthly"])
@pytest.mark.parametrize("separate", [False, True])
def test_date_balancing_equal_breadth_preserves_control_predictions(head, frequency, separate, fitted_rows):
    frame = equal_date_frame()
    original, original_audits = predict(head, frame, end="2024-02-16", refit_frequency=frequency)
    balanced, audits = predict(head, frame, end="2024-02-16", refit_frequency=frequency,
                               training_frame=frame if separate else None, date_weighted=True)
    pd.testing.assert_frame_equal(original, balanced)
    assert len(original_audits) == len(audits)
    assert all(kwargs == {} for _, _, kwargs in fitted_rows)
    for old, new in zip(original_audits, audits):
        assert all(new[key] == value for key, value in old.items())


@pytest.mark.parametrize("head", ["upside", "downside"])
@pytest.mark.parametrize("bad", ["duplicate", "nonfinite", "missing_date", "missing_symbol", "not_frame"])
def test_invalid_separate_panel_fails_before_fitting(head, bad, fitted_rows):
    panel = uneven_panel()
    if bad == "duplicate":
        panel = pd.concat([panel, panel.iloc[[0]]], ignore_index=True)
    elif bad == "nonfinite":
        panel.loc[0, swing.SWING_FEATURES[0]] = np.inf
    elif bad == "missing_date":
        panel.loc[0, "date"] = pd.NaT
    elif bad == "missing_symbol":
        panel.loc[0, "symbol"] = None
    else:
        panel = []
    with pytest.raises(ValueError, match="training|feature|finite|unique|Unique"):
        predict(head, features([("2024-01-02", "AAPL")]), training_frame=panel)
    assert fitted_rows == []


@pytest.mark.parametrize("recipe", ["rf_calibrated20", "logistic_swing20"])
def test_date_weighting_rejects_recipes_without_supported_weighted_fit(recipe, fitted_rows):
    with pytest.raises(ValueError, match="weight"):
        swing.walk_forward_predict(features([("2024-01-02", "AAPL")]), recipe,
                                   "2024-01-02", "2024-01-03", training_frame=uneven_panel(), date_weighted=True)
    assert fitted_rows == []


@pytest.mark.parametrize("bad", [None, "false", 1])
def test_date_weighting_flag_requires_a_boolean(bad, fitted_rows):
    with pytest.raises(ValueError, match="weight"):
        predict("downside", features([("2024-01-02", "AAPL")]), training_frame=uneven_panel(), date_weighted=bad)
    assert fitted_rows == []
