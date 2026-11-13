"""Frozen selection and evidence binding for the protective-exit experiment."""

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
    spec = importlib.util.spec_from_file_location("risk_runner", examples / "risk_exit_replay.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_selection_requires_lower_drawdown_and_protective_sales(runner):
    baseline = {"annualized_return": .19, "max_drawdown": .32, "protective_sells": 0}
    trailing = {"annualized_return": .18, "max_drawdown": .20, "protective_sells": 5}
    regime = {"annualized_return": .22, "max_drawdown": .24, "protective_sells": 8}
    reports = {"baseline": baseline, "trailing15": trailing, "trailing15_spy200": regime}
    assert runner.choose_policy(reports) == "trailing15_spy200"
    regime["max_drawdown"] = .32
    assert runner.choose_policy(reports) == "trailing15"
    trailing["protective_sells"] = 0
    assert runner.choose_policy(reports) is None


def test_selection_ties_follow_frozen_recipe_order(runner):
    reports = {"baseline": {"annualized_return": .19, "max_drawdown": .32, "protective_sells": 0}}
    for policy in reversed(runner.POLICIES):
        reports[policy] = {"annualized_return": .18, "max_drawdown": .20, "protective_sells": 1}
    assert runner.choose_policy(reports) == "trailing15"


def test_changed_predictions_or_replay_manifest_are_rejected(runner, tmp_path):
    commitment = {"parent_summary_sha256": "study", "parent_selection_sha256": "selection",
        "prediction_sha256": {"test-rf_calibrated20-predictions.csv": "prediction"},
        "price_inputs_sha256": {"stocks.csv": "prices"}, "dividends_sha256": "events",
        "dividend_manifest_sha256": "event-metadata", "code_sha256": {"old": "code"}}
    path = tmp_path / "inputs.json"
    path.write_text(json.dumps(commitment), encoding="utf-8")
    summary = {"protocol": "frozen-cash-dividend-replay-v1", "inputs_sha256": runner.sha256(path)}
    (tmp_path / "summary.json").write_text(json.dumps(summary), encoding="utf-8")
    runner.validate_frozen_replay(tmp_path, commitment)
    modified = dict(commitment, prediction_sha256={"test-rf_calibrated20-predictions.csv": "changed"})
    with pytest.raises(ValueError, match="changed"):
        runner.validate_frozen_replay(tmp_path, modified)
    path.write_text(path.read_text().replace("events", "changed"), encoding="utf-8")
    with pytest.raises(ValueError, match="changed"):
        runner.validate_frozen_replay(tmp_path, commitment)


def test_shared_loader_rejects_price_changed_since_selection(runner, tmp_path):
    study, data = tmp_path / "study", tmp_path / "data"
    study.mkdir()
    data.mkdir()
    selection = study / "selection.json"
    selection.write_text("{}", encoding="utf-8")
    names = ("stocks.csv", "monthly.csv", "spy.csv", "manifest.json")
    for name in names:
        (data / name).write_text("original", encoding="utf-8")
    parent = {"protocol": "long-horizon-20261111-v4", "chosen_model": "rf_calibrated20",
        "exit_policy": "trend200", "selection_sha256": runner.sha256(selection),
        "inputs_sha256": {name: runner.sha256(data / name) for name in names}}
    (study / "summary.json").write_text(json.dumps(parent), encoding="utf-8")
    (data / "stocks.csv").write_text("changed", encoding="utf-8")
    with pytest.raises(ValueError, match="changed"):
        runner.load_replay_inputs(study, data, tmp_path / "dividends")


def test_selection_is_saved_before_later_returns_and_only_selected_policy_is_tested(runner, tmp_path, monkeypatch):
    source = tmp_path / "source"
    source.mkdir()
    for name in ("summary.json", "inputs.json"):
        (source / name).write_text("{}", encoding="utf-8")
    output = tmp_path / "out"
    orders = pd.DataFrame({"date": pd.to_datetime(["2018-01-02", "2021-01-04"]),
                          "symbol": "AAPL", "action": "BUY", "allocation": 1.})
    parent = {"chosen_model": "rf_calibrated20", "exit_policy": "trend200"}
    monkeypatch.setattr(runner, "load_replay_inputs", lambda *args: (
        parent, None, None, None, {"rf_calibrated20": orders}, {"code_sha256": {}}))
    monkeypatch.setattr(runner, "validate_frozen_replay", lambda *args: None)
    calls = []

    def simulate(prices, decisions, start, end, **kwargs):
        assert decisions.date.between(start, end).all()
        assert (output / "inputs.json").exists()
        policy = kwargs["risk_policy"]
        if start == "2021-01-01" or end == "2026-11-11":
            selection = json.loads((output / "selection.json").read_text())
            assert selection["chosen_policy"] == "trailing15"
            assert policy in (None, "trailing15")
        calls.append((start, end, policy))
        curve = pd.DataFrame({"date": pd.to_datetime([start, end]), "cash": [100000., 1.],
            "total_value": [100000., 150000.], "dividends_earned": 0., "dividends_paid": 0.,
            "dividends_receivable": 0.})
        curve.attrs["policy"] = policy
        trades = pd.DataFrame({"action": ["SELL"], "fee": [1.]})
        if policy:
            trades["reason"] = "trailing_stop"
        return curve, trades

    def assess(curve, benchmark):
        policy = curve.attrs["policy"]
        return {"annualized_return": .22 if policy == "trailing15" else .20,
                "max_drawdown": .20 if policy else .32, "target_met": True}

    monkeypatch.setattr(runner.research, "simulate", simulate)
    monkeypatch.setattr(runner.research, "assess", assess)
    monkeypatch.setattr(runner.research, "spy_curve", lambda *args, **kwargs: pd.DataFrame())
    monkeypatch.setattr(runner.swing, "calendar_returns", lambda curve: [])
    monkeypatch.setattr(sys, "argv", ["risk_exit_replay.py", "--study-dir", str(source),
        "--data-dir", str(source), "--dividend-dir", str(source), "--replay-dir", str(source), "--out", str(output)])
    runner.main()
    assert calls[:3] == [("2018-01-01", "2020-12-31", policy) for policy in (None, *runner.POLICIES)]
    summary = json.loads((output / "summary.json").read_text())
    assert summary["selection_sha256"] == runner.sha256(output / "selection.json")
    assert summary["all_objectives_met"] is True
    with pytest.raises(FileExistsError):
        runner.main()
