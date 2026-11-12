"""Opt-in monthly replay fits both heads and admits no sub-target strategy."""

import importlib.util
import json
from pathlib import Path
import sys

import pandas as pd
import pytest


@pytest.fixture
def runner(monkeypatch):
    examples = Path(__file__).parents[1] / "examples"
    monkeypatch.syspath_prepend(str(examples))
    spec = importlib.util.spec_from_file_location("monthly_runner", examples / "downside_research.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def report(cagr, dd, downside_sells=0):
    return {"annualized_return": cagr, "max_drawdown": dd, "sells": 3,
        "downside_sells": downside_sells, "benchmark_annualized_return": .14,
        "benchmark_max_drawdown": .40, "target_met": cagr >= .20}


def test_monthly_dual_cannot_pass_below_twenty_percent_or_spy(runner):
    control, dual = report(.18, .30), report(.19, .20, 2)
    forecast = {"auc": .7, "brier": .18, "reference_brier": .25}
    limits = {"protected_cagr": .0977, "original_drawdown": .3268}
    assert runner.admissions(control, dual, forecast, limits)["dual"]
    assert runner.admissions(control, dual, forecast, limits, monthly=True) == {"control": False, "dual": False}
    dual = report(.25, .20, 2)
    assert runner.admissions(control, dual, forecast, limits, monthly=True)["dual"]
    dual["benchmark_annualized_return"] = .26
    assert not runner.admissions(control, dual, forecast, limits, monthly=True)["dual"]


@pytest.mark.parametrize("mode", ["reject", "control", "dual"])
def test_monthly_both_heads_are_fresh_and_later_work_follows_saved_admission(runner, tmp_path, monkeypatch, mode):
    output = tmp_path / "out"
    keys = pd.DataFrame({"date": pd.to_datetime(["2018-01-02", "2021-01-04"]), "symbol": "AAPL"})
    cached_annual = keys.assign(probability_up=.01)
    features = keys.assign(close_ma50=.1, close_ma200=.1, momentum_20=.1)
    monkeypatch.setattr(runner, "load_replay_inputs", lambda *args: ({}, None, None, None, {}, {"code_sha256": {}}))
    monkeypatch.setattr(runner, "load_snapshot", lambda *args: {name: None for name in ("stocks.csv", "monthly.csv", "spy.csv")})
    monkeypatch.setattr(runner.swing, "swing_features", lambda *args: features)
    monkeypatch.setattr(runner.downside, "make_frame", lambda *args: features)
    monkeypatch.setattr(runner, "load_entry", lambda *args: (cached_annual,
        {"protected_cagr": .0977, "original_drawdown": .3268}, {}))
    fits, evaluations = [], []

    def check_fit(head, start, end, frequency):
        assert frequency == "monthly"
        assert (output / "inputs.json").exists()
        if start == "2021-01-01":
            selection = json.loads((output / "selection.json").read_text())
            assert selection["selected_arm"] == mode and mode != "reject"
            assert head == "up" or mode == "dual"
        fits.append((head, start))
        return keys.loc[keys.date.between(start, end)].copy()

    def predict_up(frame, recipe, start, end, *, refit_frequency="annual"):
        assert recipe == "rf_swing20"
        return check_fit("up", start, end, refit_frequency).assign(probability_up=.7), []

    def predict_down(frame, start, end, *, refit_frequency="annual"):
        return check_fit("down", start, end, refit_frequency).assign(probability_downside=.2), []

    def forecast(frame, predictions, audits, end, *, refit_frequency="annual"):
        assert refit_frequency == "monthly"
        return {"auc": .7, "brier": .18, "reference_brier": .25}

    def orders(up, down, context):
        assert up.probability_up.eq(.7).all()  # The cached annual .01 must never drive monthly orders.
        return up.assign(action="BUY", allocation=1., reason="up_model", probability_downside=.2)[[
            "date", "symbol", "action", "allocation", "reason", "probability_up", "probability_downside"]]

    def evaluate(prices, spy, events, details, out, label, start, end, cost=5):
        assert details.date.between(start, end).all() and details.probability_up.eq(.7).all()
        if label.startswith("selection"):
            for name in ("selection-upside-predictions.csv", "selection-upside-fits.json",
                         "selection-downside-predictions.csv", "selection-fits.json",
                         "selection-control-decisions.csv", "selection-dual-decisions.csv"):
                assert (out / name).exists()
        else:
            assert json.loads((out / "selection.json").read_text())["selected_arm"] == mode
        evaluations.append(label)
        dual = "-dual" in label
        cagr = (.195 if dual else .19) if mode == "reject" else (
            .25 if (mode == "dual") == dual else .22)
        return report(cagr, .20 if dual else .30, 2 if dual else 0)

    monkeypatch.setattr(runner.swing, "walk_forward_predict", predict_up)
    monkeypatch.setattr(runner.downside, "predict", predict_down)
    monkeypatch.setattr(runner.downside, "orders", orders)
    monkeypatch.setattr(runner, "forecast_report", forecast)
    monkeypatch.setattr(runner, "evaluate_account", evaluate)
    monkeypatch.setattr(sys, "argv", ["downside_research.py", "--study-dir", str(tmp_path), "--data-dir", str(tmp_path),
        "--dividend-dir", str(tmp_path), "--entry-dir", str(tmp_path), "--out", str(output), "--monthly"])
    runner.main()
    summary = json.loads((output / "summary.json").read_text())
    inputs = json.loads((output / "inputs.json").read_text())
    assert summary["protocol"] == "monthly-dual-head-20261112-v1"
    assert summary["refit_frequency"] == inputs["refit_frequency"] == "monthly"
    assert inputs["entry_forecast_role"] == "contextual provenance only; monthly decisions use refitted upside scores"
    assert summary["selected_arm"] == (None if mode == "reject" else mode)
    assert set(fits[:2]) == {("up", "2018-01-01"), ("down", "2018-01-01")}
    assert set(fits[2:]) == ({("up", "2021-01-01"), ("down", "2021-01-01")} if mode == "dual" else (
        {("up", "2021-01-01")} if mode == "control" else set()))
    assert len(evaluations) == (2 if mode == "reject" else 6 if mode == "control" else 8)
    assert (output / "full-upside-predictions.csv").exists() == (mode != "reject")
    assert (output / "later-fits.json").exists() == (mode == "dual")
    assert summary["selection_sha256"] == runner.sha256(output / "selection.json")
    assert "Monthly ML Refit" in (output / "protocol-plan.md").read_text()
    with pytest.raises(FileExistsError):
        runner.main()
