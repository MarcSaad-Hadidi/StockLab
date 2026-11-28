"""Pure range/RSI signals ported from DCA Cercle v50, separate from ML features."""

import calendar
import math
import re


ENGINE_VERSION = "range_rsi_v1"
RSI_LENGTH = 14
RANGE_LENGTHS = {12: 9, 24: 12, 36: 19}


def _finite(value) -> bool:
    if not isinstance(value, (int, float)) or isinstance(value, bool):
        return False
    try:
        return math.isfinite(value)
    except OverflowError:
        return False


def _is_date(value) -> bool:
    if not isinstance(value, str) or re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}", value) is None:
        return False
    year, month, day = map(int, value.split("-"))
    return 1 <= month <= 12 and 1 <= day <= calendar.monthrange(year, month)[1]


def _classify_buy(position) -> dict:
    if position <= 3:
        return {"category": "EXTREME_BUY", "multiplier": 4}
    if position <= 10:
        return {"category": "STRONG_BUY", "multiplier": 3}
    if position <= 25:
        return {"category": "BUY", "multiplier": 2}
    return {"category": "ADD", "multiplier": 1}


def classify_range(position, rsi) -> dict:
    """Classify unrounded indicators; RSI80 overrides every purchase zone."""
    if not _finite(position) or not _finite(rsi):
        return {"category": "UNAVAILABLE", "multiplier": 0}
    if rsi >= 80:
        return {"category": "STRONG_TRIM" if position >= 90 else "LIGHT_TRIM", "multiplier": 0}
    return _classify_buy(position)


def compute_signals(bars: list[dict], profile: int = 24) -> list[dict]:
    """Return v50 SignalPoint dictionaries; confirmed PeriodCandle metadata stays intact.

    Windows count supplied bars, not days. Callers supply confirmed periods and
    explicit history_gap markers; touch fields are retrospective observations.
    """
    if profile not in (12, 24, 36):
        raise ValueError("Invalid profile: use 12, 24 or 36.")
    if not isinstance(bars, list):
        raise ValueError("Signal bars must be a list of dictionaries.")
    if len(bars) > 8000:
        raise ValueError("Signal history limit: 8000.")
    lookback = RANGE_LENGTHS[profile]
    window, count, gain, loss, previous_close, rsi = [], 0, 0., 0., None, None
    points = []
    for index, bar in enumerate(bars):
        if not isinstance(bar, dict):
            raise ValueError("Signal bars must be dictionaries.")
        day, available, end = bar.get("date"), bar.get("availableDate"), bar.get("periodEnd")
        if (index and isinstance(day, str) and isinstance(bars[index - 1].get("date"), str)
                and day <= bars[index - 1]["date"]):
            raise ValueError("Signal bars must be strictly chronological.")
        point = bar | {"profile": profile, "lookback": lookback, "engineVersion": ENGINE_VERSION,
            "rangeHigh": None, "rangeLow": None, "rsi": None, "score": None,
            "category": "UNAVAILABLE", "multiplier": 0, "touchCategory": None, "touchMultiplier": 0}
        prices = [bar.get(key) for key in ("open", "high", "low", "close")]
        valid = (all(_finite(value) and value > 0 for value in prices)
            and "volume" in bar and (bar["volume"] is None or (_finite(bar["volume"]) and bar["volume"] >= 0))
            and all(_is_date(value) for value in (day, available, end))
            and day <= available <= end and day <= end
            and bar["low"] <= min(bar["open"], bar["close"])
            and bar["high"] >= max(bar["open"], bar["close"])
            and bar["high"] >= bar["low"])
        if not valid or not bar.get("confirmed"):
            window, count, gain, loss, previous_close, rsi = [], 0, 0., 0., None, None
            point["reason"] = "invalid_candle" if not valid else (
                bar["reason"] if bar.get("reason") is not None else "provisional_period")
            points.append(point)
            continue
        if bar.get("reason") == "history_gap":
            window, count, gain, loss, previous_close, rsi = [], 0, 0., 0., None, None
        count += 1
        close = float(bar["close"])
        if previous_close is not None:
            delta = close - previous_close
            up, down = max(delta, 0.), max(-delta, 0.)
            # Preserve the source's scalar operation order at classification boundaries.
            if count <= RSI_LENGTH + 1:
                gain += up / RSI_LENGTH
                loss += down / RSI_LENGTH
            else:
                gain += (up - gain) / RSI_LENGTH
                loss += (down - loss) / RSI_LENGTH
            if count >= RSI_LENGTH + 1:
                if gain == 0 and loss == 0:
                    rsi = 50.
                elif loss == 0:
                    rsi = 100.
                elif gain >= loss:
                    rsi = 100 / (1 + loss / gain)
                else:
                    rsi = 100 * (gain / loss) / (1 + gain / loss)
        previous_close = close
        window.append(bar)
        if len(window) > lookback:
            window.pop(0)
        if len(window) == lookback:
            point["rangeHigh"] = max(float(row["high"]) for row in window)
            point["rangeLow"] = min(float(row["low"]) for row in window)
        point["rsi"] = rsi
        high, low = point["rangeHigh"], point["rangeLow"]
        if high is None or low is None or rsi is None:
            point["reason"] = "history_gap" if bar.get("reason") == "history_gap" else "insufficient_history"
        elif high - low <= 0:
            point["reason"] = "zero_range"
        else:
            score = (close - low) / (high - low) * 100
            if not math.isfinite(score) or not math.isfinite(rsi):
                point["reason"] = "nonfinite_indicator"
            else:
                verdict = classify_range(score, rsi)
                touch = _classify_buy((float(bar["low"]) - low) / (high - low) * 100)
                deeper = touch["multiplier"] > max(verdict["multiplier"], 1)
                point.pop("reason", None)
                point.update(verdict, score=score, touchCategory=touch["category"] if deeper else None,
                             touchMultiplier=touch["multiplier"] if deeper else 0)
        points.append(point)
    return points
