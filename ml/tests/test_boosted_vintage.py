"""Pure synthetic source-bound vintage preparation; no fitting or provider calls."""

from datetime import UTC, datetime, timedelta, timezone
from importlib import import_module
import hashlib
import json
import os
from pathlib import Path
import shutil

import numpy as np
import pandas as pd
import pytest

from stocklab_ml import market_risk_research as market, swing_research as swing
from stocklab_ml.versioning.validation import frame_digest


SYMBOLS = ["AAPL", "JNJ", "JPM", "MSFT", "XOM"]
FEATURES = ["return_1d", "rsi_14", "momentum_10", "volatility_20", "close_ma5", "close_ma20",
    "ma_ratio", "relative_volume", "monthly_range_9", "monthly_range_12", "monthly_range_19",
    "monthly_rsi", "monthly_multiplier", "momentum_20", "momentum_63", "momentum_126",
    "momentum_252", "close_ma50", "close_ma200", "volatility_63", "drawdown_252",
    "relative_momentum_20", "relative_momentum_63", "relative_momentum_252", "vix_level",
    "vix_change5", "vix_term_ratio", "vix_term_change5"]
FILES = ["stocks.csv", "monthly.csv", "spy.csv", "vix.csv", "vix3m.csv", "expected-sessions.csv"]
AS_OF = datetime(2014, 3, 14, 12, tzinfo=UTC)


def api():
    return import_module("stocklab_ml.boosted_vintage")


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def write_csv(root, name, frame):
    frame.to_csv(root / name, index=False, date_format="%Y-%m-%d", lineterminator="\n")


def manifest(root):
    return json.loads((root / "manifest.json").read_text())


def rebind(root, change=None):
    value = manifest(root)
    if change:
        change(value)
    value["files"] = {name: {"bytes": (root / name).stat().st_size,
        "sha256": sha((root / name).read_bytes())} for name in FILES}
    raw = (json.dumps(value, sort_keys=True, allow_nan=False) + "\n").encode()
    (root / "manifest.json").write_bytes(raw)
    return sha(raw)


def read_csv(root, name):
    frame = pd.read_csv(root / name, float_precision="round_trip")
    frame["date"] = pd.to_datetime(frame.date).astype("datetime64[ns]")
    if "symbol" in frame:
        frame["symbol"] = frame.symbol.astype("string")
        frame["volume"] = frame.volume.astype("Int64")
    return frame


@pytest.fixture(scope="module")
def template(tmp_path_factory):
    root = tmp_path_factory.mktemp("synthetic-vintage-template")
    dates = pd.bdate_range("2010-01-04", "2014-03-14").astype("datetime64[ns]")
    sessions = dates[dates != pd.Timestamp("2014-03-10")]  # Explicit synthetic closure.
    cutoff = sessions[sessions < pd.Timestamp(AS_OF.date())]

    def bars(days, symbols):
        frames = []
        for number, symbol in enumerate(symbols):
            positions = np.arange(len(days), dtype=float)
            close = 80 + number * 12 + positions * .025 + np.sin(positions / (9 + number)) * 2
            frames.append(pd.DataFrame({"date": days, "symbol": pd.Series(symbol, index=range(len(days)), dtype="string"),
                "open": close - .2, "high": close + 1, "low": close - 1, "close": close,
                "volume": pd.Series((1000 + positions % 37).astype(int), dtype="Int64")}))
        return pd.concat(frames, ignore_index=True)

    write_csv(root, "stocks.csv", bars(cutoff, SYMBOLS))
    write_csv(root, "spy.csv", bars(cutoff, ["SPY"]))
    write_csv(root, "monthly.csv", bars(pd.date_range("2010-01-01", "2014-02-01", freq="MS"), SYMBOLS))
    write_csv(root, "expected-sessions.csv", pd.DataFrame({"date": sessions}))
    for name, level in [("vix.csv", 18.), ("vix3m.csv", 25.)]:
        positions = np.arange(len(dates) - 1, dtype=float)
        close = level + positions * .004 + np.sin(positions / 9) * .3
        write_csv(root, name, pd.DataFrame({"date": dates[:-1], "open": close - .1,
            "high": close + .5, "low": close - .5, "close": close}))
    identities = {symbol: {"mic": "XNAS" if symbol in {"AAPL", "MSFT"} else "XNYS",
        "kind": "Common Stock", "currency": "USD", "timezone": "America/New_York"} for symbol in SYMBOLS}
    identities["SPY"] = {"mic": "ARCX", "kind": "ETF", "currency": "USD", "timezone": "America/New_York"}
    value = {"schema_version": 1, "profile": "hgb_swing20_market28", "identities": identities,
        "price_basis": {"stocks.csv": "splits", "monthly.csv": "splits", "spy.csv": "all"},
        "intervals": {"stocks.csv": "1day", "monthly.csv": "1month", "spy.csv": "1day",
            "vix.csv": "1day", "vix3m.csv": "1day"}, "macro_units": "index_points",
        "prefixes": {"daily": "2010-01-04", "monthly": "2010-01-01"},
        "recursive_prefix_attestation": "complete_recursive_prefixes_supplied",
        "calendar": {"coverage_start": "2010-01-01", "coverage_end": "2014-03-14",
            "attestation": "independently_supplied_expected_us_sessions"}, "files": {}}
    (root / "manifest.json").write_text(json.dumps(value))
    rebind(root)
    return root


@pytest.fixture
def bundle(template, tmp_path):
    root = tmp_path / "bundle"
    shutil.copytree(template, root)
    return root


def prepare(root, *, as_of=AS_OF, digest=None):
    return api().prepare_vintage(root, manifest_sha256=digest or sha((root / "manifest.json").read_bytes()), as_of_utc=as_of)


def expected(root):
    prices, monthly, spy = [read_csv(root, name) for name in ("stocks.csv", "monthly.csv", "spy.csv")]
    frame = swing.swing_features(prices, monthly, spy)
    calendar = pd.DatetimeIndex(spy.date)
    vix, vix3m = [read_csv(root, name).set_index("date").close.reindex(calendar)
        .where(calendar >= "2013-11-01") for name in ("vix.csv", "vix3m.csv")]
    term = vix / vix3m - 1
    days = pd.Series(calendar, index=calendar)
    macro = pd.DataFrame({"date": calendar, "vix_level": vix.shift(2) / 100,
        "vix_change5": vix.shift(2) / vix.shift(7) - 1, "vix_term_ratio": term.shift(2),
        "vix_term_change5": term.shift(2) - term.shift(7), "observation_date": days.shift(2),
        "lookback_date": days.shift(7)}).dropna(subset=market.FEATURES).reset_index(drop=True)
    return market.attach(frame, macro)[["date", "symbol"] + FEATURES]


def test_entire_target_free_matrix_matches_frozen_formulas_bit_exactly(bundle):
    wanted = expected(bundle)
    actual, metadata = prepare(bundle)
    pd.testing.assert_frame_equal(actual, wanted, check_exact=True)
    assert np.array_equal(actual[FEATURES].to_numpy().view("uint64"), wanted[FEATURES].to_numpy().view("uint64"))
    assert actual.columns.tolist() == ["date", "symbol"] + FEATURES
    assert actual.date.max() == pd.Timestamp("2014-03-13")
    assert actual.date.min() == pd.Timestamp("2013-11-12")
    assert metadata["frame_sha256"] == frame_digest(actual)
    assert metadata["feature_columns"] == FEATURES
    assert metadata["rows"] == len(actual)


def test_preparation_never_calls_target_builders_or_frozen_market_loader(bundle, monkeypatch):
    def forbidden(*args, **kwargs):
        raise AssertionError("Target builders and frozen acquisition loader are forbidden")
    monkeypatch.setattr(swing, "swing_features", forbidden)
    monkeypatch.setattr(swing, "add_targets", forbidden)
    monkeypatch.setattr(market, "load_market_risk", forbidden)
    actual, _ = prepare(bundle)
    assert not any(name.startswith(("target", "forward", "excess")) for name in actual)


def test_actual_preparation_time_is_separate_from_historical_asof_and_all_inputs_unchanged(bundle, monkeypatch):
    before = {path.name: path.read_bytes() for path in bundle.iterdir()}
    monkeypatch.setenv("OMP_NUM_THREADS", "UNCHANGED-SENTINEL")
    started = datetime.now(UTC)
    frame, metadata = prepare(bundle)
    ended = datetime.now(UTC)
    assert started <= datetime.fromisoformat(metadata["prepared_at_utc"]) <= ended
    assert metadata["as_of_utc"] == AS_OF.isoformat()
    assert metadata["cutoff_date"] == "2014-03-13"
    assert metadata["prefixes"] == {"daily": "2010-01-04", "monthly": "2010-01-01"}
    assert metadata["manifest_sha256"] == sha(before["manifest.json"])
    assert metadata["files"] == manifest(bundle)["files"]
    assert metadata["context"]["monthly_available_date"] == "2014-02-28"
    assert metadata["context"]["macro_observation_date"] == "2014-03-11"
    assert metadata["context"]["macro_lookback_date"] == "2014-03-03"
    assert metadata["context"]["index_only_rows"] == {"vix.csv": 1, "vix3m.csv": 1}
    assert "not" in " ".join(metadata["limitations"]).lower()
    assert os.environ["OMP_NUM_THREADS"] == "UNCHANGED-SENTINEL"
    assert {path.name: path.read_bytes() for path in bundle.iterdir()} == before
    assert frame.date.dtype == np.dtype("datetime64[ns]")


def test_index_only_dates_are_excluded_before_both_macro_lags(bundle):
    frame, _ = prepare(bundle)
    spy = read_csv(bundle, "spy.csv")
    vix, vix3m = [read_csv(bundle, name).set_index("date").close for name in ("vix.csv", "vix3m.csv")]
    observation, lookback = spy.date.iloc[-3], spy.date.iloc[-8]
    term = vix / vix3m - 1
    last = frame.iloc[-1]
    assert last.vix_level == vix.loc[observation] / 100
    assert last.vix_change5 == vix.loc[observation] / vix.loc[lookback] - 1
    assert last.vix_term_ratio == term.loc[observation]
    assert last.vix_term_change5 == term.loc[observation] - term.loc[lookback]


def test_unneeded_two_macro_tail_observations_do_not_require_filling(bundle):
    wanted, _ = prepare(bundle)
    spy = read_csv(bundle, "spy.csv")
    for name in ("vix.csv", "vix3m.csv"):
        frame = read_csv(bundle, name)
        write_csv(bundle, name, frame.loc[frame.date <= spy.date.iloc[-3]])
    rebind(bundle)
    actual, _ = prepare(bundle)
    pd.testing.assert_frame_equal(actual, wanted, check_exact=True)


def test_prefix_results_do_not_change_when_later_observations_are_removed(bundle):
    full, _ = prepare(bundle)
    for name in ("stocks.csv", "spy.csv", "vix.csv", "vix3m.csv"):
        frame = read_csv(bundle, name)
        write_csv(bundle, name, frame.loc[frame.date <= "2014-03-05"])
    rebind(bundle)
    prefix, _ = prepare(bundle, as_of=datetime(2014, 3, 6, tzinfo=UTC))
    pd.testing.assert_frame_equal(prefix, full.loc[full.date <= "2014-03-05"].reset_index(drop=True), check_exact=True)


def test_insufficient_recursive_warmup_is_refused(bundle):
    for name in ("stocks.csv", "spy.csv", "vix.csv", "vix3m.csv"):
        frame = read_csv(bundle, name)
        write_csv(bundle, name, frame.loc[frame.date <= "2010-12-31"])
    monthly = read_csv(bundle, "monthly.csv")
    write_csv(bundle, "monthly.csv", monthly.loc[monthly.date <= "2010-11-01"])
    rebind(bundle)
    with pytest.raises(ValueError):
        prepare(bundle, as_of=datetime(2011, 1, 3, tzinfo=UTC))


@pytest.mark.parametrize("bad", ["naive", "offset", "future", "date_only"])
def test_invalid_utc_cutoffs_fail(bundle, bad):
    values = {"naive": AS_OF.replace(tzinfo=None), "offset": AS_OF.replace(tzinfo=timezone(timedelta(hours=1))),
        "future": datetime.now(UTC) + timedelta(days=1), "date_only": AS_OF.date()}
    with pytest.raises(ValueError):
        prepare(bundle, as_of=values[bad])


@pytest.mark.parametrize("bad", ["schema_bool", "profile", "extra", "identity", "identity_extra", "basis",
    "interval", "units", "daily_seed", "monthly_seed", "seed_attestation", "coverage", "coverage_start",
    "calendar_attestation", "calendar_extra", "files_extra", "file_extra", "size_bool", "sha", "manifest_sha"])
def test_closed_manifest_identity_prefix_hash_and_calendar_contract(bundle, bad):
    value = manifest(bundle)
    if bad == "schema_bool": value["schema_version"] = True
    elif bad == "profile": value["profile"] = "hgb_swing20_market32"
    elif bad == "extra": value["extra"] = 1
    elif bad == "identity": value["identities"]["SPY"]["mic"] = "XNYS"
    elif bad == "identity_extra": value["identities"]["SPY"]["extra"] = 1
    elif bad == "basis": value["price_basis"]["spy.csv"] = "splits"
    elif bad == "interval": value["intervals"]["monthly.csv"] = "daily_derived"
    elif bad == "units": value["macro_units"] = "percent_fraction"
    elif bad == "daily_seed": value["prefixes"]["daily"] = "2012-01-03"
    elif bad == "monthly_seed": value["prefixes"]["monthly"] = "2012-01-01"
    elif bad == "seed_attestation": value["recursive_prefix_attestation"] = "last_252_bars"
    elif bad == "coverage": value["calendar"]["coverage_end"] = "2014-03-13"
    elif bad == "coverage_start": value["calendar"]["coverage_start"] = "2010-01-05"
    elif bad == "calendar_attestation": value["calendar"]["attestation"] = "derived_from_spy"
    elif bad == "calendar_extra": value["calendar"]["extra"] = 1
    elif bad == "files_extra": value["files"]["../escape.csv"] = value["files"]["spy.csv"]
    elif bad == "file_extra": value["files"]["spy.csv"]["extra"] = 1
    elif bad == "size_bool": value["files"]["spy.csv"]["bytes"] = True
    elif bad == "sha": value["files"]["spy.csv"]["sha256"] = "X" * 64
    raw = (json.dumps(value) + "\n").encode()
    (bundle / "manifest.json").write_bytes(raw)
    with pytest.raises(ValueError):
        prepare(bundle, digest="a" * 64 if bad == "manifest_sha" else sha(raw))


@pytest.mark.parametrize("bad", ["duplicate_json", "nonfinite_json", "extra_file", "subdirectory", "missing_file", "modified_file"])
def test_ambiguous_json_and_unbound_directory_contents_fail(bundle, bad):
    if bad == "duplicate_json":
        raw = (bundle / "manifest.json").read_bytes().replace(b'"schema_version": 1', b'"schema_version": 1, "schema_version": 1')
        (bundle / "manifest.json").write_bytes(raw)
    elif bad == "nonfinite_json":
        raw = (bundle / "manifest.json").read_bytes().replace(b'"schema_version": 1', b'"schema_version": NaN')
        (bundle / "manifest.json").write_bytes(raw)
    elif bad == "extra_file": (bundle / "extra.txt").write_text("unbound")
    elif bad == "subdirectory": (bundle / "extra").mkdir()
    elif bad == "missing_file": (bundle / "spy.csv").unlink()
    else: (bundle / "spy.csv").write_bytes((bundle / "spy.csv").read_bytes() + b"\n")
    with pytest.raises(ValueError):
        prepare(bundle)


@pytest.mark.parametrize("bad", ["stock_gap", "spy_gap", "both_gap", "stock_identity", "duplicate", "unsorted",
    "future", "monthly_partial", "monthly_stale", "monthly_gap", "monthly_day", "month_seed_gap",
    "calendar_missing", "calendar_duplicate", "calendar_unsorted", "calendar_weekend", "volume", "ohlc",
    "macro_missing", "macro_nan", "macro_infinite", "macro_duplicate", "macro_unsorted", "macro_future",
    "csv_header", "csv_blank", "csv_width", "csv_date", "complex"])
def test_invalid_or_incomplete_source_csv_never_repairs_or_silently_drops_rows(bundle, bad):
    name = "stocks.csv"
    if bad.startswith("monthly") or bad == "month_seed_gap": name = "monthly.csv"
    elif bad.startswith("calendar"): name = "expected-sessions.csv"
    elif bad.startswith("macro"): name = "vix.csv"
    elif bad == "spy_gap": name = "spy.csv"
    frame = read_csv(bundle, name)
    if bad in {"stock_gap", "spy_gap"}: frame = frame.drop(frame.index[-1])
    elif bad == "both_gap":
        for filename in ("stocks.csv", "spy.csv"):
            data = read_csv(bundle, filename)
            write_csv(bundle, filename, data.loc[data.date != "2014-03-13"])
    elif bad == "stock_identity": frame.loc[0, "symbol"] = "OTHER"
    elif bad in {"duplicate", "macro_duplicate", "calendar_duplicate"}: frame = pd.concat([frame, frame.iloc[[-1]]])
    elif bad in {"unsorted", "macro_unsorted", "calendar_unsorted"}: frame = frame.iloc[::-1]
    elif bad in {"future", "macro_future"}: frame.loc[frame.index[-1], "date"] = pd.Timestamp("2014-03-14")
    elif bad == "monthly_partial":
        added = frame.groupby("symbol", sort=True).tail(1).copy()
        added["date"] = pd.Timestamp("2014-03-01")
        frame = pd.concat([frame, added]).sort_values(["symbol", "date"])
    elif bad == "monthly_stale": frame = frame.loc[frame.date != "2014-02-01"]
    elif bad == "monthly_gap": frame = frame.drop(frame.index[25])
    elif bad == "monthly_day": frame.loc[0, "date"] = pd.Timestamp("2010-01-02")
    elif bad == "month_seed_gap": frame = frame.loc[frame.date != "2010-01-01"]
    elif bad == "calendar_missing": frame = frame.loc[frame.date != "2014-03-05"]
    elif bad == "calendar_weekend": frame.loc[0, "date"] = pd.Timestamp("2010-01-03")
    elif bad == "volume": frame["volume"] = frame.volume.astype(float); frame.loc[0, "volume"] = .5
    elif bad == "ohlc": frame.loc[0, "low"] = frame.loc[0, "high"] + 1
    elif bad == "macro_missing": frame = frame.loc[frame.date != "2014-03-11"]
    elif bad == "macro_nan": frame.loc[0, "close"] = np.nan
    elif bad == "macro_infinite": frame.loc[0, "close"] = np.inf
    if bad != "both_gap": write_csv(bundle, name, frame)
    if bad.startswith("csv_") or bad == "complex":
        raw = (bundle / name).read_text()
        if bad == "csv_header": raw = raw.replace("date,symbol,", "date,date,", 1)
        elif bad == "csv_blank": raw += "\n"
        elif bad == "csv_width": raw += "2014-03-01,AAPL\n"
        elif bad == "csv_date": raw = raw.replace("2010-01-04", "2010-01-04T00:00:00", 1)
        else:
            lines = raw.splitlines()
            cells = lines[1].split(",")
            cells[5] = "1+2j"
            lines[1] = ",".join(cells)
            raw = "\n".join(lines) + "\n"
        (bundle / name).write_text(raw)
    rebind(bundle)
    before = {path.name: path.read_bytes() for path in bundle.iterdir()}
    with pytest.raises(ValueError):
        prepare(bundle)
    assert {path.name: path.read_bytes() for path in bundle.iterdir()} == before


def test_bound_size_limits_fail_without_parsing_large_inputs(bundle, monkeypatch):
    module = api()
    monkeypatch.setattr(module, "MAX_TOTAL_BYTES", sum(item["bytes"] for item in manifest(bundle)["files"].values()) - 1)
    with pytest.raises(ValueError): prepare(bundle)
    monkeypatch.setattr(module, "MAX_TOTAL_BYTES", 128 * 1024 * 1024)
    monkeypatch.setattr(module, "MAX_CSV_BYTES", 16)
    with pytest.raises(ValueError): prepare(bundle)
    monkeypatch.setattr(module, "MAX_MANIFEST_BYTES", 16)
    with pytest.raises(ValueError): prepare(bundle)


@pytest.mark.parametrize("kind", ["root_symlink", "file_symlink", "file_hardlink"])
def test_links_are_rejected(bundle, tmp_path, kind):
    target = bundle
    try:
        if kind == "root_symlink":
            target = tmp_path / "linked-bundle"
            target.symlink_to(bundle, target_is_directory=True)
        else:
            source = tmp_path / "spy-original.csv"
            (bundle / "spy.csv").rename(source)
            if kind == "file_symlink": (bundle / "spy.csv").symlink_to(source)
            else: os.link(source, bundle / "spy.csv")
    except OSError as error:
        pytest.skip(f"Local filesystem cannot create the test link: {error.__class__.__name__}")
    with pytest.raises(ValueError): prepare(target)
