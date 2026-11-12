"""Scalar v50 signal rules, with controlled values from its TypeScript oracle."""

from copy import deepcopy
from datetime import date, timedelta
import math

import pytest

from stocklab_ml.dca_signals import compute_signals, classify_range


def bars(count, close=lambda i: 100. + i):
    result = []
    for i in range(count):
        day = (date(2024, 1, 1) + timedelta(days=i)).isoformat()
        price = close(i)
        result.append({"date": day, "periodEnd": day, "availableDate": day,
            "open": price, "high": price + 2., "low": price - 2., "close": price,
            "volume": 100, "confirmed": True})
    return result


@pytest.mark.parametrize("position,rsi,category,multiplier", [
    (3., 79.99, "EXTREME_BUY", 4), (3.001, 50., "STRONG_BUY", 3),
    (10., 50., "STRONG_BUY", 3), (10.001, 50., "BUY", 2),
    (25., 50., "BUY", 2), (25.001, 50., "ADD", 1),
    (100., 79.999, "ADD", 1), (89.999, 80., "LIGHT_TRIM", 0),
    (90., 80., "STRONG_TRIM", 0), (2., 80., "LIGHT_TRIM", 0),
])
def test_inclusive_zones_and_rsi80_priority(position, rsi, category, multiplier):
    assert classify_range(position, rsi) == {"category": category, "multiplier": multiplier}


@pytest.mark.parametrize("invalid", [None, math.nan, math.inf, -math.inf, True, "3"])
def test_invalid_indicators_cannot_create_a_buy(invalid):
    assert classify_range(invalid, 50.) == {"category": "UNAVAILABLE", "multiplier": 0}
    assert classify_range(50., invalid) == {"category": "UNAVAILABLE", "multiplier": 0}


@pytest.mark.parametrize("profile,lookback,low,score,category,touch,touch_multiplier", [
    (12, 9, 102., 25., "BUY", "EXTREME_BUY", 4),
    (24, 12, 100., 40., "ADD", "BUY", 2),
])
def test_scalar_wilder_seed_and_update_match_v50_oracle(
        profile, lookback, low, score, category, touch, touch_multiplier):
    closes = [100.]
    for i in range(1, 15):
        closes.append(closes[-1] + (2. if i % 2 else -1.))
    closes.append(closes[-1] - 3.)
    points = compute_signals(bars(16, lambda i: closes[i]), profile)
    assert points[13]["rsi"] is None
    assert points[14]["rsi"] == 66.66666666666667
    point = points[15]
    assert {key: point[key] for key in ("rsi", "rangeHigh", "rangeLow", "score", "category",
        "multiplier", "lookback", "touchCategory", "touchMultiplier")} == {
        "rsi": 57.77777777777778, "rangeHigh": 110., "rangeLow": low, "score": score,
        "category": category, "multiplier": 2 if profile == 12 else 1, "lookback": lookback,
        "touchCategory": touch, "touchMultiplier": touch_multiplier}


@pytest.mark.parametrize("profile,first_usable,lookback,range_low", [
    (12, 14, 9, 104.), (24, 14, 12, 101.), (36, 18, 19, 98.),
])
def test_profiles_use_actual_windows_and_current_bar(profile, first_usable, lookback, range_low):
    points = compute_signals(bars(25), profile)
    assert all(point["category"] == "UNAVAILABLE" for point in points[:first_usable])
    assert points[first_usable]["category"] != "UNAVAILABLE"
    assert points[first_usable]["rangeLow"] == range_low
    assert points[first_usable]["rangeHigh"] == 102. + first_usable
    assert points[first_usable]["lookback"] == lookback
    assert points[first_usable]["engineVersion"] == "range_rsi_v1"


def test_zero_range_keeps_seeded_neutral_rsi_without_resetting_history():
    input_bars = bars(21, lambda i: 100.)
    for bar in input_bars[:20]:
        bar["high"] = bar["low"] = 100.
    points = compute_signals(input_bars)
    assert points[19]["reason"] == "zero_range" and points[19]["rsi"] == 50.
    assert points[19]["score"] is None
    assert points[20]["category"] == "ADD" and points[20]["rsi"] == 50.


@pytest.mark.parametrize("change,reason,first_usable", [
    ({"confirmed": False}, "provisional_period", 40),
    ({"confirmed": False, "reason": "missing_month"}, "missing_month", 40),
    ({"high": math.nan}, "invalid_candle", 40),
    ({"reason": "history_gap"}, "history_gap", 39),
])
def test_invalid_provisional_and_gap_bars_restart_both_histories(change, reason, first_usable):
    input_bars = bars(50)
    input_bars[25].update(change)
    points = compute_signals(input_bars, 12)
    assert points[25]["reason"] == reason and points[25]["rsi"] is None
    assert all(point["category"] == "UNAVAILABLE" for point in points[25:first_usable])
    assert points[first_usable]["category"] != "UNAVAILABLE"


@pytest.mark.parametrize("change", [
    {"open": 0.}, {"close": math.inf}, {"high": 1.}, {"low": 1000.},
    {"volume": -1.}, {"volume": True}, {"availableDate": "2024-99-01"},
    {"availableDate": "2024-01-01"}, {"periodEnd": "2024-01-01"},
    {"availableDate": "2024-01-03"}, {"date": "2024-02-30"},
])
def test_invalid_candle_or_availability_returns_unavailable(change):
    input_bars = bars(2)
    input_bars[1].update(change)
    point = compute_signals(input_bars)[1]
    assert point["reason"] == "invalid_candle" and point["category"] == "UNAVAILABLE"
    assert point["multiplier"] == point["touchMultiplier"] == 0
    assert point["rangeHigh"] is point["rangeLow"] is point["rsi"] is point["score"] is None


def test_nullable_volume_and_iso_leap_dates_match_source_validation():
    bar = bars(1)[0] | {"date": "0000-02-29", "periodEnd": "0000-02-29",
                          "availableDate": "0000-02-29", "volume": None}
    assert compute_signals([bar])[0]["reason"] == "insufficient_history"
    del bar["volume"]
    assert compute_signals([bar])[0]["reason"] == "invalid_candle"


def test_extreme_prices_keep_all_computed_indicators_finite():
    input_bars = bars(40, lambda i: 1e308 if i % 2 else 1e307)
    for bar in input_bars:
        bar["high"] = bar["low"] = bar["close"]
    for point in compute_signals(input_bars):
        assert all(point[key] is None or math.isfinite(point[key])
                   for key in ("rangeHigh", "rangeLow", "rsi", "score"))


def test_touch_marker_does_not_replace_close_category_or_create_executable_signal():
    input_bars = bars(16, lambda i: 100.)
    input_bars[5]["low"], input_bars[15]["low"] = 90., 80.
    point = compute_signals(input_bars)[15]
    assert (point["category"], point["multiplier"]) == ("ADD", 1)
    assert (point["touchCategory"], point["touchMultiplier"]) == ("EXTREME_BUY", 4)
    input_bars[15]["confirmed"] = False
    point = compute_signals(input_bars)[15]
    assert point["category"] == "UNAVAILABLE" and point["touchMultiplier"] == 0


def test_future_bars_and_computation_cannot_mutate_prior_signals_or_inputs():
    input_bars = bars(50, lambda i: 100. + 10. * math.sin(i))
    input_bars[15].update(symbol="AAPL", validThrough="2024-12-31", reason="custom_metadata")
    before = deepcopy(input_bars)
    partial = compute_signals(input_bars[:30])
    assert partial == compute_signals(input_bars, 24)[:30]
    assert input_bars == before
    assert partial[15]["symbol"] == "AAPL" and partial[15]["validThrough"] == "2024-12-31"
    assert "reason" not in partial[15]


@pytest.mark.parametrize("input_bars,profile,match", [
    (bars(5)[::-1], 12, "chronological"),
    ([bars(1)[0], bars(1)[0]], 24, "chronological"),
    (bars(5), 13, "profile"),
    (bars(8001), 24, "limit"),
])
def test_invalid_order_profile_or_history_length_is_rejected(input_bars, profile, match):
    with pytest.raises(ValueError, match=match):
        compute_signals(input_bars, profile)


def test_empty_history_has_no_signals():
    assert compute_signals([]) == []
