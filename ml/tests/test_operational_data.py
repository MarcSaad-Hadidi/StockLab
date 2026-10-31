"""Exercise real cleaning/storage; replace only the external HTTP transport."""
from datetime import date
import hashlib
import json

import pandas as pd
import pytest


@pytest.mark.parametrize("today,want", [
    ("2026-10-27", "2026-10-26"), ("2026-10-26", "2026-10-23"),
    ("2026-07-04", "2026-07-02"), ("2026-04-04", "2026-04-02"),
    ("2026-11-27", "2026-11-25"), ("2028-01-01", "2027-12-31"),
    ("2027-06-19", "2027-06-17"),
])
def test_only_completed_us_equity_sessions_are_selected(today, want):
    from stocklab_ml.operational_data import latest_closed_session
    assert latest_closed_session(date.fromisoformat(today)).isoformat() == want


def test_unknown_calendar_year_fails_closed():
    from stocklab_ml.operational_data import latest_closed_session
    with pytest.raises(ValueError):
        latest_closed_session(date(2029, 1, 2))


@pytest.fixture
def market(transport):
    transport.body["values"] = [{"datetime": day.date().isoformat(), "open": "100", "high": "102",
        "low": "99", "close": "101", "volume": "1000"}
        for day in pd.bdate_range("2026-07-01", "2026-10-26")]
    return transport


def prepare(root):
    from stocklab_ml.operational_data import prepare_cycle_dataset
    return prepare_cycle_dataset(root, symbols=("AAPL",), history_start="2026-07-01",
                                 as_of_utc_date=date(2026, 10, 27))


def test_fresh_snapshot_is_immutable_and_retry_spends_no_extra_request(tmp_path, market):
    first = prepare(tmp_path)
    assert first["decision_date"] == "2026-10-26"
    assert first["as_of_utc_date"] == "2026-10-27"
    frame = pd.read_csv(first["dataset_path"])
    assert frame.date.max() == "2026-10-26"
    assert set(frame.symbol) == {"AAPL"}
    assert hashlib.sha256(first["dataset_path"].read_bytes()).hexdigest() == first["sha256"]
    before = {p: p.read_bytes() for p in tmp_path.rglob("*") if p.is_file()}
    assert prepare(tmp_path) == first
    assert len(market.calls) == 1
    assert {p: p.read_bytes() for p in before} == before
    assert json.loads(next(tmp_path.rglob("raw/*.json")).read_text()) == market.body


@pytest.mark.parametrize("bad", ["stale", "non_us", "currency"])
def test_missing_session_or_unsupported_market_never_publishes_a_snapshot(tmp_path, market, bad):
    if bad == "stale": market.body["values"].pop()
    elif bad == "non_us": market.body["meta"]["exchange_timezone"] = "Asia/Tokyo"
    else: market.body["meta"]["currency"] = "CAD"
    with pytest.raises(ValueError):
        prepare(tmp_path)
    assert not list(tmp_path.glob("*/manifest.json"))


@pytest.mark.parametrize("bad", ["dataset", "manifest", "missing", "scope"])
def test_corrupt_or_incompatible_cache_is_not_repaired_or_silently_refetched(tmp_path, market, bad):
    snapshot = prepare(tmp_path)
    manifest = snapshot["dataset_path"].with_name("manifest.json")
    if bad == "dataset": snapshot["dataset_path"].write_text("private-corrupt-input")
    elif bad == "missing": snapshot["dataset_path"].unlink()
    else:
        payload = json.loads(manifest.read_text())
        if bad == "scope": payload["symbols"] = ["MSFT"]
        else: payload["sha256"] = "0" * 64
        manifest.write_text(json.dumps(payload))
    before = {p: p.read_bytes() for p in tmp_path.rglob("*") if p.is_file()}
    with pytest.raises(ValueError):
        prepare(tmp_path)
    assert len(market.calls) == 1
    assert {p: p.read_bytes() for p in before} == before


@pytest.mark.parametrize("symbols", [("aapl",), ("AAPL", "AAPL"), (), ("../AAPL",)])
def test_invalid_scope_does_not_fetch(tmp_path, market, symbols):
    from stocklab_ml.operational_data import prepare_cycle_dataset
    with pytest.raises(ValueError):
        prepare_cycle_dataset(tmp_path, symbols=symbols, history_start="2026-07-01",
                              as_of_utc_date=date(2026, 10, 27))
    assert not market.calls
