"""Reject inconsistent accounting before safe per-file publication."""

from dataclasses import asdict, replace
from importlib import import_module
from fractions import Fraction
import json
import math
import os

import numpy as np
import pandas as pd
import pytest

from stocklab_ml.data.models import StorageError
from test_backtesting import decisions, market, run, simple_decisions, simple_prices


def save(result, **kwargs):
    return import_module("stocklab_ml.backtesting").save_backtest_result(result, **kwargs)


def test_roundtrip_stable_schemas_report_and_explicit_overwrite(tmp_path):
    result = run(simple_decisions(), simple_prices())
    paths = save(result, output_dir=tmp_path)
    for path, frame in [(paths.trades_path, result.trades), (paths.equity_path, result.equity_curve),
                        (paths.positions_path, result.positions_history)]:
        saved = pd.read_csv(path)
        assert list(saved) == list(frame)
        assert len(saved) == len(frame)
        assert not any(column.startswith("Unnamed") for column in saved)
    payload = json.loads(paths.report_path.read_text())
    assert payload == json.loads(json.dumps(asdict(result.report), allow_nan=False))
    assert payload["config"]["initial_capital"] == 100000
    before = {path: path.read_bytes() for path in tmp_path.iterdir()}
    with pytest.raises(StorageError):
        save(result, output_dir=tmp_path)
    assert before == {path: path.read_bytes() for path in tmp_path.iterdir()}
    save(result, output_dir=tmp_path, overwrite=True)
    assert before == {path: path.read_bytes() for path in tmp_path.iterdir()}


def test_empty_ledger_and_positions_keep_headers(tmp_path):
    source = decisions([("2024-01-05", "AAPL", .5)])
    result = run(source, simple_prices())
    saved = save(result, output_dir=tmp_path)
    assert pd.read_csv(saved.trades_path).empty
    assert pd.read_csv(saved.positions_path).empty
    assert json.loads(saved.report_path.read_text())["win_rate"] == 0


@pytest.mark.parametrize("filename", ["trades.csv", "equity_curve.csv", "positions.csv", "report.json"])
def test_any_collision_prevents_all_publication(tmp_path, filename):
    (tmp_path / filename).write_text("keep")
    with pytest.raises(StorageError):
        save(run(simple_decisions(), simple_prices()), output_dir=tmp_path)
    assert [(path.name, path.read_text()) for path in tmp_path.iterdir()] == [(filename, "keep")]


@pytest.mark.parametrize("alias", ["direct_source", "hardlink_source", "hardlink_outputs"])
def test_sources_and_output_aliases_cannot_be_overwritten(tmp_path, alias):
    source = tmp_path / "source.csv"
    source.write_text("original")
    if alias == "direct_source":
        source = tmp_path / "trades.csv"
        source.write_text("original")
    elif alias == "hardlink_source":
        os.link(source, tmp_path / "trades.csv")
    else:
        os.link(source, tmp_path / "trades.csv")
        os.link(source, tmp_path / "positions.csv")
    with pytest.raises(StorageError):
        save(run(simple_decisions(), simple_prices()), output_dir=tmp_path,
             source_paths=() if alias == "hardlink_outputs" else (source,), overwrite=True)
    assert source.read_text() == "original"
    assert not (tmp_path / "report.json").exists()


@pytest.mark.parametrize("overwrite", [False, True])
def test_atomic_report_failure_leaves_complete_csvs_and_no_temporary_files(tmp_path, monkeypatch, overwrite):
    from stocklab_ml.data import storage

    operation = storage.os.replace if overwrite else storage.os.link
    def fail_report(source, destination, *args, **kwargs):
        if destination.name == "report.json":
            raise OSError("synthetic publication failure")
        return operation(source, destination, *args, **kwargs)
    monkeypatch.setattr(storage.os, "replace" if overwrite else "link", fail_report)
    result = run(simple_decisions(), simple_prices())
    with pytest.raises(StorageError):
        save(result, output_dir=tmp_path, overwrite=overwrite)
    assert pd.read_csv(tmp_path / "trades.csv").signal.tolist() == ["BUY", "SELL"]
    assert len(pd.read_csv(tmp_path / "equity_curve.csv")) == 4
    assert len(pd.read_csv(tmp_path / "positions.csv")) == 2
    assert not (tmp_path / "report.json").exists()
    assert not list(tmp_path.glob("*.tmp"))


@pytest.mark.parametrize("bad", [
    "raw", "report", "final_value", "return", "win_rate", "drawdown_report", "counts", "skips",
    "symbols", "initial_capital", "commission", "execution_timing", "date_bounds", "allocation_policy", "exposure_policy",
    "ledger_amount", "ledger_cash", "ledger_quantity", "ledger_cost", "ledger_pnl", "buy_pnl",
    "same_day", "duplicate_decision", "opening_value", "hold_trade", "model", "confidence", "trade_order", "trade_schema", "trade_dtype",
    "equity_cash", "equity_value", "equity_drawdown", "equity_order", "equity_empty",
    "position_quantity", "position_cost", "position_mark", "position_value", "position_pnl", "opening_mark",
    "missing_position", "position_schema", "nan", "infinite",
])
def test_tampered_result_fails_before_creating_output_directory(tmp_path, bad):
    api = import_module("stocklab_ml.backtesting")
    result = run(simple_decisions(), simple_prices())
    report_changes = {
        "final_value": {"final_value": 1}, "return": {"total_return": .5},
        "win_rate": {"win_rate": .5}, "drawdown_report": {"max_drawdown": .5},
        "counts": {"executed_trades": 5}, "skips": {"skipped_low_confidence": True},
        "symbols": {"symbols": ("MSFT",)}, "initial_capital": {"initial_capital": 1},
        "commission": {"commission": 1}, "execution_timing": {"execution_timing": "same close"},
        "date_bounds": {"end_date": "2024-01-09"},
    }
    if bad == "raw": result = {}
    elif bad == "report": result = replace(result, report={})
    elif bad in report_changes: result = replace(result, report=replace(result.report, **report_changes[bad]))
    elif bad in ["allocation_policy", "exposure_policy"]:
        field = "max_trade_allocation" if bad == "allocation_policy" else "max_symbol_exposure"
        result = replace(result, report=replace(result.report, config=replace(result.report.config, **{field: .0001})))
    elif bad.startswith("ledger_"):
        column = {"ledger_amount": "total_amount", "ledger_cash": "cash_after", "ledger_quantity": "quantity",
                  "ledger_cost": "average_cost_after", "ledger_pnl": "realized_pnl"}[bad]
        result.trades.loc[1 if bad == "ledger_pnl" else 0, column] = 1.0
    elif bad == "buy_pnl": result.trades.loc[0, "realized_pnl"] = 0
    elif bad == "same_day": result.trades.loc[0, "execution_date"] = result.trades.decision_date.iloc[0]
    elif bad == "duplicate_decision": result.trades.loc[1, "decision_date"] = result.trades.decision_date.iloc[0]
    elif bad == "opening_value": result.trades["portfolio_value_before"] = 1.0
    elif bad == "hold_trade": result.trades.loc[0, "signal"] = "HOLD"
    elif bad == "model": result.trades.loc[0, "model_name"] = "other"
    elif bad == "confidence": result.trades.loc[0, "confidence"] = .5
    elif bad == "opening_mark": result.positions_history["opening_mark_price"] = 1.0
    elif bad == "trade_order": result = replace(result, trades=result.trades.iloc[::-1].reset_index(drop=True))
    elif bad == "trade_schema": result = replace(result, trades=result.trades.drop(columns="quantity"))
    elif bad == "trade_dtype": result = replace(result, trades=result.trades.astype({"quantity": "float32"}))
    elif bad == "equity_cash": result.equity_curve.loc[1, "cash"] = 1
    elif bad == "equity_value": result.equity_curve.loc[1, "total_value"] = 1
    elif bad == "equity_drawdown": result.equity_curve.loc[1, "drawdown"] = .9
    elif bad == "equity_order": result = replace(result, equity_curve=result.equity_curve.iloc[::-1].reset_index(drop=True))
    elif bad == "equity_empty": result = replace(result, equity_curve=result.equity_curve.iloc[:0])
    elif bad.startswith("position_") and bad != "position_schema":
        column = {"position_quantity": "quantity", "position_cost": "average_cost", "position_mark": "mark_price",
                  "position_value": "market_value", "position_pnl": "unrealized_pnl"}[bad]
        result.positions_history.loc[0, column] = 1.0
    elif bad == "missing_position": result = replace(result, positions_history=result.positions_history.iloc[:0])
    elif bad == "position_schema": result = replace(result, positions_history=result.positions_history.drop(columns="mark_price"))
    elif bad == "nan": result.equity_curve.loc[1, "total_value"] = np.nan
    elif bad == "infinite": result.positions_history.loc[0, "market_value"] = np.inf
    destination = tmp_path / "outputs"
    with pytest.raises(api.BacktestError):
        save(result, output_dir=destination)
    assert not destination.exists()


def test_default_directory_and_ignored_artifacts(tmp_path, monkeypatch):
    from stocklab_ml.backtesting import storage

    assert storage.DEFAULT_BACKTEST_DIR.as_posix().endswith("/ml/results/backtesting")
    monkeypatch.setattr(storage, "DEFAULT_BACKTEST_DIR", tmp_path)
    saved = save(run(simple_decisions(), simple_prices()))
    assert saved.trades_path == tmp_path / "trades.csv"
    assert saved.equity_path == tmp_path / "equity_curve.csv"
    assert saved.positions_path == tmp_path / "positions.csv"
    assert saved.report_path == tmp_path / "report.json"


def test_storage_accepts_valid_asynchronous_open_position_snapshots(tmp_path):
    source = decisions([("2024-01-05", "AAPL", .8), ("2024-01-08", "MSFT", .8)])
    prices = market([("2024-01-05", "AAPL", 100, 100), ("2024-01-08", "AAPL", 100, 200),
                     ("2024-01-10", "AAPL", 300, 300), ("2024-01-08", "MSFT", 100, 100),
                     ("2024-01-09", "MSFT", 100, 100)])
    result = run(source, prices)
    saved = save(result, output_dir=tmp_path)
    assert json.loads(saved.report_path.read_text())["open_positions_final"] == 2


def test_valid_numpy_upstream_counters_produce_plain_json(tmp_path):
    source = simple_decisions()
    source = replace(source, report=replace(source.report, buy_count=np.int64(1),
                                           sell_count=np.int64(1), hold_count=np.int64(1)))
    result = run(source, simple_prices())
    saved = save(result, output_dir=tmp_path)
    payload = json.loads(saved.report_path.read_text())
    assert payload["buy_decisions"] == payload["sell_decisions"] == payload["hold_decisions"] == 1


def test_valid_numpy_report_scalars_are_serialized_before_any_write(tmp_path):
    result = run(simple_decisions(), simple_prices())
    result = replace(result, report=replace(result.report, final_cash=np.float32(102000),
                                            final_positions_value=np.float32(0), executed_trades=np.int64(2)))
    saved = save(result, output_dir=tmp_path)
    payload = json.loads(saved.report_path.read_text())
    assert payload["final_cash"] == 102000 and payload["executed_trades"] == 2


@pytest.mark.parametrize("price", [.991, 14.56, 123.4567, 333.33333333])
def test_valid_floored_fractional_shares_are_storable(tmp_path, price):
    source = decisions([("2024-01-05", "AAPL", .8)])
    prices = market([("2024-01-05", "AAPL", price, price), ("2024-01-08", "AAPL", price, price)])
    result = run(source, prices)
    assert round(result.trades.quantity.iloc[0], 8) == result.trades.quantity.iloc[0]
    saved = save(result, output_dir=tmp_path)
    assert len(pd.read_csv(saved.trades_path)) == 1


def test_exact_decimal_cash_budget_preserves_quantity_grid(tmp_path):
    from test_backtesting import config

    source = decisions([("2024-01-05", "AAPL", .8)])
    prices = market([("2024-01-05", "AAPL", 3, 3), ("2024-01-08", "AAPL", 3, 3)])
    result = run(source, prices, config=config(initial_capital=.3, max_symbol_exposure=1, max_trade_allocation=1))
    assert result.trades.quantity.iloc[0] == .1
    assert result.trades.total_amount.iloc[0] == .3 and result.report.final_cash == 0
    save(result, output_dir=tmp_path)


@pytest.mark.parametrize("precision", [0, 2, 8])
def test_decimal_grid_boundary_does_not_drop_affordable_shares(tmp_path, precision):
    from test_backtesting import config

    source = decisions([("2024-01-05", "AAPL", .8), ("2024-01-08", "AAPL", .2)])
    prices = market([(day, "AAPL", .1, .1) for day in ["2024-01-05", "2024-01-08", "2024-01-09"]])
    result = run(source, prices, config=config(initial_capital=.3, max_symbol_exposure=1,
                                              max_trade_allocation=1, quantity_decimals=precision))
    assert result.trades.quantity.tolist() == [3, 3]
    assert result.trades.total_amount.tolist() == [.3, .3]
    assert result.trades.cash_after.tolist() == [0, .3]
    assert result.equity_curve.total_value.tolist() == [.3, .3, .3]
    assert result.report.total_return == result.report.win_rate == result.report.max_drawdown == 0
    save(result, output_dir=tmp_path)


def test_decimal_allocation_cap_does_not_drop_an_affordable_unit(tmp_path):
    from test_backtesting import config

    source = decisions([("2024-01-05", "AAPL", .8)])
    prices = market([(day, "AAPL", .01, .01) for day in ["2024-01-05", "2024-01-08"]])
    result = run(source, prices, config=config(initial_capital=.7, quantity_decimals=0))
    assert result.trades.quantity.tolist() == [7]
    assert result.trades.total_amount.tolist() == [.07]
    assert result.report.final_cash == .63 and result.report.final_value == .7
    assert result.report.total_return == 0
    save(result, output_dir=tmp_path)


def test_repeated_decimal_cash_debits_leave_the_last_affordable_share(tmp_path):
    from test_backtesting import config

    days = ["2024-01-05", "2024-01-08", "2024-01-09", "2024-01-10", "2024-01-11"]
    source = decisions([(day, "AAPL", p) for day, p in zip(days[:-1], [.8, .8, .8, .2])])
    prices = market([(day, "AAPL", .1, .1) for day in days])
    result = run(source, prices, config=config(initial_capital=.3, max_symbol_exposure=1,
                                              max_trade_allocation=.5, quantity_decimals=0))
    assert result.trades.quantity.tolist() == [1, 1, 1, 3]
    assert result.trades.cash_after.tolist() == [.2, .1, 0, .3]
    assert result.report.final_value == .3 and result.report.total_return == 0
    save(result, output_dir=tmp_path)


def test_large_quantity_uses_next_affordable_float_instead_of_skipping(tmp_path):
    from test_backtesting import config

    source = decisions([("2024-01-05", "AAPL", .8)])
    prices = market([(day, "AAPL", .1, .1) for day in ["2024-01-05", "2024-01-08"]])
    cfg = config(initial_capital=3602879701896397.5, max_symbol_exposure=1,
                 max_trade_allocation=1, quantity_decimals=0)
    result = run(source, prices, config=cfg)
    assert result.trades.quantity.tolist() == [3.602879701896397e16]
    assert result.trades.total_amount.tolist() == [3602879701896397.0]
    assert result.trades.cash_after.tolist() == [.5]
    assert result.report.skipped_buying_power == 0
    assert result.report.final_value == cfg.initial_capital
    save(result, output_dir=tmp_path)


def test_storage_overflow_is_a_backtest_error_before_writing(tmp_path):
    api = import_module("stocklab_ml.backtesting")
    source = decisions([("2024-01-05", symbol, .8) for symbol in ["AAPL", "MSFT"]])
    prices = market([(day, symbol, 100, 100) for symbol in ["AAPL", "MSFT"]
                     for day in ["2024-01-05", "2024-01-08"]])
    result = run(source, prices)
    result.positions_history["mark_price"] = 1e306
    result.positions_history["market_value"] = 1e308
    result.positions_history["unrealized_pnl"] = 1e308
    destination = tmp_path / "outputs"
    with pytest.raises(api.BacktestError, match="finite|precision"):
        save(result, output_dir=destination)
    assert not destination.exists()


def test_inflated_opening_value_with_consistently_rewritten_outputs_is_rejected(tmp_path):
    api = import_module("stocklab_ml.backtesting")
    source = decisions([("2024-01-05", "AAPL", .8), ("2024-01-08", "MSFT", .8)])
    prices = market([("2024-01-05", "AAPL", 100, 100), ("2024-01-08", "AAPL", 100, 100),
                     ("2024-01-09", "AAPL", 100, 100), ("2024-01-08", "MSFT", 100, 100),
                     ("2024-01-09", "MSFT", 100, 100)])
    result = run(source, prices)
    result.trades.loc[1, ["portfolio_value_before", "quantity", "total_amount", "cash_after", "position_quantity_after"]] = [1000000, 500, 50000, 40000, 500]
    result.equity_curve.loc[2, ["cash", "positions_value"]] = [40000, 60000]
    result.positions_history.loc[result.positions_history.symbol.eq("MSFT"), ["quantity", "market_value"]] = [500, 50000]
    result = replace(result, report=replace(result.report, final_cash=40000, final_positions_value=60000))
    destination = tmp_path / "outputs"
    with pytest.raises(api.BacktestError, match="Opening|opening"):
        save(result, output_dir=destination)
    assert not destination.exists()


def test_opening_valuation_includes_holdings_sold_later_that_day(tmp_path):
    source = decisions([("2024-01-05", "MSFT", .8), ("2024-01-08", "AAPL", .8),
                        ("2024-01-08", "MSFT", .2)])
    prices = market([(day, symbol, 100, 100) for symbol in ["AAPL", "MSFT"]
                     for day in ["2024-01-05", "2024-01-08", "2024-01-09"]])
    result = run(source, prices)
    assert result.trades.symbol.tolist() == ["MSFT", "AAPL", "MSFT"]
    assert result.trades.portfolio_value_before.tolist() == [100000, 100000, 100000]
    final = result.positions_history[result.positions_history.date.eq(pd.Timestamp("2024-01-09"))]
    assert final.symbol.tolist() == ["AAPL"]
    saved = save(result, output_dir=tmp_path)
    assert json.loads(saved.report_path.read_text())["final_cash"] == 90000


@pytest.mark.parametrize("opening,closing,capital,pnl,win", [
    (1e-300, math.nextafter(1e-300, math.inf), 1e-308, 5e-324, 1),
    (math.nextafter(1e-300, math.inf), 1e-300, 1.0000000000000004e-308, -5e-324, 0),
])
def test_subnormal_accounted_gain_and_loss_are_not_flat(tmp_path, opening, closing, capital, pnl, win):
    from test_backtesting import config

    source = decisions([("2024-01-05", "AAPL", .8), ("2024-01-08", "AAPL", .2)])
    prices = market([("2024-01-05", "AAPL", opening, opening),
                     ("2024-01-08", "AAPL", opening, closing), ("2024-01-09", "AAPL", closing, closing)])
    cfg = config(initial_capital=capital, max_symbol_exposure=1, max_trade_allocation=1)
    result = run(source, prices, config=cfg)
    assert result.trades.quantity.tolist() == [1e-8, 1e-8]
    assert result.trades.realized_pnl.iloc[-1] == pnl
    assert result.positions_history.unrealized_pnl.iloc[-1] == pnl
    assert result.report.win_rate == win
    assert result.report.final_cash - capital == pnl
    save(result, output_dir=tmp_path)


def test_pnl_preserves_accounted_cost_basis_across_weighted_buys(tmp_path):
    from test_backtesting import config

    source = decisions([("2024-01-05", "AAPL", .8), ("2024-01-08", "AAPL", .8),
                        ("2024-01-09", "AAPL", .2)])
    prices = market([("2024-01-05", "AAPL", 5.01, 5.01), ("2024-01-08", "AAPL", 5.01, 5.01),
                     ("2024-01-09", "AAPL", 3.35, 3.35),
                     ("2024-01-10", "AAPL", 3.9033333333333333, 3.9033333333333333)])
    result = run(source, prices, config=config(initial_capital=1000, max_trade_allocation=.01, quantity_decimals=0))
    assert result.trades.quantity.tolist() == [1, 2, 3]
    assert result.trades.total_amount.tolist() == [5.01, 6.7, 11.709999999999999]
    assert result.trades.realized_pnl.iloc[-1] == -1e-15
    assert result.positions_history.unrealized_pnl.iloc[-1] == -1.66
    assert result.report.win_rate == 0
    save(result, output_dir=tmp_path)


def test_exact_exposure_subtraction_does_not_invent_buying_power(tmp_path):
    from test_backtesting import config

    source = decisions([("2024-01-05", "AAPL", .8), ("2024-01-08", "AAPL", .8)])
    prices = market([("2024-01-05", "AAPL", 1, 1), ("2024-01-08", "AAPL", 1, 1),
                     ("2024-01-09", "AAPL", .9999999999999999, .9999999999999999)])
    cfg = config(initial_capital=1e16, max_symbol_exposure=.7, max_trade_allocation=.7, quantity_decimals=0)
    result = run(source, prices, config=cfg)
    assert result.trades.quantity.tolist() == [7000000000000000.0]
    assert result.report.skipped_buying_power == 1
    assert result.report.final_cash == 3000000000000000.0
    save(result, output_dir=tmp_path)


def test_replay_rejects_exposure_breach_hidden_by_rounded_operands(tmp_path):
    from test_backtesting import config

    api = import_module("stocklab_ml.backtesting")
    source = decisions([("2024-01-05", "AAPL", .8)])
    prices = market([("2024-01-05", "AAPL", 1, 1), ("2024-01-08", "AAPL", 1, 1),
                     ("2024-01-09", "AAPL", .9999999999999999, .9999999999999999)])
    cfg = config(initial_capital=1e16, max_symbol_exposure=.7, max_trade_allocation=.7, quantity_decimals=0)
    result = run(source, prices, config=cfg)
    extra = result.trades.iloc[:1].copy(deep=True)
    extra.loc[0, "decision_date"] = pd.Timestamp("2024-01-08")
    extra.loc[0, "execution_date"] = pd.Timestamp("2024-01-09")
    extra.loc[0, ["quantity", "execution_price", "total_amount", "cash_after", "position_quantity_after"]] = [1, .9999999999999999, .9999999999999999, 2999999999999999.0, 7000000000000001.0]
    result = replace(result, trades=pd.concat([result.trades, extra], ignore_index=True))
    result.equity_curve.loc[2, ["cash", "positions_value"]] = [2999999999999999.0, 7000000000000000.0]
    result.positions_history.loc[1, ["quantity", "market_value", "unrealized_pnl"]] = [7000000000000001.0, 7000000000000000.0, -.9999999999999999]
    result = replace(result, report=replace(result.report, decision_count=2, buy_decisions=2,
                    executed_trades=2, executed_buys=2, final_cash=2999999999999999.0,
                    final_positions_value=7000000000000000.0))
    destination = tmp_path / "outputs"
    with pytest.raises(api.BacktestError, match="policy"):
        save(result, output_dir=destination)
    assert not destination.exists()


def test_published_notional_stays_within_exact_allocation_budget(tmp_path):
    from test_backtesting import config

    source = decisions([("2024-01-05", "AAPL", .8)])
    prices = market([(day, "AAPL", .12345678901234568, .12345678901234568)
                     for day in ["2024-01-05", "2024-01-08"]])
    cfg = config(initial_capital=.12345678, max_symbol_exposure=1, max_trade_allocation=.12345678901234568)
    result = run(source, prices, config=cfg)
    assert result.trades.quantity.tolist() == [.12345677]
    budget = Fraction("0.12345678") * Fraction("0.12345678901234568")
    assert Fraction(str(result.trades.total_amount.iloc[0])) <= budget
    save(result, output_dir=tmp_path)


@pytest.mark.parametrize("close_position", [False, True])
def test_exact_cash_preserves_flat_large_balance_equity(tmp_path, close_position):
    from test_backtesting import config

    capital, price = 9994156765551810.0, 695361500203047.0
    source = decisions([("2024-01-05", "AAPL", .8),
                        ("2024-01-08", "AAPL", .2 if close_position else .5)])
    prices = market([(day, "AAPL", price, price) for day in
                     ["2024-01-05", "2024-01-08", "2024-01-09"]])
    cfg = config(initial_capital=capital, max_symbol_exposure=.07,
                 max_trade_allocation=.07, quantity_decimals=0)
    result = run(source, prices, config=cfg)
    assert result.trades.quantity.tolist() == ([1, 1] if close_position else [1])
    assert result.equity_curve.total_value.tolist() == [capital] * 3
    assert result.report.total_return == result.report.max_drawdown == 0
    if close_position:
        assert result.report.final_cash == capital
        assert result.trades.realized_pnl.iloc[-1] == 0
    save(result, output_dir=tmp_path)


def test_replay_rejects_phantom_gain_from_rounded_cash(tmp_path):
    from test_backtesting import config

    api = import_module("stocklab_ml.backtesting")
    capital, price = 9994156765551810.0, 695361500203047.0
    prices = market([(day, "AAPL", price, price) for day in ["2024-01-05", "2024-01-08"]])
    result = run(decisions([("2024-01-05", "AAPL", .8)]), prices,
                 config=config(initial_capital=capital, max_symbol_exposure=.07,
                               max_trade_allocation=.07, quantity_decimals=0))
    forged_value = 9994156765551812.0
    result.equity_curve.loc[1, "total_value"] = forged_value
    result = replace(result, report=replace(result.report, final_value=forged_value,
                                           total_return=forged_value / capital - 1))
    with pytest.raises(api.BacktestError, match="equity"):
        save(result, output_dir=tmp_path / "forged")
    assert not (tmp_path / "forged").exists()
