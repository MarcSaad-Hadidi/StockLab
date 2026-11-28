"""Resolve a bound, pre-open HGBT28 observation from a vintage or completed paper cycle."""

import argparse
from datetime import UTC, datetime, time
import json
import math
from pathlib import Path
import re
from zoneinfo import ZoneInfo

import pandas as pd

from stocklab_ml.boosted_diagnostic import _utc
from stocklab_ml.boosted_snapshot import SYMBOLS
from stocklab_ml.boosted_vintage import (
    MAX_CSV_BYTES, _csv, _day, _digest, _ordinary, _prices, _read, _require, _sha, prepare_vintage,
)
from stocklab_ml.operational_data import HOLIDAYS, _validated_snapshot, latest_closed_session
from stocklab_ml.versioning import ModelVersionError
from stocklab_ml.versioning.registry import _unique_keys
from stocklab_ml.versioning.validation import frame_digest

TARGET = "open[t+21]/open[t+1]-1 > .002"
IDENTITY = {"schema_version": 1, "mode": "diagnostic_only", "execution_eligible": False,
            "model_name": "hgb_swing20_market28", "model_status": "Archived", "target_name": "target_20",
            "target_definition": TARGET, "horizon_sessions": 20, "policy": "trend200-immediate-v1",
            "model_created_after_as_of": False, "prospective_status": "PENDING"}
HASHES = {"input_manifest_sha256", "feature_frame_sha256", "policy_context_sha256", "artifact_sha256", "registry_sha256"}
CONTEXT = ["date", "symbol", "close_ma50", "close_ma200", "momentum_20"]


def read_observation(path: Path, expected_sha: str) -> dict:
    raw = _read(path.absolute(), 64 * 1024)
    _require(_digest(expected_sha) and _sha(raw) == expected_sha, "Observation differs from its external SHA-256.")
    try:
        observation = json.loads(raw.decode("utf-8"), object_pairs_hook=_unique_keys)
    except ModelVersionError as error:
        raise ValueError("Duplicate observation fields are forbidden.") from error
    fields = IDENTITY.keys() | HASHES | {"model_version", "feature_date", "fit_boundary", "decisions", "as_of_utc",
                                         "trained_at_utc", "prepared_at_utc", "inferred_at_utc"}
    _require(isinstance(observation, dict) and observation.keys() == fields, "Expected a closed boosted diagnostic payload.")
    _require(all(type(observation[field]) is type(value) and observation[field] == value for field, value in IDENTITY.items())
             and all(_digest(observation[field]) for field in HASHES)
             and isinstance(observation["model_version"], str)
             and re.fullmatch(r"v[0-9]{6,}", observation["model_version"]) is not None,
             "Expected a prospective Archived diagnostic for the original target.")
    decisions = observation["decisions"]
    _require(isinstance(decisions, list) and len(decisions) == len(SYMBOLS)
             and all(isinstance(row, dict) and row.keys() == {"symbol", "probability_up", "action", "reason", *CONTEXT[2:]}
                     and all(type(row[field]) in (int, float) and math.isfinite(row[field])
                             for field in (*CONTEXT[2:], "probability_up"))
                     and 0 <= row["probability_up"] <= 1 for row in decisions)
             and [row["symbol"] for row in decisions] == sorted(SYMBOLS), "Expected five ordered finite original scores.")
    return observation


def model_entry(observation, feature_date, calendar, now):
    following = calendar.loc[calendar.gt(feature_date)]
    _require(not following.empty, "Capture calendar must include the next scheduled session.")
    entry = following.iloc[0]
    scheduled_open = datetime.combine(entry.date(), time(9, 30), ZoneInfo("America/New_York")).astimezone(UTC)
    trained, as_of, prepared, inferred = (_utc(datetime.fromisoformat(observation[field]))
        for field in ("trained_at_utc", "as_of_utc", "prepared_at_utc", "inferred_at_utc"))
    _require(feature_date.date() < as_of.date() and trained <= as_of <= prepared <= inferred <= now
             and inferred < scheduled_open, "The captured inference must precede the next scheduled open and follow its training.")
    return entry


def resolve_outcomes(prices, feature_date, entry_session, scores):
    _require(prices.date.eq(feature_date).any(), "Outcome vintage does not cover the capture session.")
    following = pd.DatetimeIndex(prices.loc[prices.date.gt(feature_date), "date"].unique()).sort_values()
    _require(len(following) == 0 or following[0] == entry_session, "The observed entry session changed after the capture.")
    summary = {"outcome_status": "PENDING", "subsequent_sessions": len(following), "outcomes": [], "brier_loss": None}
    if len(following) < 21:
        return summary
    known_session = following[20]
    opens = prices.loc[prices.date.isin((entry_session, known_session))].pivot(index="symbol", columns="date", values="open")
    _require(set(opens.index) == set(scores) and opens.notna().all().all(), "Maturing outcomes require both opens for every symbol.")
    rows = []
    for symbol, forecast in scores.items():
        entry, endpoint = (float(opens.loc[symbol, day]) for day in (entry_session, known_session))
        price_return = endpoint / entry - 1
        _require(math.isfinite(price_return), "Nonfinite open-to-open return.")
        rows.append({"symbol": symbol, "probability_up": forecast, "entry_open": entry, "known_open": endpoint,
                     "price_return": price_return, "target_20": int(price_return > .002)})
    return summary | {"outcome_status": "OBSERVED", "entry_session": entry_session.date().isoformat(),
        "known_session": known_session.date().isoformat(), "outcomes": rows,
        "brier_loss": math.fsum((row["probability_up"] - row["target_20"])**2 for row in rows) / len(rows)}


def checked_source(bundle: Path, prepared: dict, name: str) -> bytes:
    raw = _read(bundle.absolute() / name, MAX_CSV_BYTES)
    binding = prepared["files"][name]
    _require(len(raw) == binding["bytes"] and _sha(raw) == binding["sha256"], "A prepared vintage file changed.")
    return raw


def validated_capture(observation_path, observation_sha, captured_bundle, outcome_as_of):
    observation = read_observation(observation_path, observation_sha)
    captured_as_of = _utc(datetime.fromisoformat(observation["as_of_utc"]))
    _require(_utc(datetime.fromisoformat(observation["inferred_at_utc"])) <= _utc(outcome_as_of) <= datetime.now(UTC),
             "Outcome as-of must follow inference and precede the current clock.")
    feature_date = _day(observation["feature_date"])
    capture_sha = observation["input_manifest_sha256"]
    frame, captured = prepare_vintage(captured_bundle, manifest_sha256=capture_sha, as_of_utc=captured_as_of)
    _require(captured["cutoff_date"] == observation["feature_date"] and feature_date >= _day(observation["fit_boundary"]),
             "The capture session differs from the bound vintage or predates fitting.")
    latest = frame.loc[frame.date.eq(feature_date)].reset_index(drop=True)
    trends = latest[CONTEXT[2:]].itertuples(index=False, name=None)
    _require(frame_digest(latest) == observation["feature_frame_sha256"]
             and frame_digest(latest[CONTEXT]) == observation["policy_context_sha256"]
             and all(tuple(row[field] for field in CONTEXT[2:]) == values
                     for row, values in zip(observation["decisions"], trends, strict=True)),
             "Captured features and trend context differ.")
    schedule = _csv(checked_source(captured_bundle, captured, "expected-sessions.csv"), ["date"]).date
    entry_session = model_entry(observation, feature_date, schedule, datetime.now(UTC))
    return observation, captured, schedule, entry_session


def outcome_result(observation, observation_sha, entry_session, prices, cutoff, outcome_manifest_sha, outcome_as_of):
    scores = {row["symbol"]: row["probability_up"] for row in observation["decisions"]}
    return {"observation_sha256": observation_sha, "model_version": observation["model_version"],
        "artifact_sha256": observation["artifact_sha256"], "feature_date": observation["feature_date"],
        "target_definition": TARGET, "inferred_at_utc": observation["inferred_at_utc"],
        "expected_entry_session": entry_session.date().isoformat(), "outcome_manifest_sha256": outcome_manifest_sha,
        "outcome_as_of_utc": outcome_as_of.isoformat(), "outcome_cutoff_date": cutoff.date().isoformat(),
        **resolve_outcomes(prices, _day(observation["feature_date"]), entry_session, scores)}


def evaluate(observation_path, observation_sha, captured_bundle, outcome_bundle, outcome_manifest_sha, outcome_as_of):
    observation, captured, schedule, entry_session = validated_capture(
        observation_path, observation_sha, captured_bundle, outcome_as_of)
    if (outcome_manifest_sha == observation["input_manifest_sha256"] and outcome_bundle.absolute() == captured_bundle.absolute()
            and outcome_as_of.date() == datetime.fromisoformat(observation["as_of_utc"]).date()):
        outcome = captured
    else:
        _, outcome = prepare_vintage(outcome_bundle, manifest_sha256=outcome_manifest_sha, as_of_utc=outcome_as_of)
        outcome_schedule = _csv(checked_source(outcome_bundle, outcome, "expected-sessions.csv"), ["date"]).date
        common_end = min(entry_session, _day(outcome["calendar"]["coverage_end"]))
        start = _day(captured["prefixes"]["daily"])
        _require(schedule[schedule.between(start, common_end)].tolist()
                 == outcome_schedule[outcome_schedule.between(start, common_end)].tolist(),
                 "The expected calendar through the entry session changed.")
    cutoff = _day(outcome["cutoff_date"])
    prices = _prices(checked_source(outcome_bundle, outcome, "stocks.csv"), SYMBOLS, cutoff)
    return outcome_result(observation, observation_sha, entry_session, prices, cutoff, outcome_manifest_sha, outcome_as_of)


def evaluate_cycle(observation_path, observation_sha, captured_bundle, snapshot, snapshot_sha, cycle_ledger, ledger_sha, outcome_as_of):
    observation, _, _, entry_session = validated_capture(observation_path, observation_sha, captured_bundle, outcome_as_of)
    feature_date = _day(observation["feature_date"])
    snapshot = snapshot.absolute()
    _ordinary(snapshot, directory=True)
    ledger_bytes = _read(cycle_ledger.absolute(), 64 * 1024)
    _require(_digest(ledger_sha) and _sha(ledger_bytes) == ledger_sha, "The cycle ledger differs from its external SHA-256.")
    ledger = json.loads(ledger_bytes.decode("utf-8"), object_pairs_hook=_unique_keys)
    _require(isinstance(ledger, dict) and type(ledger.get("SchemaVersion")) is int and ledger["SchemaVersion"] == 2
             and ledger.get("Stage") == "COMPLETED" and ledger.get("Symbols") == sorted(SYMBOLS)
             and _digest(ledger.get("DatasetSha256")), "Expected a completed cycle session with a bound dataset.")
    cutoff = _day(ledger["Session"])
    started = _utc(datetime.fromisoformat(ledger["StartedAtUtc"]))
    _require(2026 <= feature_date.year <= cutoff.year <= 2028
             and feature_date <= cutoff <= pd.Timestamp(latest_closed_session(outcome_as_of.date()))
             and cutoff.date() < started.date() and started <= outcome_as_of,
             "The completed cycle is outside the observation and outcome cutoffs.")

    manifest_path = snapshot / "manifest.json"
    manifest_bytes = _read(manifest_path, 64 * 1024)
    _require(_digest(snapshot_sha) and _sha(manifest_bytes) == snapshot_sha, "The cycle manifest differs from its external SHA-256.")
    manifest = json.loads(manifest_bytes.decode("utf-8"), object_pairs_hook=_unique_keys)
    _require(_day(manifest["history_start"]) <= feature_date, "The cycle history must start by the captured session.")
    checked = _validated_snapshot(snapshot, session=cutoff.date(), symbols=tuple(sorted(SYMBOLS)),
                                  history_start=manifest["history_start"], today=outcome_as_of.date())
    stock_bytes = _read(checked["dataset_path"], MAX_CSV_BYTES)
    _require(_sha(_read(manifest_path, 64 * 1024)) == snapshot_sha
             and _sha(stock_bytes) == checked["sha256"] == manifest["sha256"] == ledger["DatasetSha256"],
             "The completed cycle must bind the exact stock snapshot.")
    prices = _prices(stock_bytes, SYMBOLS, cutoff)
    expected = pd.bdate_range(feature_date, cutoff).difference(pd.to_datetime(sorted(HOLIDAYS)))
    observed = prices.loc[prices.date.between(feature_date, cutoff)]
    _require(all(pd.DatetimeIndex(group.date).equals(expected) for _, group in observed.groupby("symbol"))
             and set(observed.symbol) == set(SYMBOLS), "The cycle snapshot must include every expected session for each symbol.")
    return outcome_result(observation, observation_sha, entry_session, prices, cutoff, snapshot_sha, outcome_as_of) | {
        "outcome_source": "completed_paper_cycle", "cycle_ledger_sha256": ledger_sha}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--observation", type=Path, required=True)
    parser.add_argument("--observation-sha256", required=True)
    parser.add_argument("--captured-bundle", type=Path, required=True)
    outcome = parser.add_mutually_exclusive_group(required=True)
    outcome.add_argument("--outcome-bundle", type=Path)
    outcome.add_argument("--outcome-cycle-snapshot", type=Path)
    parser.add_argument("--outcome-manifest-sha256", required=True)
    parser.add_argument("--outcome-as-of-utc", type=datetime.fromisoformat, required=True)
    parser.add_argument("--completed-session-ledger", type=Path)
    parser.add_argument("--completed-session-ledger-sha256")
    args = parser.parse_args()
    if args.outcome_cycle_snapshot and (not args.completed_session_ledger or not args.completed_session_ledger_sha256):
        parser.error("A completed session ledger and its SHA-256 are required with a cycle snapshot.")
    if args.outcome_bundle and (args.completed_session_ledger or args.completed_session_ledger_sha256):
        parser.error("Completed session ledger arguments require a cycle snapshot.")
    try:
        as_of = _utc(args.outcome_as_of_utc)
        capture = (args.observation, args.observation_sha256, args.captured_bundle)
        if args.outcome_cycle_snapshot:
            result = evaluate_cycle(*capture, args.outcome_cycle_snapshot, args.outcome_manifest_sha256,
                                    args.completed_session_ledger, args.completed_session_ledger_sha256, as_of)
        else:
            result = evaluate(*capture, args.outcome_bundle, args.outcome_manifest_sha256, as_of)
    except (AttributeError, KeyError, ModelVersionError, OSError, TypeError, ValueError):
        parser.exit(1, "Invalid observation, chronology or bound outcome vintage.\n")
    print(json.dumps(result, allow_nan=False))


if __name__ == "__main__":
    main()
