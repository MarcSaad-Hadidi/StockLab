"""Training histories never fabricate sessions or cross an identity's gaps."""

import importlib

import numpy as np
import pandas as pd
import pytest

from stocklab_ml import downside_research as downside, swing_research as swing
from stocklab_ml.features.models import FeatureEngineeringError


def panel():
    return importlib.import_module("stocklab_ml.training_panel")


def bars(dates, symbol="NEW:NASDAQ", volume=7):
    dates = pd.to_datetime(dates).astype("datetime64[ns]")
    price = 100. + np.arange(len(dates)) + np.sin(np.arange(len(dates)) / 7)
    return pd.DataFrame({"date": dates, "symbol": symbol, "open": price,
        "high": price + 2., "low": price - 3., "close": price + 1., "volume": volume}).astype({
            "symbol": "string", "volume": "Int64"})


def identity():
    catalog = {"symbol": "NEW", "exchange": "NASDAQ", "mic_code": "XNAS",
               "currency": "USD", "type": "Common Stock"}
    metadata = dict(catalog, interval="1day", exchange_timezone="America/New_York")
    return catalog, metadata


def test_identity_requires_the_exact_catalog_instrument():
    catalog, metadata = identity()
    assert panel().validate_identity(catalog, metadata) is None


@pytest.mark.parametrize("side,key,value", [
    ("metadata", "symbol", "OTHER"), ("metadata", "exchange", "NYSE"),
    ("metadata", "mic_code", "XNYS"), ("metadata", "currency", "CAD"),
    ("metadata", "type", "ETF"), ("metadata", "interval", "1month"),
    ("metadata", "exchange_timezone", "UTC"), ("metadata", "mic_code", None),
    ("catalog", "exchange", "OTC"), ("catalog", "currency", "CAD"),
    ("catalog", "type", "ETF"), ("catalog", "mic_code", "NAS"),
    ("catalog", "symbol", "BAD SYMBOL"),
])
def test_ambiguous_or_wrong_identity_is_quarantined(side, key, value):
    catalog, metadata = identity()
    (catalog if side == "catalog" else metadata)[key] = value
    with pytest.raises(ValueError):
        panel().validate_identity(catalog, metadata)


def test_monthly_aggregation_uses_full_spy_months_and_excludes_open_and_ipo_months():
    dates = pd.bdate_range("2024-01-01", "2024-04-15").difference(pd.to_datetime(["2024-02-19"]))
    spy = bars(dates, "SPY")
    prices = bars(dates)
    prices[["open", "high", "low", "close"]] = np.column_stack([
        100. + np.arange(len(dates)), 102. + np.arange(len(dates)),
        97. + np.arange(len(dates)), 101. + np.arange(len(dates))])
    prices = prices.loc[prices.date >= "2024-01-15"].reset_index(drop=True)
    result = panel().aggregate_monthly(prices, spy)
    assert result.date.tolist() == list(pd.to_datetime(["2024-02-01", "2024-03-01"]))
    february = result.iloc[0]
    assert february[["open", "high", "low", "close", "volume"]].tolist() == [123., 144., 120., 143., 140]
    assert str(result.volume.dtype) == "Int64"
    missing = prices.loc[prices.date.ne(pd.Timestamp("2024-02-05"))].reset_index(drop=True)
    assert panel().aggregate_monthly(missing, spy).date.tolist() == [pd.Timestamp("2024-03-01")]


def test_aggregation_never_treats_a_truncated_spy_boundary_as_a_full_month():
    dates = pd.bdate_range("2024-01-15", "2024-03-11")
    result = panel().aggregate_monthly(bars(dates), bars(dates, "SPY"))
    assert result.date.tolist() == [pd.Timestamp("2024-02-01")]


def test_monthly_volume_overflow_fails_instead_of_wrapping():
    dates = pd.bdate_range("2024-01-01", "2024-03-11")
    with pytest.raises(ValueError, match="volume"):
        panel().aggregate_monthly(bars(dates, volume=2**62), bars(dates, "SPY"))


@pytest.mark.parametrize("function", ["aggregate_monthly", "build_identity"])
@pytest.mark.parametrize("bad", ["off_calendar", "duplicate", "multiple_symbols", "wrong_spy"])
def test_unobserved_or_ambiguous_sessions_fail_closed(function, bad):
    dates = pd.bdate_range("2024-01-01", "2024-04-15")
    prices, spy = bars(dates), bars(dates, "SPY")
    if bad == "off_calendar":
        prices.loc[0, "date"] = pd.Timestamp("2023-12-31")
    elif bad == "duplicate":
        prices = pd.concat([prices.iloc[:1], prices], ignore_index=True)
    elif bad == "multiple_symbols":
        prices.loc[0, "symbol"] = "OTHER:NASDAQ"
    else:
        spy["symbol"] = pd.Series("QQQ", index=spy.index, dtype="string")
    with pytest.raises((ValueError, FeatureEngineeringError)):
        getattr(panel(), function)(prices, spy)


def test_gap_resets_warmup_and_censors_both_episode_tails():
    dates = pd.bdate_range("2010-01-01", "2020-11-11")
    spy = bars(dates, "SPY")
    prices = bars(dates)
    prices = prices.loc[(prices.date >= "2010-03-17")
                        & prices.date.ne(pd.Timestamp("2013-09-16"))].reset_index(drop=True)
    original = prices.copy(deep=True)
    up, down, report = panel().build_identity(prices, spy)
    pd.testing.assert_frame_equal(prices, original)
    assert report["monthly_provenance"] == "daily_derived"
    assert report["status"] == "usable"
    assert report["internal_missing_sessions"] == 1
    assert report["missing_dates"] == ["2013-09-16"]
    assert len(report["episodes"]) == 2
    assert up.date.min() == pd.Timestamp("2011-11-01")
    assert up.loc[up.date > "2013-09-16", "date"].min() == pd.Timestamp("2015-05-01")
    assert report["episodes"][0]["start"] == "2010-03-17"
    assert report["episodes"][0]["end"] == "2013-09-13"
    assert report["episodes"][1]["start"] == "2013-09-17"
    assert report["up_rows"] == len(up) == len(down) == report["down_rows"]
    assert np.isfinite(up[swing.SWING_FEATURES].to_numpy()).all()
    assert np.isfinite(down[downside.DOWNSIDE_FEATURES].to_numpy()).all()
    for episode in report["episodes"]:
        u = up.loc[up.date.between(episode["start"], episode["end"])]
        d = down.loc[down.date.between(episode["start"], episode["end"])]
        assert u.target_20.iloc[-21:].isna().all()
        assert u.target_date_20.iloc[-21:].isna().all()
        assert d[downside.LABEL].iloc[-20:].isna().all()
        assert d[downside.KNOWN_DATE].iloc[-20:].isna().all()
        assert u.target_date_20.dropna().le(pd.Timestamp(episode["end"])).all()
        assert d[downside.KNOWN_DATE].dropna().le(pd.Timestamp(episode["end"])).all()
        assert episode["tail_missing_up"] == 21
        assert episode["tail_missing_down"] == 20
        assert episode["discarded_ranges"][0]["reason"] == "feature_warmup"
    with pytest.raises(ValueError, match="calendar"):
        swing._matching_calendar(prices, spy)


def test_short_histories_remain_explicit_empty_outcomes():
    dates = pd.bdate_range("2024-01-01", "2024-06-20")
    spy = bars(dates, "SPY")
    prices = bars(dates).iloc[7:].reset_index(drop=True)
    up, down, report = panel().build_identity(prices, spy)
    assert up.empty and down.empty
    assert {"target_20", "target_date_20", *swing.SWING_FEATURES}.issubset(up.columns)
    assert {downside.LABEL, downside.KNOWN_DATE, *downside.DOWNSIDE_FEATURES}.issubset(down.columns)
    assert report["status"] == "insufficient_history"
    assert report["up_rows"] == report["down_rows"] == 0
    assert report["episodes"][0]["status"] == "insufficient_warmup"
    assert report["episodes"][0]["discarded_ranges"] == [{
        "start": prices.date.iloc[0].date().isoformat(), "end": "2024-06-20", "reason": "insufficient_warmup"}]
    assert report["reason_counts"]["insufficient_warmup"] == len(prices)


def test_unavailable_interior_features_are_counted_without_filling_the_prices():
    dates = pd.bdate_range("2010-01-01", "2016-11-11")
    spy, prices = bars(dates, "SPY"), bars(dates)
    prices.loc[900:929, "volume"] = 0
    up, _, report = panel().build_identity(prices, spy)
    assert not up.date.isin(prices.date.iloc[919:930]).any()
    assert prices.volume.iloc[900:930].eq(0).all()
    assert report["reason_counts"]["feature_unavailable"] == 11
    assert report["up_rows"] + sum(report["reason_counts"].values()) == len(prices)
    assert report["episodes"][0]["discarded_ranges"][-1] == {
        "start": prices.date.iloc[919].date().isoformat(),
        "end": prices.date.iloc[929].date().isoformat(), "reason": "feature_unavailable"}


def test_all_zero_volume_is_reported_as_an_unusable_history():
    dates = pd.bdate_range("2010-01-01", "2014-11-11")
    prices = bars(dates, volume=0)
    up, down, report = panel().build_identity(prices, bars(dates, "SPY"))
    assert up.empty and down.empty
    assert report["reason_counts"] == {"nonfinite_feature_history": len(prices)}


def test_daily_derived_monthly_context_matches_existing_features_with_the_same_seed():
    dates = pd.bdate_range("2010-01-01", "2014-11-11")
    spy, prices = bars(dates, "SPY"), bars(dates)
    monthly = panel().aggregate_monthly(prices, spy)
    expected_up = swing.swing_features(prices, monthly, spy)
    expected_down = downside.make_frame(expected_up, prices, spy)
    up, down, _ = panel().build_identity(prices, spy)
    pd.testing.assert_frame_equal(up, expected_up)
    pd.testing.assert_frame_equal(down, expected_down)
