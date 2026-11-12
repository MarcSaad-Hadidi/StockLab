"""Separate price-path labels and causal decisions, without changing upside recipes."""

import numpy as np
import pandas as pd
import pytest

from stocklab_ml import downside_research as downside, swing_research as swing


def bars(dates, symbol="AAPL", price=100.):
    return pd.DataFrame({"date": pd.to_datetime(dates).astype("datetime64[ns]"), "symbol": symbol,
        "open": price, "high": price * 1.01, "low": price * .99, "close": price, "volume": 1000}).astype({
            "symbol": "string", "volume": "Int64"})


def inputs():
    dates = pd.bdate_range("2020-01-01", periods=300)
    prices, spy = bars(dates), bars(dates, "SPY")
    features = prices.loc[220:, ["date", "symbol"]].reset_index(drop=True)
    for column in swing.SWING_FEATURES:
        features[column] = .1
    return features, prices, spy


@pytest.mark.parametrize("future_index,expected", [(221, 1.), (240, 1.), (241, 0.)])
def test_downside_window_includes_first_and_twentieth_close_but_not_twenty_first(future_index, expected):
    features, prices, spy = inputs()
    prices.loc[future_index, ["low", "close"]] = [94., 95.]
    frame = downside.make_frame(features, prices, spy)
    assert frame[downside.LABEL].iloc[0] == expected
    assert frame[downside.KNOWN_DATE].iloc[0] == prices.date.iloc[240]
    assert frame[downside.LABEL].iloc[-20:].isna().all()
    assert frame[downside.KNOWN_DATE].iloc[-20:].isna().all()


def test_entry_overnight_gap_is_excluded_and_unobserved_future_is_not_negative():
    features, prices, spy = inputs()
    prices.loc[221:, ["open", "high", "low", "close"]] = [50., 51., 49., 50.]
    frame = downside.make_frame(features, prices, spy)
    assert frame[downside.LABEL].iloc[0] == 0
    assert frame.forward_price_adverse20.iloc[0] == 0
    assert pd.isna(frame[downside.LABEL].iloc[-1])


def test_downside_labels_never_cross_symbols():
    features, prices, spy = inputs()
    second_prices = prices.copy().assign(symbol=pd.Series(["MSFT"] * len(prices), dtype="string"))
    second_prices[["open", "high", "low", "close"]] *= 2
    second_features = features.copy().assign(symbol=pd.Series(["MSFT"] * len(features), dtype="string"))
    prices.loc[240, ["low", "close"]] = [89., 90.]
    frame = downside.make_frame(pd.concat([features, second_features], ignore_index=True),
        pd.concat([prices, second_prices], ignore_index=True), spy)
    assert frame.loc[frame.symbol.eq("AAPL"), downside.LABEL].iloc[0] == 1
    assert frame.loc[frame.symbol.eq("MSFT"), downside.LABEL].iloc[0] == 0
    assert frame.groupby("symbol")[downside.LABEL].apply(lambda x: x.iloc[-20:].isna().all()).all()


def test_direct_spy_context_is_trailing_and_has_the_frozen_dimensionless_definition():
    features, prices, spy = inputs()
    for column in ("open", "high", "low", "close"):
        spy[column] *= np.exp(np.arange(len(spy)) / 1000 + .01 * np.sin(np.arange(len(spy)) / 4))
    original = downside.make_frame(features, prices, spy)
    day = original.date.iloc[0]
    i = int(spy.index[spy.date.eq(day)][0])
    assert original.spy_momentum_20.iloc[0] == pytest.approx(spy.close.iloc[i] / spy.close.iloc[i - 20] - 1)
    assert original.spy_momentum_63.iloc[0] == pytest.approx(spy.close.iloc[i] / spy.close.iloc[i - 63] - 1)
    assert original.spy_close_ma200.iloc[0] == pytest.approx(spy.close.iloc[i] / spy.close.iloc[i - 199:i + 1].mean() - 1)
    assert original.spy_volatility_20.iloc[0] == pytest.approx(spy.close.pct_change(fill_method=None).iloc[i - 19:i + 1].std(ddof=0))
    changed = spy.copy()
    changed.loc[changed.date > original.date.iloc[25], ["open", "high", "low", "close"]] *= 2
    future = downside.make_frame(features, prices, changed)
    columns = ["date", "symbol"] + downside.DOWNSIDE_FEATURES
    before = original.date <= original.date.iloc[25]
    pd.testing.assert_frame_equal(original.loc[before, columns], future.loc[before, columns])


@pytest.mark.parametrize("bad", ["calendar", "unobserved", "duplicate", "warmup"])
def test_frame_rejects_incomplete_or_ambiguous_context(bad):
    features, prices, spy = inputs()
    if bad == "calendar":
        spy = spy.iloc[:-1].reset_index(drop=True)
    elif bad == "unobserved":
        features.loc[0, "date"] = pd.Timestamp("2019-01-01")
    elif bad == "duplicate":
        features = pd.concat([features, features.iloc[:1]], ignore_index=True)
    else:
        features = pd.concat([prices.iloc[[0]][["date", "symbol"]].assign(**{
            column: .1 for column in swing.SWING_FEATURES}), features], ignore_index=True)
    with pytest.raises(ValueError):
        downside.make_frame(features, prices, spy)


def fit_frame():
    dates = pd.bdate_range("2020-01-01", "2024-01-15").astype("datetime64[ns]")
    frame = pd.DataFrame({"date": dates, "symbol": pd.Series(["AAPL"] * len(dates), dtype="string")})
    for i, column in enumerate(downside.DOWNSIDE_FEATURES):
        frame[column] = np.sin(np.arange(len(frame)) / (i + 2))
    frame[downside.LABEL] = (np.sin(np.arange(len(frame)) / 7) + np.cos(np.arange(len(frame)) / 13) > .1).astype(float)
    frame[downside.KNOWN_DATE] = frame.date + pd.offsets.BDay(20)
    unknown = frame[downside.KNOWN_DATE] > frame.date.max()
    frame.loc[unknown, [downside.LABEL, downside.KNOWN_DATE]] = [np.nan, pd.NaT]
    return frame


def test_downside_fit_purges_full_path_and_preserves_latest_unlabeled_inference():
    frame = fit_frame()
    predictions, audits = downside.predict(frame, "2024-01-01", "2024-01-15")
    assert list(predictions) == ["date", "symbol", "probability_downside"]
    assert predictions.date.max() == frame.date.max()
    assert predictions.probability_downside.between(0, 1).all()
    audit = audits[0]
    assert audit["last_target"] < audit["first_prediction"]
    eligible = frame.loc[(frame.date >= pd.Timestamp(audit["first_prediction"]) - pd.DateOffset(years=4))
        & (frame.date < audit["first_prediction"]) & (frame[downside.KNOWN_DATE] < audit["first_prediction"])]
    assert audit["training_positive_rate"] == eligible[downside.LABEL].mean()
    assert audit["train_rows"] == len(eligible)
    changed = frame.copy()
    excluded = changed[downside.KNOWN_DATE] >= audit["first_prediction"]
    changed.loc[excluded, downside.LABEL] = 1 - changed.loc[excluded, downside.LABEL]
    repeated, repeated_audits = downside.predict(changed, "2024-01-01", "2024-01-15")
    pd.testing.assert_frame_equal(predictions, repeated)
    assert audits == repeated_audits


def test_downside_fit_rejects_one_training_class():
    frame = fit_frame()
    frame[downside.LABEL] = 0.
    with pytest.raises(ValueError, match="both classes"):
        downside.predict(frame, "2024-01-01", "2024-01-15")


@pytest.mark.parametrize("recipe,expected,last_fit,rows", [
    ("rf_swing20", [.2853654385159727, .2831667702370387, .27441829651852584, .2711261682173253,
        .26436445337543796, .2563895339828514, .253187487695777, .23033594016654543,
        .23008864915787902, .24836270914685948, .25302935309258046], "2023-11-30", 1022),
    ("rf_calibrated20", [.3389040206999085, .3392065013705061, .3403973138861686, .33848862970424043,
        .34028346447561436, .3405754199179526, .3397004565790163, .3448796063873803,
        .34672897720055446, .3630158825791221, .37006038862017904], "2022-12-01", 762),
])
def test_original_scores_and_audits_match_pre_extraction_reference(recipe, expected, last_fit, rows):
    frame = fit_frame()
    frame["target_20"] = frame[downside.LABEL]
    frame["target_date_20"] = frame.date + pd.offsets.BDay(21)
    frame.loc[frame.target_date_20 > frame.date.max(), ["target_20", "target_date_20"]] = [np.nan, pd.NaT]
    predictions, audits = swing.walk_forward_predict(frame, recipe, "2024-01-01", "2024-01-15")
    np.testing.assert_array_equal(predictions.probability_up, expected)
    expected_audit = {"recipe": recipe, "label": "target_20", "first_fit": "2020-01-01", "last_fit": last_fit,
        "last_target": "2023-12-29", "first_prediction": "2024-01-01", "train_rows": rows}
    if recipe == "rf_calibrated20":
        expected_audit.update(base_last_target="2022-12-30", calibration_start="2023-01-02", calibration_rows=239)
    assert audits == [expected_audit]
    assert set(swing.RECIPES) == {"rf_daily_v1", "rf_swing5", "rf_swing20", "boosted_swing20",
                                 "logistic_swing20", "boosted_excess20"}


def score_inputs():
    dates = pd.bdate_range("2024-01-01", periods=6)
    up = pd.DataFrame({"date": dates, "symbol": "AAPL", "probability_up": [.7, .7, .7, .2, .5, .2]})
    down = pd.DataFrame({"date": dates, "symbol": "AAPL", "probability_downside": [.65, .35, .4, .2, .8, .8]})
    features = up[["date", "symbol"]].assign(close_ma50=.1, close_ma200=-.1, momentum_20=-.1)
    return up, down, features


def test_dual_orders_preserve_score_floors_and_risk_exit_overrides_conflicting_upside():
    up, down, features = score_inputs()
    decisions = downside.orders(up, down, features)
    assert list(decisions) == ["date", "symbol", "action", "allocation", "reason", "probability_up", "probability_downside"]
    assert decisions.date.tolist() == up.date.iloc[[0, 1, 3, 4, 5]].tolist()
    assert decisions.action.tolist() == ["SELL", "BUY", "SELL", "SELL", "SELL"]
    assert decisions.reason.tolist() == ["downside_model", "up_model", "up_model", "downside_model", "downside_model"]
    assert decisions.probability_downside.tolist() == [.65, .35, .2, .8, .8]
    assert decisions.allocation.eq(1.).all()
    reordered = downside.orders(up.iloc[::-1], down.sample(frac=1, random_state=7), features)
    pd.testing.assert_frame_equal(decisions, reordered)


@pytest.mark.parametrize("bad", ["missing", "extra", "duplicate", "nan", "range", "up_duplicate", "feature_missing"])
def test_dual_orders_reject_ambiguous_or_nonfinite_forecast_date_joins(bad):
    up, down, features = score_inputs()
    if bad == "missing":
        down = down.iloc[:-1]
    elif bad == "extra":
        down = pd.concat([down, down.iloc[:1].assign(symbol="MSFT")], ignore_index=True)
    elif bad == "duplicate":
        down = pd.concat([down, down.iloc[:1]], ignore_index=True)
    elif bad == "nan":
        down.loc[0, "probability_downside"] = np.nan
    elif bad == "range":
        down.loc[0, "probability_downside"] = 1.01
    elif bad == "up_duplicate":
        up = pd.concat([up, up.iloc[:1]], ignore_index=True)
    else:
        features = features.iloc[:-1]
    with pytest.raises(ValueError):
        downside.orders(up, down, features)
