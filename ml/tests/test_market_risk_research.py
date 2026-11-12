"""Bound offline market features use only postlaunch, lagged SPY sessions."""

import csv
import hashlib
import importlib
import io
import json
from pathlib import Path

import numpy as np
import pandas as pd
import pytest


SCHEMA = ["DATE", "OPEN", "HIGH", "LOW", "CLOSE"]
FEATURES = ["vix_level", "vix_change5", "vix_term_ratio", "vix_term_change5"]
URLS = {symbol: "https://cdn-api.cboe.com/api/global/us_indices/daily_prices/"
        + symbol + "_History.csv" for symbol in ("VIX", "VIX3M")}
SPY_SHA = "a" * 64


def market():
    return importlib.import_module("stocklab_ml.market_risk_research")


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def rebind(root):
    names = ["VIX_History.csv", "VIX3M_History.csv", "cboe-source-identity.json",
             "cboe-daily-quality.json", "cboe-quality-self-check.json", "cboe-feasibility.md",
             "cboe_source_identity_probe.py", "cboe_daily_quality_probe.py"]
    manifest = {}
    for name in names:
        raw = (root / name).read_bytes()
        manifest[name] = {"bytes": len(raw), "sha256": digest(raw)}
    write_json(root / "cboe-artifact-manifest.json", manifest)


def source_report(root, symbol, spy):
    raw = (root / f"{symbol}_History.csv").read_bytes()
    rows = list(csv.reader(io.StringIO(raw.decode("utf-8"))))[1:]
    dates = pd.to_datetime([row[0] for row in rows], format="%m/%d/%Y")
    window = dates[(dates >= "2010-01-01") & (dates <= "2026-11-11")]
    errors = []
    for line, row in enumerate(rows, 2):
        opening, high, low, close = map(float, row[1:])
        if not (low <= opening <= high and low <= close <= high):
            errors.append({"line": line, "date": dates[line-2].date().isoformat(),
                           "issues": ["ohlc_order"], "raw_values": row[1:]})
    return {
        "url": URLS[symbol], "status": 200, "headers": {"Location": None},
        "raw_path": "Z:/DO-NOT-READ/ignored.csv", "bytes": len(raw), "sha256": digest(raw),
        "requested_at_utc": "2026-11-12T12:00:00+00:00",
        "completed_at_utc": "2026-11-12T12:00:01+00:00",
        "schema": SCHEMA, "schema_exact_match": True, "row_count": len(rows),
        "parsed_date_count": len(rows), "first_date": dates.min().date().isoformat(),
        "last_date": dates.max().date().isoformat(), "dates_strictly_ascending": True,
        "duplicate_dates": {}, "invalid_date_rows": [], "malformed_rows": [],
        "quote_errors_all_history": errors,
        "quote_errors_analysis_window": [error for error in errors if error["date"] >= "2010-01-01"],
        "window_unique_dates": len(window), "window_first": window.min().date().isoformat(),
        "window_last": window.max().date().isoformat(),
        "overlap_spy_open_dates": len(window.intersection(spy.date)),
        "missing_spy_open_dates": [day.date().isoformat() for day in pd.DatetimeIndex(spy.date).difference(window)],
        "dates_not_in_spy_calendar_in_window": [day.date().isoformat() for day in window.difference(spy.date)],
        "weekend_dates_in_window": [day.date().isoformat() for day in window if day.weekday() >= 5],
        "pre_2010_rows": int((dates < "2010-01-01").sum()),
        "after_cutoff_rows_audit_only": [day.date().isoformat() for day in dates if day > pd.Timestamp("2026-11-11")],
        "raw_values_changed_or_filled": False,
    }


def refresh_quality(root, spy):
    quality_path = root / "cboe-daily-quality.json"
    quality = json.loads(quality_path.read_text())
    quality["sources"] = {symbol: source_report(root, symbol, spy) for symbol in URLS}
    write_json(quality_path, quality)
    check = {symbol: {
        "all_history_ohlc_order_issues": len(item["quote_errors_all_history"]),
        "extra_spy_calendar_dates": len(item["dates_not_in_spy_calendar_in_window"]),
        "raw_hash_verified": True, "all_close_positive_finite": True, "latest": item["last_date"],
    } for symbol, item in quality["sources"].items()}
    check.update(common_window_dates=len(spy), scope_hash_verified=True)
    write_json(root / "cboe-quality-self-check.json", check)
    rebind(root)


@pytest.fixture
def bundle(tmp_path):
    root = tmp_path / "source"
    root.mkdir()
    dates = pd.bdate_range("2013-10-21", "2014-01-10").difference(
        pd.to_datetime(["2013-11-28", "2013-12-25", "2014-01-01"]))
    spy = pd.DataFrame({"date": dates.astype("datetime64[ns]"), "symbol": "SPY"})
    for name in ["root-probe-scope.md", "cboe-feasibility.md",
                 "cboe_source_identity_probe.py", "cboe_daily_quality_probe.py"]:
        (root / name).write_text("Synthetic evidence, never execute.\n", encoding="utf-8")
    scope_sha = digest((root / "root-probe-scope.md").read_bytes())
    docs = ["https://www.cboe.com/tradable_products/vix/vix_historical_data",
            "https://www.cboe.com/us/indices/dashboard/vix3m/"]
    identity = {"scope_sha256": scope_sha, "csv_requests": 0,
                "retrieved_at_utc": "2026-11-12T12:00:00+00:00", "docs": [
        {"url": doc, "status": 200, "headers": {"Location": None},
         "sha256": "b" * 64, "bytes": 100, "daily_history_links": [URLS[symbol]]}
        for doc, symbol in zip(docs, URLS)]}
    write_json(root / "cboe-source-identity.json", identity)
    for symbol in URLS:
        rows = [["12/31/2009", "10", "9" if symbol == "VIX" else "11", "8", "8.5"]]
        ready = 0
        for day in dates:
            close = 900.0
            if day >= pd.Timestamp("2013-11-01"):
                close = 20.0 + ready if symbol == "VIX" else 30.0 + ready / 2
                ready += 1
            rows.append([day.strftime("%m/%d/%Y"), close, close + 1, close - 1, close])
        if symbol == "VIX":
            rows.append(["11/02/2013", "700", "701", "699", "700"])
        rows.sort(key=lambda row: pd.to_datetime(row[0], format="%m/%d/%Y"))
        output = io.StringIO()
        writer = csv.writer(output, lineterminator="\n")
        writer.writerow(SCHEMA)
        writer.writerows(rows)
        (root / f"{symbol}_History.csv").write_text(output.getvalue(), encoding="utf-8")
    quality = {
        "scope_sha256": scope_sha,
        "source_identity_sha256": digest((root / "cboe-source-identity.json").read_bytes()),
        "script_sha256": digest((root / "cboe_daily_quality_probe.py").read_bytes()),
        "analysis_start": "2010-01-01", "asof_cutoff": "2026-11-11", "requests_limit": 2,
        "redirects_followed": False, "retries": 0, "credentials_or_environment_proxies": False,
        "spy_calendar": {"path": "Z:/DO-NOT-READ/spy.csv", "sha256": SPY_SHA,
                         "rows": len(spy), "window_rows": len(spy),
                         "first": dates.min().date().isoformat(), "last": dates.max().date().isoformat()},
    }
    write_json(root / "cboe-daily-quality.json", quality)
    refresh_quality(root, spy)
    return root, spy


def test_fixed_features_ignore_prelaunch_quotes_and_extra_source_dates(bundle):
    root, spy = bundle
    original = spy.copy(deep=True)
    macro, commitment = market().load_market_risk(root, spy, SPY_SHA)
    assert macro.columns.tolist() == ["date"] + FEATURES + ["observation_date", "lookback_date"]
    assert macro.iloc[0].date == pd.Timestamp("2013-11-12")
    assert macro.iloc[0].observation_date == pd.Timestamp("2013-11-08")
    assert macro.iloc[0].lookback_date == pd.Timestamp("2013-11-01")
    assert macro.iloc[0][FEATURES].tolist() == pytest.approx([
        .25, .25, -3 / 13, 4 / 39])
    assert all(str(macro[column].dtype) == "datetime64[ns]" for column in ["date", "observation_date", "lookback_date"])
    assert commitment["source_audit"]["VIX"]["excluded_non_spy_dates"] == ["2013-11-02"]
    assert commitment["source_audit"]["VIX"]["pre_2010_ohlc_order_issues"] == 1
    assert commitment["postlaunch_warmup_spy_sessions"] == 7
    assert commitment["manifest"]["sha256"] == digest((root / "cboe-artifact-manifest.json").read_bytes())
    assert commitment["root_probe_scope"]["sha256"] == digest((root / "root-probe-scope.md").read_bytes())
    assert commitment["files"]["VIX_History.csv"]["sha256"] == digest((root / "VIX_History.csv").read_bytes())
    json.dumps(commitment, allow_nan=False)
    pd.testing.assert_frame_equal(spy, original)


def test_changes_after_cutoff_observation_cannot_change_earlier_features(bundle):
    root, spy = bundle
    before, _ = market().load_market_risk(root, spy, SPY_SHA)
    for symbol in URLS:
        path = root / f"{symbol}_History.csv"
        rows = list(csv.reader(io.StringIO(path.read_text())))
        for row in rows[1:]:
            if pd.to_datetime(row[0], format="%m/%d/%Y") >= pd.Timestamp("2013-12-03"):
                row[1:] = ["50", "51", "49", "50"]
        output = io.StringIO()
        csv.writer(output, lineterminator="\n").writerows(rows)
        path.write_text(output.getvalue(), encoding="utf-8")
    refresh_quality(root, spy)
    after, _ = market().load_market_risk(root, spy, SPY_SHA)
    pd.testing.assert_frame_equal(before.loc[before.date <= "2013-12-04"],
                                  after.loc[after.date <= "2013-12-04"])
    assert before.loc[before.date.eq("2013-12-05"), "vix_level"].item() != after.loc[after.date.eq("2013-12-05"), "vix_level"].item()


def test_attach_preserves_all_later_keys_values_order_and_inputs(bundle):
    root, spy = bundle
    macro, _ = market().load_market_risk(root, spy, SPY_SHA)
    frame = pd.concat([spy.assign(symbol="A", value=1), spy.assign(symbol="B", value=2)], ignore_index=True)
    original, original_macro = frame.copy(deep=True), macro.copy(deep=True)
    result = market().attach(frame, macro)
    expected = frame.loc[frame.date >= "2013-11-12"].reset_index(drop=True)
    pd.testing.assert_frame_equal(result[frame.columns], expected)
    assert result.columns.tolist() == frame.columns.tolist() + FEATURES + ["observation_date", "lookback_date"]
    pd.testing.assert_frame_equal(frame, original)
    pd.testing.assert_frame_equal(macro, original_macro)


@pytest.mark.parametrize("change", ["gap", "trailing_gap", "duplicate", "unsorted", "nonfinite", "shadow", "duplicate_column", "late_prefix", "audit_future", "audit_string"])
def test_attach_rejects_coverage_or_schema_changes(bundle, change):
    root, spy = bundle
    macro, _ = market().load_market_risk(root, spy, SPY_SHA)
    frame = spy.copy()
    if change == "gap":
        macro = macro.drop(macro.index[5]).reset_index(drop=True)
    elif change == "trailing_gap":
        macro = macro.iloc[:-1].copy()
    elif change == "duplicate":
        macro = pd.concat([macro.iloc[:1], macro], ignore_index=True)
    elif change == "unsorted":
        macro = macro.iloc[::-1].reset_index(drop=True)
    elif change == "nonfinite":
        macro.loc[0, "vix_level"] = np.inf
    elif change == "shadow":
        frame["observation_date"] = frame.date
    elif change == "duplicate_column":
        frame = pd.concat([frame, frame[["symbol"]]], axis=1)
    elif change == "late_prefix":
        macro = macro.loc[macro.date > "2014-01-02"].reset_index(drop=True)
    elif change == "audit_future":
        macro.loc[0, "observation_date"] = macro.date.iloc[0]
    else:
        macro["observation_date"] = macro.observation_date.astype(str)
    with pytest.raises(ValueError):
        market().attach(frame, macro)


@pytest.mark.parametrize("change", ["raw", "scope", "required", "escape", "absolute", "drive", "bad_hash", "wrong_bytes"])
def test_manifest_and_scope_tampering_fails_closed(bundle, change):
    root, spy = bundle
    manifest_path = root / "cboe-artifact-manifest.json"
    manifest = json.loads(manifest_path.read_text())
    if change in ("raw", "scope"):
        path = root / ("VIX_History.csv" if change == "raw" else "root-probe-scope.md")
        path.write_bytes(path.read_bytes() + b"tampered")
    elif change == "required":
        del manifest["cboe-feasibility.md"]
    elif change in ("escape", "absolute", "drive"):
        name = {"escape": "../outside.txt", "absolute": "/outside.txt", "drive": "C:\\outside.txt"}[change]
        manifest[name] = {"bytes": 1, "sha256": "c" * 64}
    elif change == "bad_hash":
        manifest["VIX_History.csv"]["sha256"] = "bad"
    else:
        manifest["VIX_History.csv"]["bytes"] += 1
    write_json(manifest_path, manifest)
    with pytest.raises(ValueError):
        market().load_market_risk(root, spy, SPY_SHA)


@pytest.mark.parametrize("change", ["wrong_spy", "proxy", "redirect", "retry", "cutoff", "source_url", "official_doc", "link", "raw_metadata", "quality_count", "self_check", "source_identity", "schema", "source_status", "doc_status"])
def test_rehashed_evidence_must_still_match_the_fixed_source_contract(bundle, change):
    root, spy = bundle
    quality_path = root / "cboe-daily-quality.json"
    quality = json.loads(quality_path.read_text())
    if change == "wrong_spy":
        quality["spy_calendar"]["sha256"] = "c" * 64
    elif change == "proxy":
        quality["credentials_or_environment_proxies"] = True
    elif change == "redirect":
        quality["sources"]["VIX"]["headers"]["Location"] = "https://example.org"
    elif change == "retry":
        quality["retries"] = 1
    elif change == "cutoff":
        quality["asof_cutoff"] = "2026-11-12"
    elif change == "source_url":
        quality["sources"]["VIX"]["url"] = "https://example.org/VIX.csv"
    elif change in ("official_doc", "link", "doc_status"):
        path = root / "cboe-source-identity.json"
        identity = json.loads(path.read_text())
        if change == "official_doc":
            identity["docs"][0]["url"] = "https://example.org"
        elif change == "link":
            identity["docs"][0]["daily_history_links"] = []
        else:
            identity["docs"][0]["status"] = 302
        write_json(path, identity)
        quality["source_identity_sha256"] = digest(path.read_bytes())
    elif change == "raw_metadata":
        quality["sources"]["VIX"]["bytes"] += 1
    elif change == "quality_count":
        quality["sources"]["VIX"]["window_unique_dates"] += 1
    elif change == "self_check":
        path = root / "cboe-quality-self-check.json"
        check = json.loads(path.read_text())
        check["VIX"]["extra_spy_calendar_dates"] = 0
        write_json(path, check)
    elif change == "source_identity":
        quality["source_identity_sha256"] = "c" * 64
    elif change == "source_status":
        quality["sources"]["VIX"]["status"] = 302
    else:
        quality["sources"]["VIX"]["schema"] = SCHEMA[::-1]
    write_json(quality_path, quality)
    rebind(root)
    with pytest.raises(ValueError):
        market().load_market_risk(root, spy, SPY_SHA)


@pytest.mark.parametrize("change", ["missing", "duplicate", "order", "date", "date_format", "nan", "zero", "ohlc", "header", "malformed"])
def test_rehashed_raw_sources_are_parsed_without_repairs(bundle, change):
    root, spy = bundle
    path = root / "VIX_History.csv"
    rows = list(csv.reader(io.StringIO(path.read_text())))
    if change == "missing":
        del rows[15]
    elif change == "duplicate":
        rows.insert(15, rows[15].copy())
    elif change == "order":
        rows[15], rows[16] = rows[16], rows[15]
    elif change == "date":
        rows[15][0] = "11/31/2013"
    elif change == "date_format":
        rows[15][0] = "11/8/2013"
    elif change in ("nan", "zero", "ohlc"):
        rows[15][2] = {"nan": "nan", "zero": "0", "ohlc": "1"}[change]
    elif change == "header":
        rows[0][0] = "Date"
    else:
        rows[15].append("extra")
    output = io.StringIO()
    csv.writer(output, lineterminator="\n").writerows(rows)
    path.write_text(output.getvalue(), encoding="utf-8")
    quality_path = root / "cboe-daily-quality.json"
    quality = json.loads(quality_path.read_text())
    quality["sources"]["VIX"].update(bytes=len(path.read_bytes()), sha256=digest(path.read_bytes()))
    write_json(quality_path, quality)
    rebind(root)
    with pytest.raises(ValueError):
        market().load_market_risk(root, spy, SPY_SHA)


@pytest.mark.parametrize("change", ["duplicate", "unsorted", "nan", "time", "string", "non_spy", "hash"])
def test_supplied_spy_calendar_is_exact_and_unambiguous(bundle, change):
    root, spy = bundle
    supplied_sha = SPY_SHA
    if change == "duplicate":
        spy = pd.concat([spy.iloc[:1], spy], ignore_index=True)
    elif change == "unsorted":
        spy = spy.iloc[::-1].reset_index(drop=True)
    elif change == "nan":
        spy.loc[0, "date"] = pd.NaT
    elif change == "time":
        spy.loc[0, "date"] += pd.Timedelta(hours=1)
    elif change == "string":
        spy["date"] = spy.date.astype(str)
    elif change == "non_spy":
        spy["symbol"] = "QQQ"
    else:
        supplied_sha = "invalid"
    with pytest.raises(ValueError):
        market().load_market_risk(root, spy, supplied_sha)


def test_symlinked_manifest_member_is_rejected_before_reading(bundle):
    root, spy = bundle
    source = root / "cboe-feasibility.md"
    target = root.parent / "outside.md"
    target.write_text("outside")
    source.unlink()
    try:
        source.symlink_to(target)
    except OSError:
        pytest.skip("Windows account lacks symlink creation privilege")
    with pytest.raises(ValueError):
        market().load_market_risk(root, spy, SPY_SHA)


def test_non_json_numeric_metadata_cannot_enter_the_commitment(bundle):
    root, spy = bundle
    path = root / "cboe-daily-quality.json"
    quality = json.loads(path.read_text())
    quality["sources"]["VIX"]["headers"]["Age"] = float("nan")
    write_json(path, quality)
    rebind(root)
    with pytest.raises(ValueError):
        market().load_market_risk(root, spy, SPY_SHA)


def test_duplicate_json_keys_cannot_shadow_source_evidence(bundle):
    root, spy = bundle
    path = root / "cboe-artifact-manifest.json"
    raw = path.read_text()
    path.write_text(raw.replace("{", '{"VIX_History.csv": {},', 1), encoding="utf-8")
    with pytest.raises(ValueError):
        market().load_market_risk(root, spy, SPY_SHA)
