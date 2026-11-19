"""Offline, fixed-lag Cboe market features for the frozen paper study only."""

import csv
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath, PureWindowsPath
import re
import sys

import numpy as np
import pandas as pd


FEATURES = ["vix_level", "vix_change5", "vix_term_ratio", "vix_term_change5"]
_AUDIT = ["observation_date", "lookback_date"]
_SCHEMA = ["DATE", "OPEN", "HIGH", "LOW", "CLOSE"]
_START, _CUTOFF, _AVAILABLE = "2010-01-01", "2026-11-11", "2013-11-01"
_FIRST_FIT = pd.Timestamp("2014-01-02")
_URLS = {symbol: "https://cdn-api.cboe.com/api/global/us_indices/daily_prices/"
         + symbol + "_History.csv" for symbol in ("VIX", "VIX3M")}
_DOCS = {
    "VIX": "https://www.cboe.com/tradable_products/vix/vix_historical_data",
    "VIX3M": "https://www.cboe.com/us/indices/dashboard/vix3m/",
}
_REQUIRED = {"VIX_History.csv", "VIX3M_History.csv", "cboe-source-identity.json",
             "cboe-daily-quality.json", "cboe-quality-self-check.json", "cboe-feasibility.md",
             "cboe_source_identity_probe.py", "cboe_daily_quality_probe.py"}


def _sha(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def _digest(value) -> bool:
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def _read(root: Path, name: str) -> bytes:
    if (not isinstance(name, str) or not name or PureWindowsPath(name).drive
            or PureWindowsPath(name).is_absolute() or PurePosixPath(name).is_absolute()
            or any(part in ("", ".", "..") for part in name.replace("\\", "/").split("/"))):
        raise ValueError("Market artifact paths must stay relative to their source directory.")
    path = root
    for part in name.replace("\\", "/").split("/"):
        path = path / part
        if path.is_symlink() or path.is_junction():
            raise ValueError("Market artifacts cannot use symlinks or junctions.")
    try:
        if not path.resolve().is_relative_to(root) or not path.is_file():
            raise ValueError("Market artifact is missing or outside its source directory.")
        return path.read_bytes()
    except OSError as error:
        raise ValueError(f"Cannot read market artifact: {name}") from error


def _json(raw: bytes, name: str) -> dict:
    def invalid_constant(value):
        raise ValueError(f"Invalid JSON numeric constant in {name}: {value}")

    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"Duplicate JSON key in {name}: {key}")
            result[key] = value
        return result

    try:
        value = json.loads(raw.decode("utf-8-sig"), object_pairs_hook=unique,
                           parse_constant=invalid_constant)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"Invalid market JSON: {name}") from error
    if not isinstance(value, dict):
        raise ValueError(f"Market JSON must be an object: {name}")
    return value


def _same(actual: dict, expected: dict, name: str) -> None:
    if not isinstance(actual, dict):
        raise ValueError(f"Missing market evidence: {name}")
    for key, value in expected.items():
        if (key not in actual or actual[key] != value
                or (type(value) in (int, bool) and type(actual[key]) is not type(value))):
            raise ValueError(f"Market evidence mismatch: {name}.{key}")


def _dates(frame: pd.DataFrame, name: str, *, ordered: bool = False) -> pd.DatetimeIndex:
    if (not isinstance(frame, pd.DataFrame) or not frame.columns.is_unique
            or "date" not in frame or frame.date.dtype != np.dtype("datetime64[ns]")
            or frame.date.isna().any() or not frame.date.eq(frame.date.dt.normalize()).all()):
        raise ValueError(f"{name} requires unique columns and normalized datetime64[ns] dates.")
    dates = pd.DatetimeIndex(frame.date)
    if ordered and (dates.empty or not dates.is_unique or not dates.is_monotonic_increasing):
        raise ValueError(f"{name} dates must be nonempty, unique and strictly ordered.")
    return dates


def _source(raw: bytes, item: dict, symbol: str, calendar: pd.DatetimeIndex):
    _same(item, {"url": _URLS[symbol], "status": 200, "bytes": len(raw),
                 "sha256": _sha(raw), "schema": _SCHEMA, "schema_exact_match": True,
                 "raw_values_changed_or_filled": False}, symbol)
    _same(item.get("headers"), {"Location": None}, symbol + ".headers")
    if "error" in item:
        raise ValueError(f"Source quality probe failed: {symbol}")
    try:
        reader = csv.reader(io.StringIO(raw.decode("utf-8-sig")), strict=True)
        if next(reader) != _SCHEMA:
            raise ValueError(f"Unexpected {symbol} CSV schema.")
        dates, quotes, errors = [], [], []
        for line, row in enumerate(reader, 2):
            if not row:
                continue
            if len(row) != 5 or re.fullmatch(r"[0-9]{2}/[0-9]{2}/[0-9]{4}", row[0]) is None:
                raise ValueError(f"Malformed {symbol} row {line}.")
            day = pd.to_datetime(row[0], format="%m/%d/%Y", errors="raise")
            values = [float(value) for value in row[1:]]
            if not np.isfinite(values).all() or min(values) <= 0:
                raise ValueError(f"Non-positive or non-finite {symbol} quote at row {line}.")
            opening, high, low, close = values
            if not (low <= opening <= high and low <= close <= high):
                if pd.Timestamp(_START) <= day <= pd.Timestamp(_CUTOFF):
                    raise ValueError(f"Invalid {symbol} OHLC order in the analysis window.")
                errors.append({"line": line, "date": day.date().isoformat(),
                               "issues": ["ohlc_order"], "raw_values": row[1:]})
            dates.append(day)
            quotes.append(close)
    except (UnicodeError, csv.Error, StopIteration, OverflowError) as error:
        raise ValueError(f"Invalid {symbol} CSV.") from error
    dates = pd.DatetimeIndex(dates).astype("datetime64[ns]")
    if dates.empty or not dates.is_unique or not dates.is_monotonic_increasing:
        raise ValueError(f"{symbol} dates must be nonempty, unique and strictly ordered.")
    window = dates[(dates >= _START) & (dates <= _CUTOFF)]
    missing, extra = calendar.difference(window), window.difference(calendar)
    if not missing.empty:
        raise ValueError(f"{symbol} is missing {len(missing)} SPY sessions; no filling is allowed.")
    iso = lambda values: [value.date().isoformat() for value in values]
    expected = {
        "row_count": len(dates), "parsed_date_count": len(dates),
        "first_date": iso(dates[:1])[0], "last_date": iso(dates[-1:])[0],
        "dates_strictly_ascending": True, "duplicate_dates": {}, "invalid_date_rows": [],
        "malformed_rows": [], "quote_errors_all_history": errors,
        "quote_errors_analysis_window": [], "window_unique_dates": len(window),
        "window_first": iso(window[:1])[0], "window_last": iso(window[-1:])[0],
        "overlap_spy_open_dates": len(calendar), "missing_spy_open_dates": [],
        "dates_not_in_spy_calendar_in_window": iso(extra),
        "weekend_dates_in_window": iso(window[window.weekday >= 5]),
        "pre_2010_rows": int((dates < _START).sum()),
        "after_cutoff_rows_audit_only": iso(dates[dates > _CUTOFF]),
    }
    _same(item, expected, symbol)
    audit = {"raw_rows": len(dates), "pre_2010_rows": expected["pre_2010_rows"],
             "pre_2010_ohlc_order_issues": sum(error["date"] < _START for error in errors),
             "excluded_non_spy_dates": iso(extra), "excluded_non_spy_rows": len(extra),
             "prelaunch_source_rows": int((dates < _AVAILABLE).sum()),
             "after_cutoff_dates": expected["after_cutoff_rows_audit_only"]}
    # Exclude index-only dates before shifts so they never become trading sessions.
    return pd.Series(quotes, index=dates, dtype="float64").reindex(calendar), audit, expected


def load_market_risk(root: Path, spy: pd.DataFrame, spy_sha256: str) -> tuple[pd.DataFrame, dict]:
    """Verify the bound offline source bundle, then build four fixed causal features."""
    root = Path(root)
    if root.is_symlink() or root.is_junction() or not root.is_dir():
        raise ValueError("Market source directory must be an existing real directory.")
    root = root.resolve()
    calendar = _dates(spy, "SPY calendar", ordered=True)
    if (not _digest(spy_sha256) or calendar[0] < pd.Timestamp(_START)
            or calendar[-1] > pd.Timestamp(_CUTOFF)
            or ("symbol" in spy and set(spy.symbol) != {"SPY"})):
        raise ValueError("The supplied SPY identity, hash or analysis window is invalid.")
    manifest_raw = _read(root, "cboe-artifact-manifest.json")
    manifest = _json(manifest_raw, "manifest")
    if not _REQUIRED <= manifest.keys():
        raise ValueError("Market manifest omits required source evidence.")
    files, contents = {}, {}
    for name, entry in manifest.items():
        raw = _read(root, name)
        if (not isinstance(entry, dict) or not _digest(entry.get("sha256"))
                or type(entry.get("bytes")) is not int or entry["bytes"] < 0):
            raise ValueError(f"Invalid market manifest entry: {name}")
        _same(entry, {"bytes": len(raw), "sha256": _sha(raw)}, name)
        files[name], contents[name] = {"bytes": len(raw), "sha256": _sha(raw)}, raw
    scope_raw = _read(root, "root-probe-scope.md")
    identity = _json(contents["cboe-source-identity.json"], "source identity")
    quality = _json(contents["cboe-daily-quality.json"], "source quality")
    check = _json(contents["cboe-quality-self-check.json"], "source self-check")
    _same(identity, {"scope_sha256": _sha(scope_raw), "csv_requests": 0}, "source identity")
    docs = identity.get("docs")
    if not isinstance(docs, list) or len(docs) != 2 or any(not isinstance(doc, dict) for doc in docs):
        raise ValueError("Both official Cboe source identity pages are required.")
    for symbol, url in _DOCS.items():
        matches = [doc for doc in docs if doc.get("url") == url]
        if len(matches) != 1:
            raise ValueError(f"Missing official Cboe identity page for {symbol}.")
        doc = matches[0]
        _same(doc, {"status": 200}, symbol + " identity")
        _same(doc.get("headers"), {"Location": None}, symbol + " identity headers")
        links = doc.get("daily_history_links")
        if (not isinstance(links, list) or _URLS[symbol] not in links
                or not _digest(doc.get("sha256")) or type(doc.get("bytes")) is not int
                or doc["bytes"] <= 0):
            raise ValueError(f"Canonical {symbol} history must be linked from its official Cboe page.")
    _same(quality, {
        "scope_sha256": _sha(scope_raw),
        "source_identity_sha256": files["cboe-source-identity.json"]["sha256"],
        "script_sha256": files["cboe_daily_quality_probe.py"]["sha256"],
        "analysis_start": _START, "asof_cutoff": _CUTOFF, "requests_limit": 2,
        "redirects_followed": False, "retries": 0, "credentials_or_environment_proxies": False,
    }, "source quality")
    _same(quality.get("spy_calendar"), {
        "sha256": spy_sha256, "rows": len(calendar), "window_rows": len(calendar),
        "first": calendar[0].date().isoformat(), "last": calendar[-1].date().isoformat(),
    }, "SPY commitment")
    if not isinstance(quality.get("sources"), dict) or set(quality["sources"]) != set(_URLS):
        raise ValueError("Exactly the two fixed Cboe sources are required.")
    closes, audits, vintage = {}, {}, {}
    for symbol in _URLS:
        item = quality["sources"][symbol]
        closes[symbol], audits[symbol], parsed = _source(contents[f"{symbol}_History.csv"], item, symbol, calendar)
        _same(check.get(symbol), {
            "all_history_ohlc_order_issues": len(parsed["quote_errors_all_history"]),
            "extra_spy_calendar_dates": len(parsed["dates_not_in_spy_calendar_in_window"]),
            "raw_hash_verified": True, "all_close_positive_finite": True, "latest": parsed["last_date"],
        }, symbol + " self-check")
        vintage[symbol] = {key: item.get(key) for key in ("url", "requested_at_utc", "completed_at_utc", "headers")}
    _same(check, {"common_window_dates": len(calendar), "scope_hash_verified": True}, "source self-check")
    vix = closes["VIX"].where(calendar >= _AVAILABLE)
    vix3m = closes["VIX3M"].where(calendar >= _AVAILABLE)
    term = vix / vix3m - 1
    dates = pd.Series(calendar, index=calendar)
    macro = pd.DataFrame({
        "date": calendar, "vix_level": vix.shift(2) / 100,
        "vix_change5": vix.shift(2) / vix.shift(7) - 1,
        "vix_term_ratio": term.shift(2), "vix_term_change5": term.shift(2) - term.shift(7),
        "observation_date": dates.shift(2), "lookback_date": dates.shift(7),
    }).dropna(subset=FEATURES).reset_index(drop=True)
    if (macro.empty or macro.date.iloc[0] >= _FIRST_FIT
            or not np.isfinite(macro[FEATURES].to_numpy()).all()
            or not macro.date.equals(pd.Series(calendar[calendar >= macro.date.iloc[0]], name="date"))):
        raise ValueError("Market warmup would change an eligible fit or leave later coverage gaps.")
    calendar_raw = ("\n".join(day.date().isoformat() for day in calendar) + "\n").encode("ascii")
    prelaunch = int((calendar < _AVAILABLE).sum())
    commitment = {
        "protocol": "broader-boosted-market-risk-20261112-v1",
        "manifest": {"file": "cboe-artifact-manifest.json", "bytes": len(manifest_raw), "sha256": _sha(manifest_raw)},
        "files": files,
        "root_probe_scope": {"file": "root-probe-scope.md", "bytes": len(scope_raw), "sha256": _sha(scope_raw)},
        "spy_sha256": spy_sha256,
        "spy_calendar": {"sha256": _sha(calendar_raw), "encoding": "ISO-date lines, final LF",
                         "rows": len(calendar), "first": calendar[0].date().isoformat(),
                         "last": calendar[-1].date().isoformat()},
        "parameters": {"analysis_start": _START, "asof_cutoff": _CUTOFF,
                       "availability_start": _AVAILABLE, "lag_spy_sessions": 2,
                       "change_spy_sessions": 5, "features": list(FEATURES), "audit_columns": list(_AUDIT)},
        "source_audit": audits, "prelaunch_spy_sessions": prelaunch,
        "warmup_spy_sessions": len(calendar) - len(macro),
        "postlaunch_warmup_spy_sessions": len(calendar) - len(macro) - prelaunch,
        "feature_rows": len(macro), "first_feature_date": macro.date.iloc[0].date().isoformat(),
        "last_feature_date": macro.date.iloc[-1].date().isoformat(),
        "source_vintage": {"identity_retrieved_at_utc": identity.get("retrieved_at_utc"), "sources": vintage},
        "timing_limits": ["Current histories can include retrospective EOD corrections; original vintages are not certified.",
                          "Two SPY session lags are conservative research timing, not proof of historical publication availability.",
                          "Prospective use needs retrieval timestamps before decisions and retained daily vintages."],
        "environment": {"python": sys.version, "numpy": np.__version__, "pandas": pd.__version__,
                        "OMP_NUM_THREADS": os.environ.get("OMP_NUM_THREADS")},
    }
    return macro, commitment


def attach(frame: pd.DataFrame, macro: pd.DataFrame) -> pd.DataFrame:
    """Attach exact market coverage, removing only the unavailable initial prefix."""
    _dates(frame, "Feature frame")
    _dates(macro, "Market features", ordered=True)
    columns = FEATURES + _AUDIT
    if (macro.columns.tolist() != ["date"] + columns or set(columns) & set(frame.columns)
            or not all(pd.api.types.is_numeric_dtype(macro[column]) for column in FEATURES)
            or not np.isfinite(macro[FEATURES].to_numpy(dtype="float64")).all()):
        raise ValueError("Market features require finite columns without duplicates or shadowing.")
    for column in _AUDIT:
        if macro[column].dtype != np.dtype("datetime64[ns]") or macro[column].isna().any():
            raise ValueError("Market observation dates must remain datetime64[ns] audit columns.")
    if (not macro.lookback_date.ge(pd.Timestamp(_AVAILABLE)).all()
            or not macro.lookback_date.lt(macro.observation_date).all()
            or not macro.observation_date.lt(macro.date).all()):
        raise ValueError("Market audit dates must use postlaunch observations strictly before prediction.")
    retained = frame.loc[frame.date >= macro.date.iloc[0]]
    dropped = frame.loc[frame.date < macro.date.iloc[0], "date"]
    if dropped.ge(_FIRST_FIT).any() or not retained.date.isin(macro.date).all():
        raise ValueError("Market features would remove an eligible fit or leave later session gaps.")
    return retained.merge(macro, on="date", how="left", sort=False, validate="many_to_one")
