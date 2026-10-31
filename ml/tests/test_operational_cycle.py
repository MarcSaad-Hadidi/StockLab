"""Real model metadata/storage and real child processes; only market/.NET boundaries are controlled."""
from datetime import UTC, datetime
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
from uuid import uuid4

import numpy as np
import pandas as pd
import pytest


@pytest.fixture(scope="module")
def fitted(tmp_path_factory):
    from stocklab_ml.artifacts import save_model_artifact
    from stocklab_ml.data.cleaning import clean_historical_dataset
    from stocklab_ml.features import engineer_features
    from stocklab_ml.modeling import train_logistic_regression
    from stocklab_ml.versioning import ModelVersionRegistry
    root = tmp_path_factory.mktemp("cycle-model")
    index = np.arange(140)
    price = 100 + index * .07 + 3 * np.sin(index * .7)
    frame = pd.DataFrame({"date": pd.bdate_range(end="2026-06-30", periods=140), "symbol": "AAPL",
        "open": price, "high": price + 1, "low": price - 1, "close": price, "volume": 1000 + index})
    cleaned = clean_historical_dataset(frame, as_of_date="2026-06-30")
    result = train_logistic_regression(engineer_features(cleaned.dataframe).dataframe)
    registry = ModelVersionRegistry(root / "registry.json")
    record = registry.register_version(result, training_run_id=str(uuid4()), trained_at_utc=datetime.now(UTC))
    save_model_artifact(registry.bind_version(result, model_name=record.model_name, model_version=record.model_version), output_dir=root / "artifacts")
    registry.set_active_version(record.model_name, record.model_version)
    return root


@pytest.fixture
def config(tmp_path, fitted, monkeypatch, transport):
    private = tmp_path / "PRIVATE_CONFIG_ROOT"
    shutil.copytree(fitted, private)
    backend = private / "backend.dll"
    backend.write_text('''import json,sys
from pathlib import Path
args=dict(x[2:].split("=",1) for x in sys.argv[1:] if x.startswith("--") and "=" in x)
state=Path(args["AiTradingCycle:StateDirectory"])
state.mkdir(parents=True,exist_ok=True)
with (state/"backend-calls.jsonl").open("a") as log: log.write(json.dumps(args)+"\\n")
if args.get("AiTradingCycle:CheckOnly")=="true": print(json.dumps({"status":"READY","stage":"PREFLIGHT"}))
else: print(json.dumps({"status":"COMPLETED","stage":"COMPLETED","session":"2026-10-26","executed":0,"rejected":0,"hold":1,"replay":False}))
''')
    monkeypatch.setattr(shutil, "which", lambda _: sys.executable)
    transport.body["values"] = [{"datetime": day.date().isoformat(), "open": "100", "high": "102",
        "low": "99", "close": "101", "volume": "1000"} for day in pd.bdate_range("2026-07-01", "2026-10-26")]
    return {"schema_version": 1, "backend_dll": str(backend), "expected_database": "isolated-test-database",
        "registry_path": str(private / "registry.json"), "artifacts_directory": str(private / "artifacts"),
        "model_name": "logistic_regression_baseline", "symbols": ["AAPL"], "history_start": "2026-07-01",
        "data_directory": str(private / "data"), "state_directory": str(private / "state"),
        "trusted_artifacts": True, "share_website_quota": False, "interval_seconds": 1800}


def run(config):
    from stocklab_ml.operational_cycle import run_once
    return run_once(config, now_utc=datetime(2026, 10, 27, 4, tzinfo=UTC))


def test_operator_composes_preflight_fresh_inputs_and_guarded_backend_without_leaking_private_values(config, transport):
    result = run(config)
    assert result["status"] == "COMPLETED" and result["hold"] == 1
    assert "PRIVATE_CONFIG_ROOT" not in json.dumps(result)
    assert "ML-TEST-KEY" not in json.dumps(result)
    assert len(transport.calls) == 1
    state = Path(config["state_directory"])
    calls = [json.loads(line) for line in (state / "backend-calls.jsonl").read_text().splitlines()]
    assert calls[0]["AiTradingCycle:CheckOnly"] == "true"
    assert calls[1]["AiTradingCycle:DecisionDate"] == "2026-10-26"
    assert calls[1]["AiTradingCycle:PreparedUtcDate"] == "2026-10-27"
    assert calls[1]["AiDecisionPipeline:DatasetPath"].endswith("2026-10-26/cleaned.csv") or calls[1]["AiDecisionPipeline:DatasetPath"].endswith("2026-10-26\\cleaned.csv")
    assert json.loads((state / "status.json").read_text())["last_success_at_utc"] is not None
    assert run(config)["status"] == "COMPLETED"
    assert len(transport.calls) == 1


@pytest.mark.parametrize("bad", ["preflight", "invalid-output", "oversize-output"])
def test_failed_backend_boundary_blocks_and_sanitizes_output(config, transport, bad):
    backend = Path(config["backend_dll"])
    backend.write_text('import sys; print("private-error-credential",file=sys.stderr); '
                       + ('print("x"*70000)' if bad == "oversize-output" else 'print("private-error-path")')
                       + ('; sys.exit(1)' if bad == "preflight" else ''))
    result = run(config)
    assert result["status"] == "FAILED" and result["stage"] == "PREFLIGHT"
    assert "private-error" not in json.dumps(result)
    assert not transport.calls


def test_stale_data_never_reaches_execution_and_preserves_last_success(config, transport):
    state = Path(config["state_directory"])
    state.mkdir()
    previous = "2026-10-23T12:00:00+00:00"
    (state / "status.json").write_text(json.dumps({"last_success_at_utc": previous}))
    transport.body["values"].pop()
    result = run(config)
    assert result["status"] == "FAILED" and result["stage"] == "DATA"
    assert json.loads((state / "status.json").read_text())["last_success_at_utc"] == previous
    assert len((state / "backend-calls.jsonl").read_text().splitlines()) == 1


@pytest.mark.parametrize("encoding", ["utf-8", "utf-8-sig"])
def test_shared_credential_requires_explicit_consent_and_does_not_enter_backend_arguments(config, transport, monkeypatch, encoding):
    monkeypatch.delenv("TWELVE_DATA_ML_API_KEY")
    secret = Path(config["state_directory"]).parent / "website-secrets.json"
    secret.write_text(json.dumps({"TwelveData:ActiveWebsiteKey": "Website", "TwelveData:Keys:Website": "ML-TEST-KEY"}), encoding=encoding)
    config["website_secrets_path"] = str(secret)
    assert run(config)["status"] == "FAILED"
    assert not transport.calls
    config["share_website_quota"] = True
    assert run(config)["status"] == "COMPLETED"
    assert transport.calls[0][0].headers["Authorization"] == "apikey ML-TEST-KEY"
    assert "ML-TEST-KEY" not in (Path(config["state_directory"]) / "backend-calls.jsonl").read_text()


def test_busy_owner_and_stop_request_do_not_start_another_pass(config, transport):
    from stocklab_ml.operational_cycle import request_stop
    from stocklab_ml.retraining.state import retraining_lock
    state = Path(config["state_directory"])
    state.mkdir()
    with retraining_lock(state / "runner-pass.json"):
        assert run(config)["status"] == "BUSY"
    request_stop(config)
    assert run(config)["status"] == "STOPPED"
    assert not transport.calls


@pytest.mark.parametrize("bad", ["unknown-field", "untrusted", "same-directory", "relative", "interval", "symbols"])
def test_invalid_configuration_never_spends_provider_quota(config, transport, bad):
    if bad == "unknown-field": config["api_key"] = "private-unexpected-secret"
    elif bad == "untrusted": config["trusted_artifacts"] = False
    elif bad == "same-directory": config["data_directory"] = config["state_directory"]
    elif bad == "relative": config["backend_dll"] = "backend.dll"
    elif bad == "interval": config["interval_seconds"] = True
    else: config["symbols"] = ["AAPL", "AAPL"]
    assert run(config)["status"] == "FAILED"
    assert not transport.calls


def test_loop_stops_after_the_current_pass_and_restart_reuses_inputs(config, transport, monkeypatch, tmp_path, capsys):
    from stocklab_ml import operational_cycle as cycle
    class Clock(datetime):
        @classmethod
        def now(cls, tz=None):
            return datetime(2026, 10, 27, 4, tzinfo=UTC)
    monkeypatch.setattr(cycle, "datetime", Clock)
    monkeypatch.setattr(cycle.time, "sleep", lambda _: cycle.request_stop(config))
    path = tmp_path / "config.json"
    path.write_text(json.dumps(config))
    for _ in range(2):
        assert cycle.main(["--config", str(path), "--loop"]) == 0
        output = [json.loads(line) for line in capsys.readouterr().out.splitlines()]
        assert output[0]["status"] == "COMPLETED" and output[-1]["status"] == "STOPPED"
        status = json.loads((Path(config["state_directory"]) / "status.json").read_text())
        assert status["status"] == "STOPPED" and status["last_success_at_utc"] is not None
    assert len(transport.calls) == 1


def test_second_loop_reports_busy_without_starting_another_pass(config, transport, tmp_path, capsys):
    from stocklab_ml import operational_cycle as cycle
    from stocklab_ml.retraining.state import retraining_lock
    path = tmp_path / "config.json"
    path.write_text(json.dumps(config))
    with retraining_lock(Path(config["state_directory"]) / "runner-owner.json"):
        assert cycle.main(["--config", str(path), "--loop"]) == 1
    assert json.loads(capsys.readouterr().out)["status"] == "BUSY"
    assert not transport.calls


def test_failure_publication_cannot_overwrite_a_later_success(config, monkeypatch):
    from stocklab_ml import operational_cycle as cycle
    backend = Path(config["backend_dll"])
    good = backend.read_text()
    backend.write_text('print("invalid-private-backend-output")')
    save = cycle.save_raw
    interleaved = []
    def publish(path, payload, **kwargs):
        if path.name == "status.json" and payload.get("status") == "FAILED" and not interleaved:
            backend.write_text(good)
            interleaved.append(run(config))
        return save(path, payload, **kwargs)
    monkeypatch.setattr(cycle, "save_raw", publish)
    assert run(config)["status"] == "FAILED"
    assert interleaved[0]["status"] == "BUSY"
    assert run(config)["status"] == "COMPLETED"
    status = json.loads((Path(config["state_directory"]) / "status.json").read_text())
    assert status["status"] == "COMPLETED" and status["last_success_at_utc"] is not None


@pytest.mark.parametrize("extra,code,channel", [((), 1, "stdout"), (("--private=PRIVATE_VALUE",), 2, "stderr")])
def test_actual_cli_failure_never_echoes_private_paths_or_arguments(tmp_path, extra, code, channel):
    environment = os.environ.copy()
    environment["PYTHONPATH"] = str(Path(__file__).parents[1] / "src")
    result = subprocess.run([sys.executable, "-m", "stocklab_ml.operational_cycle", "--once", "--config",
        str(tmp_path / "PRIVATE_CONFIG_PATH"), *extra], capture_output=True, text=True, timeout=30, env=environment)
    assert result.returncode == code
    assert "PRIVATE" not in result.stdout + result.stderr
    assert "FAILED" in result.stdout if channel == "stdout" else "cycle_invalid_arguments" in result.stderr


def test_actual_stop_cli_accepts_a_windows_utf8_bom_config(tmp_path):
    state = tmp_path / "state"
    config = tmp_path / "config.json"
    config.write_text(json.dumps({"state_directory": str(state)}), encoding="utf-8-sig")
    environment = os.environ.copy()
    environment["PYTHONPATH"] = str(Path(__file__).parents[1] / "src")
    result = subprocess.run([sys.executable, "-m", "stocklab_ml.operational_cycle", "--stop", "--config", str(config)],
        capture_output=True, text=True, timeout=30, env=environment)
    assert result.returncode == 0
    assert json.loads(result.stdout)["status"] == "STOP_REQUESTED"
    assert (state / "stop.requested").exists()
