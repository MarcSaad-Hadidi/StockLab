"""Validate benchmark accounting before per-file atomic CSV/JSON publication."""

from dataclasses import asdict, replace
from importlib import import_module
import json
import os
from pathlib import Path
import subprocess

import numpy as np
import pandas as pd
import pytest

from stocklab_ml.data.models import StorageError
from test_backtesting import market, simple_prices
from test_benchmark import compare, hold_backtest


def save(result, **kwargs):
    return import_module("stocklab_ml.backtesting").save_benchmark_comparison(result, **kwargs)


def example():
    prices = simple_prices()
    return compare(hold_backtest(prices), prices)


def test_roundtrip_and_overwrite_are_deterministic(tmp_path):
    result = example()
    saved = save(result, output_dir=tmp_path)
    for path, frame in [(saved.allocations_path, result.benchmark_allocations),
            (saved.equity_path, result.benchmark_equity_curve),
            (saved.positions_path, result.benchmark_positions_history)]:
        loaded = pd.read_csv(path)
        assert list(loaded) == list(frame)
        assert len(loaded) == len(frame)
    payload = json.loads(saved.comparison_path.read_text())
    assert payload == json.loads(json.dumps({"benchmark": asdict(result.benchmark_report),
                                             "comparison": asdict(result.comparison_report)}, allow_nan=False))
    assert payload["benchmark"]["dividends_included"] is False
    before = {path: path.read_bytes() for path in tmp_path.iterdir()}
    with pytest.raises(StorageError):
        save(result, output_dir=tmp_path)
    save(result, output_dir=tmp_path, overwrite=True)
    assert before == {path: path.read_bytes() for path in tmp_path.iterdir()}


@pytest.mark.parametrize("filename", ["buy_and_hold_allocations.csv", "buy_and_hold_equity_curve.csv",
    "buy_and_hold_positions.csv", "comparison.json"])
def test_any_collision_prevents_all_writes(tmp_path, filename):
    (tmp_path / filename).write_text("keep")
    with pytest.raises(StorageError):
        save(example(), output_dir=tmp_path)
    assert [(path.name, path.read_text()) for path in tmp_path.iterdir()] == [(filename, "keep")]


@pytest.mark.parametrize("alias", ["direct_source", "hardlink_source", "hardlink_outputs", "directory_source"])
def test_source_output_aliases_are_rejected_even_with_overwrite(tmp_path, alias):
    source = tmp_path / "source.csv"
    source.write_text("original")
    first = tmp_path / "buy_and_hold_allocations.csv"
    second = tmp_path / "buy_and_hold_equity_curve.csv"
    if alias == "direct_source":
        source = first
        source.write_text("original")
    elif alias.startswith("hardlink"):
        os.link(source, first)
        if alias.endswith("outputs"):
            os.link(source, second)
    else:
        # Windows directory junctions exercise real path resolution without
        # requiring the machine's optional CreateSymbolicLink privilege.
        linked = tmp_path / "linked"
        if os.name == "nt":
            subprocess.run(["cmd", "/c", "mklink", "/J", str(linked), str(tmp_path)],
                           check=True, capture_output=True)
        else:
            linked.symlink_to(tmp_path, target_is_directory=True)
        source = linked / first.name
        first.write_text("original")
    with pytest.raises(StorageError):
        save(example(), output_dir=tmp_path, overwrite=True,
             source_paths=() if alias.endswith("outputs") else (source,))
    assert source.read_text() == "original"
    assert not (tmp_path / "comparison.json").exists()


@pytest.mark.parametrize("overwrite", [False, True])
def test_late_atomic_failure_leaves_only_complete_files(tmp_path, monkeypatch, overwrite):
    from stocklab_ml.data import storage
    operation = storage.os.replace if overwrite else storage.os.link
    def fail(source, destination, *args, **kwargs):
        if destination.name == "comparison.json":
            raise OSError("synthetic failure")
        return operation(source, destination, *args, **kwargs)
    monkeypatch.setattr(storage.os, "replace" if overwrite else "link", fail)
    with pytest.raises(StorageError):
        save(example(), output_dir=tmp_path, overwrite=overwrite)
    assert pd.read_csv(tmp_path / "buy_and_hold_allocations.csv").quantity.tolist() == [1000]
    assert len(pd.read_csv(tmp_path / "buy_and_hold_equity_curve.csv")) == 4
    assert not (tmp_path / "comparison.json").exists()
    assert not list(tmp_path.glob("*.tmp"))


@pytest.mark.parametrize("bad", ["raw", "report_type", "comparison_type", "target", "quantity", "invested",
    "residual", "entry_date", "entry_price", "final_mark_date", "final_mark_price", "final_market_value",
    "component_value", "component_return", "cash", "positions_value", "total_value", "drawdown", "empty_equity",
    "equity_date", "equity_dtype", "allocation_dtype", "allocation_schema", "allocation_order", "nan", "infinity",
    "position_quantity", "position_price", "position_value", "future_mark", "missing_position",
    "final_value", "total_return", "max_drawdown", "initial_capital", "symbols", "invested_symbols",
    "symbol_count", "commission", "slippage", "dividends", "quantity_decimals", "entry_timing", "valuation_timing",
    "price_adjustment", "return_delta", "value_delta", "ai_return", "ai_drawdown", "benchmark_return",
    "same_period", "same_symbols", "same_capital", "same_dates", "method", "model_name", "end_date", "date_list"])
def test_tampered_results_rejected_before_directory_creation(tmp_path, bad):
    result = example()
    report_changes = {"final_value": {"final_value": 1}, "total_return": {"total_return": .5},
        "max_drawdown": {"max_drawdown": .5}, "initial_capital": {"initial_capital": 1},
        "symbols": {"symbols": ("MSFT",)}, "invested_symbols": {"invested_symbols": ()},
        "symbol_count": {"symbol_count": True}, "commission": {"commission": 1}, "slippage": {"slippage": 1},
        "dividends": {"dividends_included": True}, "quantity_decimals": {"quantity_decimals": 7},
        "entry_timing": {"entry_timing": "same-day open"}, "valuation_timing": {"valuation_timing": "future close"},
        "price_adjustment": {"price_adjustment_note": "readjusted"}}
    comparison_changes = {"return_delta": {"return_delta": .5}, "value_delta": {"final_value_delta": 5},
        "ai_return": {"ai_total_return": .5}, "ai_drawdown": {"ai_max_drawdown": -1},
        "benchmark_return": {"benchmark_total_return": .5}, "same_period": {"same_period": False},
        "same_symbols": {"same_symbols": False}, "same_capital": {"same_initial_capital": False},
        "same_dates": {"same_equity_dates": False}, "method": {"benchmark_method": "winner"},
        "model_name": {"model_name": "../bad"}, "end_date": {"end_date": "2024-01-09"},
        "date_list": {"equity_dates": ("2024-01-05", "2024-01-10")}}
    allocation_columns = {"target": "target_allocation", "quantity": "quantity", "invested": "invested_amount",
        "residual": "residual_cash", "entry_price": "entry_price", "final_mark_price": "final_mark_price",
        "final_market_value": "final_market_value", "component_value": "component_final_value", "component_return": "component_return"}
    if bad == "raw": result = {}
    elif bad == "report_type": result = replace(result, benchmark_report={})
    elif bad == "comparison_type": result = replace(result, comparison_report={})
    elif bad in report_changes:
        result = replace(result, benchmark_report=replace(result.benchmark_report, **report_changes[bad]))
    elif bad in comparison_changes:
        result = replace(result, comparison_report=replace(result.comparison_report, **comparison_changes[bad]))
    elif bad in allocation_columns: result.benchmark_allocations.loc[0, allocation_columns[bad]] = 1
    elif bad in ["entry_date", "final_mark_date"]:
        result.benchmark_allocations.loc[0, bad] = pd.Timestamp("2024-01-05" if bad == "entry_date" else "2024-01-11")
    elif bad in ["cash", "positions_value", "total_value", "drawdown"]:
        result.benchmark_equity_curve.loc[1, bad] = 1
    elif bad == "empty_equity": result = replace(result, benchmark_equity_curve=result.benchmark_equity_curve.iloc[:0])
    elif bad == "equity_date": result.benchmark_equity_curve.loc[1, "date"] = pd.Timestamp("2024-01-06")
    elif bad == "equity_dtype": result = replace(result, benchmark_equity_curve=result.benchmark_equity_curve.astype({"cash": "float32"}))
    elif bad == "allocation_dtype": result = replace(result, benchmark_allocations=result.benchmark_allocations.astype({"quantity": "float32"}))
    elif bad == "allocation_schema": result = replace(result, benchmark_allocations=result.benchmark_allocations.drop(columns="symbol"))
    elif bad == "allocation_order": result = replace(result, benchmark_allocations=result.benchmark_allocations.set_axis([9]))
    elif bad in ["nan", "infinity"]: result.benchmark_allocations.loc[0, "quantity"] = np.nan if bad == "nan" else np.inf
    elif bad in ["position_quantity", "position_price", "position_value"]:
        column = {"position_quantity": "quantity", "position_price": "mark_price", "position_value": "market_value"}[bad]
        result.benchmark_positions_history.loc[0, column] = 1
    elif bad == "future_mark": result.benchmark_positions_history.loc[0, "mark_date"] = pd.Timestamp("2024-01-11")
    elif bad == "missing_position": result = replace(result, benchmark_positions_history=result.benchmark_positions_history.iloc[1:].reset_index(drop=True))
    destination = tmp_path / "outputs"
    with pytest.raises(import_module("stocklab_ml.backtesting").BacktestError):
        save(result, output_dir=destination)
    assert not destination.exists()


def test_default_directory_and_git_ignore(tmp_path, monkeypatch):
    from stocklab_ml.backtesting import benchmark_storage
    assert benchmark_storage.DEFAULT_BENCHMARK_DIR.as_posix().endswith("/ml/results/benchmark")
    monkeypatch.setattr(benchmark_storage, "DEFAULT_BENCHMARK_DIR", tmp_path)
    saved = save(example())
    assert saved.comparison_path == tmp_path / "comparison.json"
    check = subprocess.run(["git", "check-ignore", "ml/results/benchmark/comparison.json"],
                           cwd=Path(__file__).resolve().parents[2], capture_output=True)
    assert check.returncode == 0


@pytest.mark.parametrize("kind", ["all_cash", "one_unavailable", "thirds", "precision_cash"])
def test_valid_uninvested_and_rational_tranche_results_can_be_saved(tmp_path, kind):
    rows = [("2024-01-05", "AAPL", 333, 333)]
    if kind != "all_cash":
        rows.append(("2024-01-08", "AAPL", 333, 333))
    if kind == "one_unavailable":
        rows.append(("2024-01-05", "MSFT", 100, 100))
    if kind == "thirds":
        rows += [(day, symbol, 333, 333) for symbol in ["MSFT", "NVDA"]
                 for day in ["2024-01-05", "2024-01-08"]]
    prices = market(rows)
    capital = 1e-7 if kind == "precision_cash" else 100000
    result = compare(hold_backtest(prices, capital), prices)
    saved = save(result, output_dir=tmp_path)
    payload = json.loads(saved.comparison_path.read_text())
    assert payload["benchmark"]["final_value"] == capital
    assert payload["benchmark"]["total_return"] == 0
    assert pd.read_csv(saved.allocations_path).residual_cash.ge(0).all()
