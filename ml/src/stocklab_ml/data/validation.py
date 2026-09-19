"""Strict schema normalization and OHLCV validation, without repairs."""

from dataclasses import asdict
from datetime import date
import math
import re

import pandas as pd

from .models import (
    DatasetValidationError, HistoricalBar, HistoricalMetadata, HistoricalRequest,
    InvalidProviderResponseError, RangeTooLargeError,
)

LIMIT_MESSAGE = "Requested range may exceed Twelve Data single-request limit. Reduce the period."
COLUMNS = ["date", "symbol", "open", "high", "low", "close", "volume"]
SYMBOL_PATTERN = r"[A-Z0-9][A-Z0-9.\^/-]{0,31}(?::[A-Z0-9][A-Z0-9._-]{0,15})?"


def calendar_date(value: str | date) -> date:
    if type(value) is date:
        return value
    if not isinstance(value, str) or not re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}", value):
        raise DatasetValidationError("Dates must be calendar dates in YYYY-MM-DD format.")
    try:
        return date.fromisoformat(value)
    except ValueError:
        raise DatasetValidationError("Invalid calendar date.") from None


def validate_request(symbol: str, start_date: str | date, end_date: str | date) -> HistoricalRequest:
    if not isinstance(symbol, str):
        raise DatasetValidationError("A single non-empty symbol is required.")
    symbol = symbol.strip().upper()
    if not re.fullmatch(SYMBOL_PATTERN, symbol):
        raise DatasetValidationError("Unsupported symbol format; supply one ticker.")
    start, end = calendar_date(start_date), calendar_date(end_date)
    if start >= end:
        raise DatasetValidationError("start_date must be before end_date.")
    if end >= date.today():
        raise DatasetValidationError("end_date must be before today; current daily bars may be incomplete.")
    # Calendar days upper-bound daily points even for seven-day trading markets.
    if (end - start).days + 1 >= 5000:
        raise RangeTooLargeError(LIMIT_MESSAGE)
    return HistoricalRequest(symbol, start, end)


def validate_envelope(payload: dict, request: HistoricalRequest) -> HistoricalMetadata:
    if not isinstance(payload, dict) or payload.get("status") != "ok":
        raise InvalidProviderResponseError("Expected a successful time-series response.")
    meta, values = payload.get("meta"), payload.get("values")
    if not isinstance(meta, dict) or not isinstance(values, list) or not values:
        raise InvalidProviderResponseError("Missing metadata or empty historical values.")
    if len(values) >= 4900:
        raise RangeTooLargeError(LIMIT_MESSAGE)
    for key in ("symbol", "interval", "currency", "exchange", "exchange_timezone"):
        if not isinstance(meta.get(key), str) or not meta[key].strip():
            raise InvalidProviderResponseError("Missing or invalid provider metadata.")
    for key in ("mic_code", "name"):
        if meta.get(key) is not None and not isinstance(meta[key], str):
            raise InvalidProviderResponseError("Invalid optional provider metadata.")
    symbol, _, exchange = request.symbol.partition(":")
    if meta["symbol"].upper() != symbol or meta["interval"] != "1day":
        raise InvalidProviderResponseError("Provider symbol or interval differs from the request.")
    if exchange and meta["exchange"].upper() != exchange:
        raise InvalidProviderResponseError("Provider exchange differs from the request.")
    return HistoricalMetadata(
        symbol=request.symbol, interval="1day", currency=meta["currency"],
        exchange=meta["exchange"], mic_code=meta.get("mic_code"),
        exchange_timezone=meta["exchange_timezone"], name=meta.get("name"),
    )


def _price(value: object) -> float:
    if isinstance(value, bool) or not isinstance(value, (str, int, float)):
        raise DatasetValidationError("OHLC prices must be finite positive numbers.")
    if isinstance(value, str) and not re.fullmatch(r"[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?", value):
        raise DatasetValidationError("Invalid OHLC numeric representation.")
    try:
        number = float(value)
    except (ValueError, OverflowError):
        raise DatasetValidationError("Invalid OHLC number.") from None
    if not math.isfinite(number) or number <= 0:
        raise DatasetValidationError("OHLC prices must be finite positive numbers.")
    return number


def _volume(value: object) -> int:
    # V1 requires volume on every bar; missing volume is never fabricated.
    if type(value) is int:
        number = value
    elif isinstance(value, str) and re.fullmatch(r"[0-9]{1,19}", value):
        number = int(value)
    else:
        raise DatasetValidationError("Volume must be a non-negative integer and present on every bar.")
    if not 0 <= number <= 2**63 - 1:
        raise DatasetValidationError("Volume is outside the Int64 range.")
    return number


def build_dataframe(payload: dict, request: HistoricalRequest) -> pd.DataFrame:
    """Normalize one raw response deterministically; reject rather than clean."""
    validate_envelope(payload, request)
    bars, seen = [], set()
    for row in payload["values"]:
        if not isinstance(row, dict):
            raise DatasetValidationError("Each provider bar must be an object.")
        day = calendar_date(row.get("datetime"))
        if not request.start_date <= day <= request.end_date:
            raise DatasetValidationError("Provider date falls outside the requested range.")
        if day in seen:
            raise DatasetValidationError("Duplicate symbol/date in provider data.")
        seen.add(day)
        prices = {key: _price(row.get(key)) for key in ("open", "high", "low", "close")}
        if not (prices["low"] <= min(prices["open"], prices["close"])
                <= max(prices["open"], prices["close"]) <= prices["high"]):
            raise DatasetValidationError("Invalid OHLC high/low relationship.")
        bars.append(HistoricalBar(day, **prices, volume=_volume(row.get("volume"))))
    frame = pd.DataFrame([asdict(bar) for bar in bars])
    try:
        frame["date"] = pd.to_datetime(frame["date"]).astype("datetime64[ns]")
    except (ValueError, OverflowError):
        raise DatasetValidationError("Dates are outside the supported pandas date range.") from None
    frame["symbol"] = pd.Series(request.symbol, index=frame.index, dtype="string")
    frame = frame.astype({**{key: "float64" for key in ("open", "high", "low", "close")}, "volume": "Int64"})
    return frame[COLUMNS].sort_values("date").reset_index(drop=True)
