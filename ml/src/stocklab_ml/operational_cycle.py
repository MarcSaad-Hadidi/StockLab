"""Explicit local paper-cycle runner. Never trains, promotes, or submits broker orders."""
import argparse
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
from datetime import UTC, datetime
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time

from stocklab_ml.artifacts.local_storage import artifact_paths, read_bounded, read_local
from stocklab_ml.data.storage import save_raw
from stocklab_ml.data.validation import calendar_date
from stocklab_ml.operational_data import prepare_cycle_dataset
from stocklab_ml.retraining.contracts import RetrainingBusyError
from stocklab_ml.retraining.state import read_json, retraining_lock
from stocklab_ml.versioning import ModelVersionRegistry
from stocklab_ml.versioning.registry import _unique_keys

ML_ROOT = Path(__file__).resolve().parents[2]
REQUIRED = {"schema_version", "backend_dll", "expected_database", "registry_path", "artifacts_directory",
            "model_name", "symbols", "history_start", "data_directory", "state_directory", "trusted_artifacts",
            "share_website_quota", "interval_seconds"}
STAGES = {"PREFLIGHT", "DATABASE_TARGET", "DATABASE_CONNECTION", "MIGRATIONS", "PROVIDER", "POLICY", "PORTFOLIO", "MODEL", "INPUTS", "JOURNAL",
          "RECONCILIATION", "INFERENCE", "EXECUTION", "PERFORMANCE", "COMPLETED"}


def validate_config(payload):
    if (type(payload) is not dict or not REQUIRED <= payload.keys()
            or payload.keys() - REQUIRED - {"website_secrets_path"}
            or type(payload["schema_version"]) is not int or payload["schema_version"] != 1
            or payload["trusted_artifacts"] is not True or type(payload["share_website_quota"]) is not bool
            or type(payload["interval_seconds"]) is not int or not 60 <= payload["interval_seconds"] <= 86400
            or payload["model_name"] not in {"logistic_regression_baseline", "random_forest_baseline"}
            or not isinstance(payload["expected_database"], str) or not 1 <= len(payload["expected_database"]) <= 128
            or payload["expected_database"] != payload["expected_database"].strip()
            or any(ord(c) < 32 for c in payload["expected_database"])):
        raise ValueError("Invalid cycle configuration.")
    config = payload.copy()
    symbols = config["symbols"]
    if (type(symbols) is not list or not 1 <= len(symbols) <= 10 or len(set(symbols)) != len(symbols)
            or any(not isinstance(s, str) or not s or len(s) > 32 or s != s.upper()
                   or any(c.isspace() or ord(c) < 32 for c in s) for s in symbols)):
        raise ValueError("Invalid cycle universe.")
    calendar_date(config["history_start"])
    for key in ("backend_dll", "registry_path", "artifacts_directory", "data_directory", "state_directory", "website_secrets_path"):
        if key not in config:
            continue
        value = config[key]
        if not isinstance(value, (str, Path)) or not Path(value).is_absolute() or ".." in Path(value).parts:
            raise ValueError("Explicit absolute private paths are required.")
        config[key] = Path(value).resolve()
    data, state = config["data_directory"], config["state_directory"]
    if (data.is_relative_to(state) or state.is_relative_to(data)
            or config["backend_dll"].suffix.lower() != ".dll" or not config["backend_dll"].is_file()
            or any(config[key].is_relative_to(data) or config[key].is_relative_to(state)
                   for key in ("backend_dll", "registry_path", "artifacts_directory", "website_secrets_path") if key in config)):
        raise ValueError("Cycle paths must not alias inputs or configuration.")
    return config


def _operator_json(path):
    # Windows User Secrets/config editors can preserve a UTF-8 BOM.
    return json.loads(read_bounded(path, 65536).decode("utf-8-sig"), object_pairs_hook=_unique_keys,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError()))


@contextmanager
def _ml_credential(config):
    original = os.environ.get("TWELVE_DATA_ML_API_KEY")
    try:
        if not (original or "").strip():
            if not config["share_website_quota"] or "website_secrets_path" not in config:
                raise ValueError("An explicitly configured ML credential is required.")
            secrets = _operator_json(config["website_secrets_path"])
            selected = secrets.get("TwelveData:ActiveWebsiteKey", "Website")
            if selected not in {"Website", "Fallback"}:
                raise ValueError("Invalid configured website credential selector.")
            key = secrets.get("TwelveData:Keys:" + selected)
            if not isinstance(key, str) or not key.strip():
                raise ValueError("Configured credential is unavailable.")
            os.environ["TWELVE_DATA_ML_API_KEY"] = key
        yield
    finally:
        if original is None:
            os.environ.pop("TWELVE_DATA_ML_API_KEY", None)
        else:
            os.environ["TWELVE_DATA_ML_API_KEY"] = original


def _process(command, *, environment):
    options = {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if os.name == "nt" else {"start_new_session": True}
    with subprocess.Popen(command, cwd=ML_ROOT.parent / "backend/StockLab.Api", env=environment,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE, **options) as process:
        def read(stream):
            chunks, size = [], 0
            while chunk := stream.read(4096):
                size += len(chunk)
                if size > 65536:
                    process.kill()
                    raise ValueError("Backend output exceeded its contract.")
                chunks.append(chunk)
            return b"".join(chunks)
        with ThreadPoolExecutor(max_workers=2) as readers:
            stdout, stderr = readers.submit(read, process.stdout), readers.submit(read, process.stderr)
            try:
                code = process.wait(timeout=600)
                result = stdout.result()
                stderr.result()  # Drain and bound private diagnostics; never relay them.
                return code, result
            finally:
                if process.poll() is None:
                    try:
                        process.send_signal(signal.CTRL_BREAK_EVENT if os.name == "nt" else signal.SIGINT)
                        process.wait(timeout=5)
                    except (OSError, subprocess.TimeoutExpired):
                        process.kill()
                        process.wait()


class BackendCycleError(Exception):
    def __init__(self, stage):
        self.stage = stage


def _backend(config, version, *, snapshot=None):
    arguments = {"AiTradingCycle:ExpectedDatabase": config["expected_database"],
        "AiTradingCycle:ModelName": config["model_name"], "AiTradingCycle:ModelVersion": version,
        "AiTradingCycle:StateDirectory": str(config["state_directory"]),
        "AiTraderMetadata:RegistryPath": str(config["registry_path"]),
        "AiDecisionPipeline:PythonExecutable": sys.executable, "AiDecisionPipeline:MlDirectory": str(ML_ROOT),
        "AiDecisionPipeline:ArtifactsDirectory": str(config["artifacts_directory"]),
        "AiDecisionPipeline:ModelName": config["model_name"], "AiDecisionPipeline:TrustedArtifacts": "true"}
    arguments.update({f"AiDecisionPipeline:Symbols:{i}": symbol for i, symbol in enumerate(config["symbols"])})
    if snapshot is None:
        arguments["AiTradingCycle:CheckOnly"] = "true"
    else:
        arguments.update({"AiTradingCycle:DecisionDate": snapshot["decision_date"],
            "AiTradingCycle:PreparedUtcDate": snapshot["as_of_utc_date"], "AiTradingCycle:DatasetSha256": snapshot["sha256"],
            "AiDecisionPipeline:DatasetPath": str(snapshot["dataset_path"])})
    executable = shutil.which("dotnet")
    if executable is None:
        raise ValueError("Backend runtime is unavailable.")
    environment = os.environ.copy()
    environment.pop("TWELVE_DATA_ML_API_KEY", None)
    environment.update(ASPNETCORE_ENVIRONMENT="Development", DOTNET_ENVIRONMENT="Development",
                       ASPNETCORE_CONTENTROOT=str(ML_ROOT.parent / "backend/StockLab.Api"))
    code, output = _process([executable, str(config["backend_dll"]), "--run-ai-trader-cycle"]
        + [f"--{key}={value}" for key, value in arguments.items()], environment=environment)
    result = json.loads(output, object_pairs_hook=_unique_keys,
                        parse_constant=lambda _: (_ for _ in ()).throw(ValueError()))
    if type(result) is not dict:
        raise ValueError("Invalid backend result.")
    if code != 0 or result.get("status") in {"FAILED", "CANCELLED"}:
        raise BackendCycleError(result.get("stage") if result.get("stage") in STAGES else "PREFLIGHT")
    if snapshot is None:
        if result != {"status": "READY", "stage": "PREFLIGHT"}:
            raise ValueError("Invalid preflight result.")
    elif (set(result) != {"status", "stage", "session", "executed", "rejected", "hold", "replay"}
            or result["status"] != "COMPLETED" or result["stage"] != "COMPLETED"
            or result["session"] != snapshot["decision_date"] or type(result["replay"]) is not bool
            or any(type(result[key]) is not int or result[key] < 0 for key in ("executed", "rejected", "hold"))
            or sum(result[key] for key in ("executed", "rejected", "hold")) != len(config["symbols"])):
        raise ValueError("Invalid completed-cycle result.")
    return result


def request_stop(config):
    state = Path(config["state_directory"])
    if not state.is_absolute() or ".." in state.parts:
        raise ValueError("An explicit state directory is required.")
    state.mkdir(parents=True, exist_ok=True)
    (state / "stop.requested").touch(exist_ok=True)


def _run_pass(config, now):
    stage, state = "PREFLIGHT", config["state_directory"]
    health = {}
    try:
        previous = read_json(state / "status.json", limit=65536) or {}
        last_success = previous.get("last_success_at_utc")
        if last_success is not None:
            parsed = datetime.fromisoformat(last_success)
            if parsed.tzinfo is None or parsed.utcoffset().total_seconds() != 0:
                raise ValueError("Invalid previous status.")
        health = {"status": "RUNNING", "stage": "PREFLIGHT", "attempted_at_utc": now.isoformat(),
                  "last_success_at_utc": last_success}
        save_raw(state / "status.json", health, overwrite=True)
        registry = ModelVersionRegistry(config["registry_path"])
        active = registry.get_active_version(config["model_name"])
        if active is None:
            raise ValueError("An explicitly promoted Active model is required.")
        read_local(artifact_paths(active.model_name, active.model_version, config["artifacts_directory"]), registry=registry)
        _backend(config, active.model_version)
        stage = "DATA"
        save_raw(state / "status.json", health | {"stage": stage}, overwrite=True)
        with _ml_credential(config):
            snapshot = prepare_cycle_dataset(config["data_directory"], symbols=tuple(config["symbols"]),
                history_start=config["history_start"], as_of_utc_date=now.date())
        stage = "EXECUTION"
        save_raw(state / "status.json", health | {"stage": stage}, overwrite=True)
        result = _backend(config, active.model_version, snapshot=snapshot)
        save_raw(state / "status.json", health | result | {"last_success_at_utc": datetime.now(UTC).isoformat()}, overwrite=True)
        return result
    except Exception as error:
        if isinstance(error, BackendCycleError):
            stage = error.stage
        result = {"status": "FAILED", "stage": stage}
        if health:
            try:
                save_raw(state / "status.json", health | result, overwrite=True)
            except Exception:
                pass
        return result


def run_once(payload, *, now_utc=None):
    try:
        config = validate_config(payload)
        now = datetime.now(UTC) if now_utc is None else now_utc
        if now.tzinfo is None or now.utcoffset().total_seconds() != 0:
            raise ValueError("A UTC clock is required.")
        state = config["state_directory"]
        state.mkdir(parents=True, exist_ok=True)
        if (state / "stop.requested").exists():
            return {"status": "STOPPED", "stage": "STOPPED"}
        with retraining_lock(state / "runner-pass.json"):
            return _run_pass(config, now)
    except RetrainingBusyError:
        return {"status": "BUSY", "stage": "LOCK"}
    except Exception:
        return {"status": "FAILED", "stage": "CONFIGURATION"}


class _Parser(argparse.ArgumentParser):
    def error(self, message):
        self.exit(2, "cycle_invalid_arguments: use --help for usage.\n")


def main(argv=None):
    parser = _Parser(description=__doc__)
    parser.add_argument("--config", required=True, type=Path)
    mode = parser.add_mutually_exclusive_group(required=True)
    for name in ("once", "loop", "stop"):
        mode.add_argument("--" + name, action="store_true")
    args = parser.parse_args(argv)
    try:
        payload = _operator_json(args.config)
        if args.stop:
            request_stop(payload)
            print(json.dumps({"status": "STOP_REQUESTED", "stage": "STOPPED"}))
            return 0
        if args.once:
            result = run_once(payload)
            print(json.dumps(result))
            return 0 if result["status"] in {"COMPLETED", "STOPPED"} else 1
        config = validate_config(payload)
        state = config["state_directory"]
        with retraining_lock(state / "runner-owner.json"):
            (state / "stop.requested").unlink(missing_ok=True)
            while not (state / "stop.requested").exists():
                print(json.dumps(run_once(payload)), flush=True)
                for _ in range(config["interval_seconds"]):
                    if (state / "stop.requested").exists():
                        break
                    time.sleep(1)
            stopped = {"status": "STOPPED", "stage": "STOPPED"}
            with retraining_lock(state / "runner-pass.json"):
                previous = read_json(state / "status.json", limit=65536) or {}
                save_raw(state / "status.json", previous | stopped, overwrite=True)
            print(json.dumps(stopped), flush=True)
        return 0
    except RetrainingBusyError:
        print(json.dumps({"status": "BUSY", "stage": "LOCK"}))
        return 1
    except KeyboardInterrupt:
        return 130
    except Exception:
        print(json.dumps({"status": "FAILED", "stage": "CONFIGURATION"}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
