"""Test-only synthetic inputs; operational exports must use verified market data."""

import importlib.util
import json
from pathlib import Path

import numpy as np
import pandas as pd
import pytest

spec = importlib.util.spec_from_file_location("export_ai_trader_catalogs",
    Path(__file__).parents[1] / "scripts" / "export_ai_trader_catalogs.py")
exporter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(exporter)


def test_existing_output_is_rejected_without_changing_it(tmp_path):
    source = tmp_path / "input.csv"
    source.write_text("invalid data")
    destination = tmp_path / "existing"
    destination.mkdir()
    marker = destination / "registry.json"
    marker.write_text("preserve")
    with pytest.raises(FileExistsError):
        exporter.export_catalogs(source, destination)
    assert marker.read_text() == "preserve"
    assert source.read_text() == "invalid data"


def test_export_uses_real_estimators_same_chronological_holdout_and_never_promotes(tmp_path):
    # Offline fixture only: exercising the existing estimators and validated writers without HTTP.
    offsets = np.arange(140)
    prices = 100 + offsets * .07 + 3 * np.sin(offsets * .7)
    frame = pd.DataFrame({"date": pd.date_range("2024-01-01", periods=140, freq="B"),
        "symbol": "TEST", "open": prices, "high": prices + 1, "low": prices - 1,
        "close": prices, "volume": 1000 + offsets})
    source = tmp_path / "source.csv"
    frame.to_csv(source, index=False, date_format="%Y-%m-%d")
    before = source.read_bytes()
    destination = tmp_path / "catalogs"
    provenance = json.loads(exporter.export_catalogs(source, destination).read_text())
    registry = json.loads((destination / "registry.json").read_text())
    assert len(registry["models"]) == 2
    assert {model["status"] for model in registry["models"]} == {"Archived"}
    assert provenance["observations"] == 140 and provenance["symbols"] == ["TEST"]
    periods = []
    for record in provenance["registered_models"]:
        report = json.loads((destination / record["backtest_report"]).read_text())
        assert report["model_name"] == record["model_name"]
        assert report["initial_capital"] == 100000
        assert report["start_date"] > "2024-04-01"
        assert (destination / record["benchmark_comparison"]).is_file()
        periods.append((report["start_date"], report["end_date"]))
    assert periods[0] == periods[1]
    assert source.read_bytes() == before
