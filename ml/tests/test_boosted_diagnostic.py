"""Synthetic Archived process contract; no market fits, providers or account calls."""

from datetime import UTC, datetime, timedelta
import hashlib
from importlib import import_module
import json
from pathlib import Path
from uuid import uuid4

import pandas as pd
import pytest

from stocklab_ml.artifacts import save_model_artifact
from stocklab_ml.versioning import ModelVersionRegistry
from stocklab_ml.versioning.validation import frame_digest
from test_boosted_snapshot import fitted_inputs, fitted_snapshot, snapshot_thread_limits


def api():
    return import_module("stocklab_ml.boosted_diagnostic")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


@pytest.fixture
def case(fitted_inputs, fitted_snapshot, tmp_path, monkeypatch):
    module = api()
    snapshot = fitted_snapshot[0]
    trained = datetime.fromisoformat(snapshot.metadata["trained_at_utc"])
    registry = ModelVersionRegistry(tmp_path / "registry.json", clock=lambda: trained + timedelta(seconds=1))
    record = registry.register_version(snapshot, training_run_id=str(uuid4()), trained_at_utc=trained)
    context = registry.bind_version(snapshot, model_name=record.model_name, model_version=record.model_version)
    artifact = save_model_artifact(context, output_dir=tmp_path / "artifacts")
    bundle = tmp_path / "bundle"
    bundle.mkdir()
    (bundle / "manifest.json").write_text("{}\n")
    frame = fitted_inputs[0].loc[lambda x: x.date.dt.year.eq(2024),
        ["date", "symbol"] + list(snapshot.metadata["feature_columns"])].reset_index(drop=True)
    options = dict(registry_path=registry.path, registry_sha256=sha(registry.path), artifacts_dir=artifact.artifact_path.parents[2],
        model_version=record.model_version, bundle_dir=bundle, manifest_sha256=sha(bundle / "manifest.json"),
        as_of_utc=datetime(2025, 1, 1, tzinfo=UTC), trusted_artifacts=True)
    calls = []

    def prepare(directory, **kwargs):
        calls.append((directory, kwargs))
        return frame.copy(deep=True), {"cutoff_date": frame.date.max().date().isoformat(),
            "prepared_at_utc": datetime.now(UTC).isoformat(), "as_of_utc": options["as_of_utc"].isoformat(),
            "frame_sha256": frame_digest(frame)}

    monkeypatch.setattr(module, "prepare_vintage", prepare)
    return module, options, frame, context, calls, tmp_path


def test_real_archived_inference_returns_only_latest_five_raw_context_bound_decisions(case):
    module, options, frame, context, calls, root = case
    before = {p: p.read_bytes() for p in root.rglob("*") if p.is_file()}
    start = datetime.now(UTC)
    result = module.infer_diagnostic(**options)
    end = datetime.now(UTC)
    latest = frame.loc[frame.date.eq(frame.date.max())].reset_index(drop=True)
    predictions = context.predict(latest)
    expected = predictions.policy_decisions(latest).decisions
    assert len(result["decisions"]) == 5
    assert [row["symbol"] for row in result["decisions"]] == ["AAPL", "JNJ", "JPM", "MSFT", "XOM"]
    for actual, wanted in zip(result["decisions"], expected.to_dict("records")):
        assert set(actual) == {"symbol", "probability_up", "action", "reason", "close_ma50", "close_ma200", "momentum_20"}
        for key in ("symbol", "probability_up", "action", "reason"):
            assert actual[key] == wanted[key]
        values = latest.loc[latest.symbol.eq(actual["symbol"])].iloc[0]
        assert [actual[key] for key in ("close_ma50", "close_ma200", "momentum_20")] == [values[key] for key in ("close_ma50", "close_ma200", "momentum_20")]
    assert result["execution_eligible"] is False and result["mode"] == "diagnostic_only"
    assert result["model_status"] == "Archived" and result["prospective_status"] == "PENDING"
    assert result["model_created_after_as_of"] is True
    assert result["fit_boundary"] == "2024-01-01"
    assert result["feature_date"] == latest.date.iloc[0].date().isoformat()
    assert result["feature_frame_sha256"] == frame_digest(latest)
    assert result["registry_sha256"] == options["registry_sha256"]
    assert result["input_manifest_sha256"] == options["manifest_sha256"]
    assert result["artifact_sha256"] == context.record.training_report["estimator_sha256"]
    assert start <= datetime.fromisoformat(result["prepared_at_utc"]) <= datetime.fromisoformat(result["inferred_at_utc"]) <= end
    assert result["trained_at_utc"] == context.record.trained_at_utc.isoformat()
    assert len(calls) == 1 and calls[0][0] == options["bundle_dir"]
    assert "confidence" not in json.dumps(result) and "decision_id" not in json.dumps(result)
    assert {p: p.read_bytes() for p in before} == before
    assert {p for p in root.rglob("*") if p.is_file()} == set(before)


@pytest.mark.parametrize("bad", ["untrusted", "registry_hash", "manifest_hash", "version", "missing_version", "naive", "future", "relative_path", "backward_features"])
def test_invalid_identity_trust_dates_and_backward_prediction_are_refused(case, bad, monkeypatch):
    module, options, frame, _, _, _ = case
    if bad == "untrusted": options["trusted_artifacts"] = False
    elif bad == "registry_hash": options["registry_sha256"] = "a" * 64
    elif bad == "manifest_hash": options["manifest_sha256"] = "X" * 64
    elif bad == "version": options["model_version"] = "v000002"
    elif bad == "missing_version": options["model_version"] = "v1"
    elif bad == "naive": options["as_of_utc"] = options["as_of_utc"].replace(tzinfo=None)
    elif bad == "future": options["as_of_utc"] = datetime.now(UTC) + timedelta(days=1)
    elif bad == "relative_path": options["registry_path"] = Path("registry.json")
    elif bad == "backward_features":
        earlier = frame.copy()
        earlier["date"] -= pd.DateOffset(years=2)
        monkeypatch.setattr(module, "prepare_vintage", lambda *args, **kwargs: (earlier, {
            "cutoff_date": earlier.date.max().date().isoformat(), "as_of_utc": options["as_of_utc"].isoformat(),
            "prepared_at_utc": datetime.now(UTC).isoformat()}))
    with pytest.raises(module.BoostedDiagnosticError, match="boosted_diagnostic_failed"):
        module.infer_diagnostic(**options)


def test_active_or_substituted_record_is_refused_before_unpickle(case, monkeypatch):
    module, options, _, _, _, _ = case
    data = json.loads(options["registry_path"].read_text())
    data["models"][0]["status"] = "Active"
    options["registry_path"].write_text(json.dumps(data))
    options["registry_sha256"] = sha(options["registry_path"])
    monkeypatch.setattr(module, "load_model_artifact", lambda *args, **kwargs: pytest.fail("Must reject before unpickle"))
    with pytest.raises(module.BoostedDiagnosticError):
        module.infer_diagnostic(**options)


def test_registry_substitution_during_inference_is_refused(case, monkeypatch):
    module, options, _, _, _, _ = case
    original = module.load_model_artifact

    def changed(*args, **kwargs):
        loaded = original(*args, **kwargs)
        options["registry_path"].write_bytes(options["registry_path"].read_bytes() + b" ")
        return loaded

    monkeypatch.setattr(module, "load_model_artifact", changed)
    with pytest.raises(module.BoostedDiagnosticError):
        module.infer_diagnostic(**options)


def test_cli_uses_explicit_identity_and_sanitizes_argument_or_inference_failures(case, monkeypatch, capsys):
    module, options, _, _, _, _ = case
    arguments = []
    for key in ("registry_path", "registry_sha256", "artifacts_dir", "model_version", "bundle_dir", "manifest_sha256"):
        arguments += ["--" + key.replace("_", "-"), str(options[key])]
    arguments += ["--as-of-utc", options["as_of_utc"].isoformat(), "--trusted-artifacts"]
    assert module.main(arguments) == 0
    result = json.loads(capsys.readouterr().out)
    assert len(result["decisions"]) == 5 and result["execution_eligible"] is False
    monkeypatch.setattr(module, "infer_diagnostic", lambda **kwargs: (_ for _ in ()).throw(module.BoostedDiagnosticError("PRIVATE-PATH")))
    assert module.main(arguments) == 1
    captured = capsys.readouterr()
    assert captured.out == "" and captured.err == "boosted_diagnostic_failed\n"
    with pytest.raises(SystemExit) as error:
        module.main(["--private-unknown-option", "SECRET"])
    assert error.value.code == 2
    captured = capsys.readouterr()
    assert "SECRET" not in captured.err and "PRIVATE" not in captured.err
