"""Fixed monthly refits preserve separate full-horizon purges and annual defaults."""

import numpy as np
import pandas as pd
import pytest

from stocklab_ml import downside_research as downside, swing_research as swing


def frame():
    dates = pd.bdate_range("2020-01-01", "2024-03-15").difference(pd.to_datetime(["2024-01-01"]))
    result = pd.DataFrame({"date": dates.astype("datetime64[ns]"), "symbol": "AAPL"})
    for i, column in enumerate(downside.DOWNSIDE_FEATURES):
        result[column] = np.sin(np.arange(len(result)) / (i + 2))
    for label, known, horizon in [("target_20", "target_date_20", 21), (downside.LABEL, downside.KNOWN_DATE, 20)]:
        result[known] = result.date.shift(-horizon)
        result[label] = (np.sin(np.arange(len(result)) / (horizon - 9)) > .1).astype(float)
        result.loc[result[known].isna(), label] = np.nan
    return result


def predict(head, data, start, end, **kwargs):
    return (downside.predict(data, start, end, **kwargs) if head == "downside"
            else swing.walk_forward_predict(data, "rf_swing20", start, end, **kwargs))


@pytest.mark.parametrize("head,label,known", [
    ("upside", "target_20", "target_date_20"), ("downside", downside.LABEL, downside.KNOWN_DATE)])
def test_months_refit_at_first_observed_session_with_their_own_known_date(head, label, known):
    data = frame()
    predictions, audits = predict(head, data, "2024-01-01", "2024-03-15", refit_frequency="monthly")
    assert [a["first_prediction"] for a in audits] == ["2024-01-02", "2024-02-01", "2024-03-01"]
    assert predictions.date.tolist() == data.loc[data.date.between("2024-01-01", "2024-03-15"), "date"].tolist()
    for audit in audits:
        first = pd.Timestamp(audit["first_prediction"])
        train = data.loc[(data.date >= first - pd.DateOffset(years=4)) & (data.date < first)
                         & (data[known] < first) & data[label].notna()]
        assert audit["first_fit"] == str(train.date.min().date())
        assert audit["last_fit"] == str(train.date.max().date())
        assert audit["last_target"] == str(train[known].max().date())
        assert audit["train_rows"] == len(train)
        assert pd.Timestamp(audit["last_target"]) < first
        if head == "downside":
            assert audit["training_positive_rate"] == train[label].mean()
        else:
            assert "training_positive_rate" not in audit
    assert pd.isna(data[label].iloc[-1])
    assert np.isfinite(predictions.iloc[:, -1]).all()
    annual, annual_audits = predict(head, data, "2024-01-01", "2024-01-31")
    pd.testing.assert_frame_equal(predictions.loc[predictions.date < "2024-02-01"], annual)
    assert audits[0] == annual_audits[0]


@pytest.mark.parametrize("head,label,known", [
    ("upside", "target_20", "target_date_20"), ("downside", downside.LABEL, downside.KNOWN_DATE)])
def test_unknown_or_boundary_equal_future_labels_cannot_change_earlier_month_predictions(head, label, known):
    data = frame()
    first, audits = predict(head, data, "2024-01-01", "2024-02-29", refit_frequency="monthly")
    changed = data.copy()
    excluded = changed[known] >= pd.Timestamp("2024-02-01")
    assert changed[known].eq(pd.Timestamp("2024-02-01")).any()
    changed.loc[excluded, label] = 1 - changed.loc[excluded, label]
    second, repeated = predict(head, changed, "2024-01-01", "2024-02-29", refit_frequency="monthly")
    pd.testing.assert_frame_equal(first, second)
    assert audits == repeated


@pytest.mark.parametrize("head", ["upside", "downside"])
def test_explicit_annual_frequency_preserves_default_predictions_and_audits(head):
    data = frame()
    default, default_audits = predict(head, data, "2024-01-01", "2024-03-15")
    explicit, explicit_audits = predict(head, data, "2024-01-01", "2024-03-15", refit_frequency="annual")
    pd.testing.assert_frame_equal(default, explicit)
    assert default_audits == explicit_audits
    assert len(explicit_audits) == 1


@pytest.mark.parametrize("head", ["upside", "downside"])
@pytest.mark.parametrize("bad", [None, True, "weekly", ""])
def test_unknown_refit_frequency_fails_before_feature_access(head, bad):
    with pytest.raises(ValueError, match="frequency"):
        predict(head, pd.DataFrame(), "2024-01-01", "2024-02-29", refit_frequency=bad)


@pytest.mark.parametrize("head,label", [("upside", "target_20"), ("downside", downside.LABEL)])
def test_every_month_requires_two_training_classes(head, label):
    data = frame()
    data[label] = 1.
    data.loc[data.date < "2020-02-01", label] = 0.
    with pytest.raises(ValueError, match="both classes"):
        predict(head, data, "2024-01-01", "2024-02-29", refit_frequency="monthly")
