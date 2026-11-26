"""Outcome timing and known-label checks for captured diagnostics."""

from datetime import UTC, datetime, timedelta
import hashlib
from importlib.util import module_from_spec, spec_from_file_location
import json
from pathlib import Path
import shutil

import pandas as pd
import pytest

from stocklab_ml.versioning.validation import frame_digest
from test_boosted_vintage import AS_OF, FILES, prepare, read_csv, rebind, template, write_csv

spec = spec_from_file_location("check_boosted_observation", Path(__file__).resolve().parents[1] / "examples/check_boosted_observation.py")
observer = module_from_spec(spec)
spec.loader.exec_module(observer)
model_entry, read_observation, resolve_outcomes = observer.model_entry, observer.read_observation, observer.resolve_outcomes


def test_locked_snapshot_does_not_score_before_session_twenty_one():
    feature = pd.Timestamp("2026-11-25")
    sessions = pd.bdate_range(feature, "2026-12-28").difference(pd.to_datetime(["2026-11-26", "2026-12-25"]))
    opens = pd.DataFrame({"date": sessions, "symbol": "AAPL", "open": 100.})
    opens.loc[opens.date.eq("2026-12-24"), "open"] = 95.
    opens.loc[opens.date.eq("2026-12-28"), "open"] = 103.
    scores = {"AAPL": 0.7}

    pending = resolve_outcomes(opens.iloc[:-1], feature, sessions[1], scores)
    assert pending == {"outcome_status": "PENDING", "subsequent_sessions": 20, "outcomes": [], "brier_loss": None}

    mature = resolve_outcomes(opens, feature, sessions[1], scores)
    assert mature["outcome_status"] == "OBSERVED"
    assert mature["entry_session"] == "2026-11-27" and mature["known_session"] == "2026-12-28"
    assert mature["outcomes"][0]["target_20"] == 1
    assert mature["outcomes"][0]["price_return"] == pytest.approx(.03)
    assert mature["brier_loss"] == pytest.approx(.09)


def test_observation_requires_pre_open_inference_and_same_expected_entry():
    feature = pd.Timestamp("2026-11-25")
    sessions = pd.Series(pd.to_datetime(["2026-11-25", "2026-11-27"]))
    observation = {"trained_at_utc": "2026-11-13T02:00:00Z", "as_of_utc": "2026-11-26T04:00:00Z",
                   "prepared_at_utc": "2026-11-26T04:01:00Z", "inferred_at_utc": "2026-11-27T14:29:59Z"}
    certain_now = datetime(2026, 11, 27, 15, tzinfo=UTC)
    assert model_entry(observation, feature, sessions, certain_now) == sessions.iloc[-1]
    observation["inferred_at_utc"] = "2026-11-27T14:30:00Z"
    with pytest.raises(ValueError, match="precede the next scheduled open"):
        model_entry(observation, feature, sessions, certain_now)

    moved = pd.DataFrame({"date": pd.to_datetime(["2026-11-25", "2026-11-30"]), "symbol": "AAPL", "open": 100.})
    with pytest.raises(ValueError, match="entry session changed"):
        resolve_outcomes(moved, feature, sessions.iloc[-1], {"AAPL": 0.7})


def test_rejects_an_observation_that_does_not_match_its_external_digest(tmp_path: Path):
    path = tmp_path / "observation.json"
    path.write_bytes(b"{}")
    with pytest.raises(ValueError, match="external SHA-256"):
        read_observation(path, "0" * 64)
    duplicate = b'{"schema_version": 1, "schema_version": 1}'
    path.write_bytes(duplicate)
    with pytest.raises(ValueError, match="Duplicate observation fields"):
        read_observation(path, hashlib.sha256(duplicate).hexdigest())


def test_bound_later_vintage_resolves_existing_scores(template, tmp_path):
    capture = tmp_path / "capture"
    shutil.copytree(template, capture)
    sessions = read_csv(template, "expected-sessions.csv").date
    observed = sessions[sessions < pd.Timestamp(AS_OF.date())]
    feature, entry, known = observed.iloc[-22], observed.iloc[-21], observed.iloc[-1]
    as_of = datetime.combine(feature.date() + timedelta(days=1), datetime.min.time(), UTC)
    for name in FILES:
        if name == "expected-sessions.csv":
            continue
        source = read_csv(template, name)
        last = feature.to_period("M").start_time - pd.Timedelta(days=1) if name == "monthly.csv" else feature
        write_csv(capture, name, source.loc[source.date <= last])
    captured_digest = rebind(capture)
    source_frame, _ = prepare(capture, as_of=as_of, digest=captured_digest)
    latest = source_frame.loc[source_frame.date.eq(feature)].reset_index(drop=True)
    observation = observer.IDENTITY | {"model_version": "v000001", "trained_at_utc": "2013-11-01T01:00:00Z",
        "fit_boundary": "2013-11-12", "feature_date": feature.date().isoformat(), "as_of_utc": as_of.isoformat(),
        "prepared_at_utc": (as_of + timedelta(minutes=1)).isoformat(), "inferred_at_utc": (as_of + timedelta(minutes=2)).isoformat(),
        "input_manifest_sha256": captured_digest, "feature_frame_sha256": frame_digest(latest),
        "policy_context_sha256": frame_digest(latest[observer.CONTEXT]), "artifact_sha256": "1" * 64,
        "registry_sha256": "2" * 64}
    observation["decisions"] = [row | {"action": "HOLD", "reason": "threshold_hold", "probability_up": .5}
                                for row in latest[observer.CONTEXT[1:]].to_dict("records")]
    payload = json.dumps(observation).encode()
    path = tmp_path / "captured.json"
    path.write_bytes(payload)
    resolved = observer.evaluate(path, hashlib.sha256(payload).hexdigest(), capture, template,
                                 hashlib.sha256((template / "manifest.json").read_bytes()).hexdigest(), AS_OF)
    assert resolved["outcome_status"] == "OBSERVED"
    assert resolved["entry_session"] == entry.date().isoformat() and resolved["known_session"] == known.date().isoformat()
    assert len(resolved["outcomes"]) == 5 and resolved["brier_loss"] == .25
