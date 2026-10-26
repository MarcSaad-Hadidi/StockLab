"""Synthetic-only catalogs; private operational inputs never enter the test suite."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
from uuid import uuid4

import numpy as np
import pandas as pd
import pytest
from stocklab_ml.versioning import ModelVersionError

SCRIPTS = Path(__file__).parents[1] / "scripts"


def load_script(name):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


exporter = load_script("export_ai_trader_catalogs")
diagnostic = load_script("diagnose_zero_trades")


@pytest.fixture
def catalog(tmp_path):
    index = np.arange(140)
    prices = 100 + index * .07 + 3 * np.sin(index * .7)
    frame = pd.DataFrame({"date": pd.date_range("2024-01-01", periods=140, freq="B"),
        "symbol": "TEST", "open": prices, "high": prices + 1, "low": prices - 1,
        "close": prices, "volume": 1000 + index})
    source = tmp_path / "input.csv"
    frame.to_csv(source, index=False, date_format="%Y-%m-%d")
    directory = tmp_path / "PRIVATE_CATALOG_PATH"
    exporter.export_catalogs(source, directory)
    return directory


def contents(directory):
    return {str(path.relative_to(directory)): path.read_bytes()
            for path in directory.rglob("*") if path.is_file()}


def test_reproduces_registered_results_and_reports_without_writes_or_raw_rows(catalog):
    before = contents(catalog)
    result = diagnostic.diagnose_catalog(catalog, as_of_date="2026-10-26")
    assert result["schema_version"] == 1
    assert len(result["models"]) == 2
    assert any(model["executed_trades"] > 0 for model in result["models"]), "The synthetic control must also exercise nonzero execution"
    for model in result["models"]:
        original = json.loads((catalog / model["model_name"] / "backtest/report.json").read_text())
        assert model["reproduced"] is True
        assert model["decision_count"] == original["decision_count"]
        assert model["signal_counts"] == {"BUY": original["buy_decisions"],
            "SELL": original["sell_decisions"], "HOLD": original["hold_decisions"]}
        assert model["executed_trades"] == original["executed_trades"]
        assert sum(model["signal_counts"].values()) == model["decision_count"]
        assert model["executed_trades"] + model["signal_counts"]["HOLD"] + sum(model["skipped"].values()) == model["decision_count"]
        assert 0 <= model["probability_up"]["min"] <= model["probability_up"]["max"] <= 1
        for action in ("BUY", "SELL"):
            assert model["confidence"][action]["passes_minimum"] <= model["signal_counts"][action]
    serialized = json.dumps(result)
    for private in (str(catalog), "PRIVATE_CATALOG_PATH", "training_run_id", "holdout_sha256", "target_date", "predictions"):
        assert private not in serialized
    assert contents(catalog) == before


@pytest.mark.parametrize("tamper", ["holdout", "report", "ambiguous", "missing", "type"])
def test_mismatches_or_multiple_versions_fail_without_repair(catalog, tamper):
    path = catalog / "registry.json"
    registry = json.loads(path.read_text())
    if tamper == "holdout":
        registry["models"][0]["holdout_sha256"] = "0" * 64
        path.write_text(json.dumps(registry))
    elif tamper == "ambiguous":
        registry["models"].append(registry["models"][0] | {
            "version_number": 2, "model_version": "v000002", "training_run_id": str(uuid4())})
        path.write_text(json.dumps(registry))
    elif tamper == "missing":
        registry["models"].pop(0)
        path.write_text(json.dumps(registry))
    else:
        path = catalog / registry["models"][0]["model_name"] / "backtest/report.json"
        report = json.loads(path.read_text())
        if tamper == "type": report["commission"] = False
        else: report["executed_trades"] += 1
        path.write_text(json.dumps(report))
    before = contents(catalog)
    with pytest.raises(ModelVersionError if tamper == "holdout" else ValueError):
        diagnostic.diagnose_catalog(catalog, as_of_date="2026-10-26")
    assert contents(catalog) == before


@pytest.mark.parametrize("payload", ['{"executed_trades":0,"executed_trades":1}', '{"executed_trades":NaN}'])
def test_ambiguous_or_nonfinite_report_json_is_rejected(catalog, payload):
    path = catalog / "logistic_regression_baseline/backtest/report.json"
    path.write_text(payload)
    before = contents(catalog)
    with pytest.raises(ValueError):
        diagnostic.diagnose_catalog(catalog, as_of_date="2026-10-26")
    assert contents(catalog) == before


def cli(directory, *extra):
    env = os.environ.copy()
    env["PYTHONPATH"] = str(Path(__file__).parents[1] / "src")
    return subprocess.run([sys.executable, str(SCRIPTS / "diagnose_zero_trades.py"),
        "--catalog-dir", str(directory), "--as-of-date", "2026-10-26", *extra],
        capture_output=True, text=True, timeout=60, env=env)


def test_real_cli_emits_only_aggregate_json(catalog):
    before = contents(catalog)
    result = cli(catalog)
    assert result.returncode == 0, result.stderr
    assert len(json.loads(result.stdout)["models"]) == 2
    assert result.stderr == ""
    assert "PRIVATE_CATALOG_PATH" not in result.stdout
    assert contents(catalog) == before


def test_real_cli_failure_does_not_expose_input_paths_or_tracebacks(tmp_path):
    result = cli(tmp_path / "PRIVATE_CATALOG_PATH")
    assert result.returncode == 1
    assert result.stdout == ""
    assert result.stderr.strip() == "diagnostic_failed: check catalog, date and original model environment."


@pytest.mark.parametrize("extra", [("PRIVATE_ARGUMENT_VALUE",), ("--unused=PRIVATE_ARGUMENT_VALUE",)])
def test_cli_argument_errors_do_not_echo_private_values(tmp_path, extra):
    result = cli(tmp_path / "PRIVATE_CATALOG_PATH", *extra)
    assert result.returncode == 2
    assert result.stdout == ""
    assert result.stderr.strip() == "diagnostic_invalid_arguments: use --help for usage."
