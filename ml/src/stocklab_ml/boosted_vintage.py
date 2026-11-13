"""Read-only, source-bound HGBT28 inputs; no targets, acquisition or activation."""

import csv
from datetime import UTC, datetime, timedelta
import hashlib
import io
import json
from pathlib import Path
import re

import numpy as np
import pandas as pd

from stocklab_ml import research
from stocklab_ml.artifacts.contracts import ModelArtifactError
from stocklab_ml.artifacts.local_storage import read_bounded
from stocklab_ml.boosted_snapshot import FEATURES, SYMBOLS
from stocklab_ml.data.cleaning import _validate_cleaned
from stocklab_ml.data.models import DataCleaningError
from stocklab_ml.data.validation import COLUMNS
from stocklab_ml.features.models import FeatureEngineeringError
from stocklab_ml.versioning.validation import frame_digest


MAX_MANIFEST_BYTES = 64 * 1024
MAX_CSV_BYTES = 64 * 1024 * 1024
MAX_TOTAL_BYTES = 128 * 1024 * 1024
_FILES = {"stocks.csv", "monthly.csv", "spy.csv", "vix.csv", "vix3m.csv", "expected-sessions.csv"}
_PREFIXES = {"daily": "2010-01-04", "monthly": "2010-01-01"}
_IDENTITIES = {symbol: {"mic": "XNAS" if symbol in {"AAPL", "MSFT"} else "XNYS",
    "kind": "Common Stock", "currency": "USD", "timezone": "America/New_York"} for symbol in sorted(SYMBOLS)}
_IDENTITIES["SPY"] = {"mic": "ARCX", "kind": "ETF", "currency": "USD", "timezone": "America/New_York"}
_FIXED = {"schema_version": 1, "profile": "hgb_swing20_market28", "identities": _IDENTITIES,
    "price_basis": {"stocks.csv": "splits", "monthly.csv": "splits", "spy.csv": "all"},
    "intervals": {"stocks.csv": "1day", "monthly.csv": "1month", "spy.csv": "1day",
        "vix.csv": "1day", "vix3m.csv": "1day"}, "macro_units": "index_points", "prefixes": _PREFIXES,
    "recursive_prefix_attestation": "complete_recursive_prefixes_supplied"}
_MARKET = list(FEATURES[-4:])
_SWING = list(FEATURES[:-4])


def _require(valid, message):
    if not valid:
        raise ValueError(message)


def _keys(value, keys):
    _require(isinstance(value, dict) and set(value) == set(keys), "Vintage object fields differ from the closed schema.")


def _sha(raw):
    return hashlib.sha256(raw).hexdigest()


def _digest(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def _day(value):
    _require(isinstance(value, str) and re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}", value) is not None,
        "Exact ISO dates are required.")
    try:
        return pd.Timestamp(value).as_unit("ns")
    except (ValueError, OverflowError) as error:
        raise ValueError("Invalid normalized calendar date.") from error


def _ordinary(path, *, directory=False):
    _require(".." not in path.parts, "Vintage paths cannot traverse directories.")
    for parent in (path, *path.parents):
        _require(not parent.is_symlink() and not parent.is_junction(), "Vintage links and junctions are forbidden.")
    _require(path.is_dir() if directory else path.is_file(), "An ordinary vintage directory/file is required.")
    if not directory:
        _require(path.stat().st_nlink == 1, "Vintage hard links are forbidden.")


def _read(path, limit):
    _ordinary(path)
    try:
        return read_bounded(path, limit)
    except ModelArtifactError as error:
        raise ValueError("Vintage file is missing, unreadable or exceeds its byte limit.") from error


def _manifest(raw):
    def unique(pairs):
        value = {}
        for key, item in pairs:
            _require(key not in value, "Duplicate manifest keys are forbidden.")
            value[key] = item
        return value

    def invalid(value):
        raise ValueError("Nonfinite manifest values are forbidden.")

    try:
        value = json.loads(raw.decode("utf-8"), object_pairs_hook=unique, parse_constant=invalid)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise ValueError("Invalid vintage manifest JSON.") from error
    _keys(value, set(_FIXED) | {"calendar", "files"})
    _require(type(value["schema_version"]) is int and all(value[key] == expected for key, expected in _FIXED.items()),
        "Unsupported HGBT28 identity, units, adjustment, interval or recursive prefix.")
    calendar = value["calendar"]
    _keys(calendar, {"coverage_start", "coverage_end", "attestation"})
    _require(calendar["attestation"] == "independently_supplied_expected_us_sessions",
        "An independent expected-calendar attestation is required.")
    _require(_day(calendar["coverage_start"]) <= _day(_PREFIXES["daily"])
        and _day(calendar["coverage_start"]) <= _day(calendar["coverage_end"]), "Invalid calendar coverage.")
    files = value["files"]
    _keys(files, _FILES)
    for item in files.values():
        _keys(item, {"bytes", "sha256"})
        _require(type(item["bytes"]) is int and 0 < item["bytes"] <= MAX_CSV_BYTES and _digest(item["sha256"]),
            "Each vintage source requires bounded bytes and SHA-256.")
    _require(sum(item["bytes"] for item in files.values()) <= MAX_TOTAL_BYTES, "Vintage bundle exceeds its total byte limit.")
    return value


def _csv(raw, columns, *, prices=False):
    try:
        rows = list(csv.reader(io.StringIO(raw.decode("utf-8")), strict=True))
        _require(len(rows) > 1 and rows[0] == columns and all(len(row) == len(columns) for row in rows[1:]),
            "CSV headers, nonblank records and record widths must be exact.")
        _require(all(re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}", row[0]) is not None for row in rows[1:]),
            "CSV dates must be exact ISO calendar dates.")
        dtype = {name: "float64" for name in columns if name not in {"date", "symbol", "volume"}}
        if prices:
            _require(all(re.fullmatch(r"[0-9]+", row[-1]) is not None and int(row[-1]) < 2**63 for row in rows[1:]),
                "Normalized volumes require nonnegative Int64 values.")
            dtype.update(symbol="string", volume="Int64")
        frame = pd.read_csv(io.BytesIO(raw), dtype=dtype, float_precision="round_trip", keep_default_na=False)
        frame["date"] = pd.to_datetime(frame.date, format="%Y-%m-%d", errors="raise").astype("datetime64[ns]")
        return frame
    except (UnicodeError, csv.Error, ValueError, TypeError, OverflowError) as error:
        raise ValueError("Invalid normalized vintage CSV.") from error


def _prices(raw, symbols, cutoff):
    frame = _csv(raw, COLUMNS, prices=True)
    try:
        _validate_cleaned(frame, cutoff)
    except DataCleaningError as error:
        raise ValueError("Invalid, unsorted or future OHLCV source.") from error
    _require(set(frame.symbol) == set(symbols), "OHLCV source identities differ from the supported profile.")
    return frame


def _index(raw, cutoff):
    frame = _csv(raw, ["date", "open", "high", "low", "close"])
    values = frame[["open", "high", "low", "close"]]
    _require(frame.date.is_unique and frame.date.is_monotonic_increasing and frame.date.le(cutoff).all()
        and np.isfinite(values.to_numpy()).all() and values.gt(0).all().all()
        and frame.low.le(values[["open", "close"]].min(axis=1)).all()
        and frame.high.ge(values[["open", "close"]].max(axis=1)).all(),
        "Index sources require ordered unique dates and positive finite coherent OHLC through cutoff.")
    return frame.set_index("date").close


def _trailing(daily, monthly, spy):
    frame = research.research_features(daily, monthly, labeled_only=False)
    reference = spy.set_index("date").close
    derived = []
    for _, group in daily.groupby("symbol", sort=True):
        group = group.copy()
        for window in (20, 63, 126, 252):
            group[f"momentum_{window}"] = group.close / group.close.shift(window) - 1
        for window in (50, 200):
            group[f"close_ma{window}"] = group.close / group.close.rolling(window).mean() - 1
        group["volatility_63"] = group.close.pct_change(fill_method=None).rolling(63).std(ddof=0)
        group["drawdown_252"] = group.close / group.close.rolling(252).max() - 1
        for window in (20, 63, 252):
            reference_return = reference / reference.shift(window) - 1
            group[f"relative_momentum_{window}"] = group[f"momentum_{window}"] - group.date.map(reference_return)
        derived.append(group[["date", "symbol"] + _SWING[len(research.RESEARCH_FEATURES):]])
    return frame.merge(pd.concat(derived, ignore_index=True), on=["date", "symbol"], validate="one_to_one")


def prepare_vintage(bundle_dir: Path, *, manifest_sha256: str, as_of_utc: datetime) -> tuple[pd.DataFrame, dict]:
    """Prepare genuine profile inputs from a local attested vintage, without fitting or writing.

    Preparation UTC is sampled internally. A historical as-of, source hashes and
    the supplied calendar do not certify historical point-in-time availability.
    """
    now = datetime.now(UTC)
    _require(type(as_of_utc) is datetime and as_of_utc.tzinfo is not None
        and as_of_utc.utcoffset() == timedelta(0) and as_of_utc <= now, "A nonfuture aware UTC as-of is required.")
    _require(_digest(manifest_sha256), "An external lowercase manifest SHA-256 is required.")
    root = Path(bundle_dir).absolute()
    _ordinary(root, directory=True)
    _require({path.name for path in root.iterdir()} == _FILES | {"manifest.json"}, "Vintage directory contains missing or extra entries.")
    manifest_raw = _read(root / "manifest.json", MAX_MANIFEST_BYTES)
    _require(_sha(manifest_raw) == manifest_sha256, "Vintage manifest SHA-256 differs from its external binding.")
    manifest = _manifest(manifest_raw)
    raw = {}
    for name, item in manifest["files"].items():
        raw[name] = _read(root / name, MAX_CSV_BYTES)
        _require(len(raw[name]) == item["bytes"] and _sha(raw[name]) == item["sha256"], "Vintage source bytes differ from their binding.")
    calendar = _csv(raw["expected-sessions.csv"], ["date"]).date
    coverage = manifest["calendar"]
    _require(calendar.is_unique and calendar.is_monotonic_increasing and calendar.dt.dayofweek.lt(5).all()
        and calendar.between(_day(coverage["coverage_start"]), _day(coverage["coverage_end"])).all()
        and _day(coverage["coverage_end"]).date() >= as_of_utc.date(), "Expected calendar is invalid or stale relative to its attested coverage.")
    closed = calendar.loc[calendar.dt.date < as_of_utc.date()]
    _require(not closed.empty, "No supplied session is closed at the UTC cutoff.")
    cutoff = closed.iloc[-1]
    expected = pd.DatetimeIndex(closed.loc[closed >= _day(_PREFIXES["daily"])])
    _require(not expected.empty and expected[0] == _day(_PREFIXES["daily"]), "The expected daily prefix is incomplete.")
    daily = _prices(raw["stocks.csv"], SYMBOLS, cutoff)
    spy = _prices(raw["spy.csv"], {"SPY"}, cutoff)
    for source in (daily, spy):
        _require(all(pd.DatetimeIndex(group.date).equals(expected) for _, group in source.groupby("symbol", sort=True)),
            "Every stock/SPY history must match the expected full sessions through cutoff.")
    monthly = _prices(raw["monthly.csv"], SYMBOLS, cutoff)
    last_month = cutoff.to_period("M") - 1
    months = pd.date_range(_PREFIXES["monthly"], last_month.start_time, freq="MS")
    _require(len(months) > 0 and all(pd.DatetimeIndex(group.date).equals(months)
        for _, group in monthly.groupby("symbol", sort=True)), "Native monthly prefixes must end at exactly the previous complete month.")
    try:
        frame = _trailing(daily, monthly, spy)
    except (FeatureEngineeringError, DataCleaningError) as error:
        raise ValueError("Insufficient or invalid recursive feature history.") from error

    sources = {name: _index(raw[name], cutoff) for name in ("vix.csv", "vix3m.csv")}
    extras = {name: int((~series.index.isin(expected)).sum()) for name, series in sources.items()}
    vix, vix3m = [sources[name].reindex(expected).where(expected >= "2013-11-01")
        for name in ("vix.csv", "vix3m.csv")]
    term = vix / vix3m - 1
    dates = pd.Series(expected, index=expected)
    macro = pd.DataFrame({"date": expected, "vix_level": vix.shift(2) / 100,
        "vix_change5": vix.shift(2) / vix.shift(7) - 1, "vix_term_ratio": term.shift(2),
        "vix_term_change5": term.shift(2) - term.shift(7), "observation_date": dates.shift(2),
        "lookback_date": dates.shift(7)}).reset_index(drop=True)
    first_position = int(expected.searchsorted(pd.Timestamp("2013-11-01"))) + 7
    _require(first_position < len(expected), "Insufficient macro availability/warmup history.")
    eligible = expected[first_position:]
    frame = frame.loc[frame.date >= eligible[0]].merge(macro, on="date", how="left", validate="many_to_one")
    frame = frame.sort_values(["date", "symbol"]).reset_index(drop=True)
    required_keys = pd.MultiIndex.from_product([eligible, sorted(SYMBOLS)], names=["date", "symbol"])
    _require(pd.MultiIndex.from_frame(frame[["date", "symbol"]]).equals(required_keys)
        and np.isfinite(frame[list(FEATURES)].to_numpy()).all(), "Every eligible stock session needs finite complete features and lagged observations.")
    audit = frame[["date", "symbol", "monthly_available_date", "observation_date", "lookback_date"]]
    result = frame[["date", "symbol"] + list(FEATURES)].copy()
    metadata = {"schema_version": 1, "formula_schema": "hgb_swing20_market28-v1",
        "prepared_at_utc": datetime.now(UTC).isoformat(), "as_of_utc": as_of_utc.isoformat(),
        "cutoff_date": cutoff.date().isoformat(), "manifest_sha256": manifest_sha256,
        "files": manifest["files"], "calendar": manifest["calendar"], "prefixes": manifest["prefixes"],
        "feature_columns": list(FEATURES), "frame_sha256": frame_digest(result), "context_sha256": frame_digest(audit),
        "rows": len(result), "dates": result.date.nunique(),
        "context": {"monthly_available_date": audit.monthly_available_date.iloc[-1].date().isoformat(),
            "macro_observation_date": audit.observation_date.iloc[-1].date().isoformat(),
            "macro_lookback_date": audit.lookback_date.iloc[-1].date().isoformat(), "index_only_rows": extras},
        "limitations": ["Source identities, complete prefixes and calendar coverage are caller attestations, not independently certified.",
            "Actual preparation now and historical as-of/source hashes do not certify past publication or point-in-time vintages.",
            "Current adjusted/index histories can contain retrospective corrections; two SPY-session lags do not prove availability."]}
    return result, metadata
