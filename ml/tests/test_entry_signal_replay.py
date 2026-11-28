"""Admission and temporal boundaries for the single raw-RF entry ablation."""

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
    spec = importlib.util.spec_from_file_location("entry_runner", examples / "entry_signal_replay.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_admission_uses_exact_saved_incumbent_values_and_strict_improvements(runner):
    incumbent = {"annualized_return": .081474254321}
    original = {"max_drawdown": .32681373501027844}
    candidate = {"annualized_return": .081474254322, "max_drawdown": .3268137350102784,
                 "protective_sells": 1}
    assert runner.admit_candidate(candidate, incumbent, original)
    for changed in ({"annualized_return": incumbent["annualized_return"]},
                    {"max_drawdown": original["max_drawdown"]}, {"protective_sells": 0}):
        assert not runner.admit_candidate(dict(candidate, **changed), incumbent, original)


@pytest.mark.parametrize("admitted", [False, True])
def test_later_fits_and_returns_only_follow_persisted_admission(runner, tmp_path, monkeypatch, admitted):
    source = tmp_path / "source"
    source.mkdir()
    output = tmp_path / "out"
    parent = {"chosen_model": "rf_calibrated20", "exit_policy": "trend200"}
    commitment = {"code_sha256": {}}
    monkeypatch.setattr(runner, "load_replay_inputs", lambda *args: (parent, None, None, None, {}, commitment))
    monkeypatch.setattr(runner, "load_snapshot", lambda *args: {"stocks.csv": None, "monthly.csv": None, "spy.csv": None})
    features = pd.DataFrame({"date": pd.to_datetime(["2018-01-02", "2021-01-04"]),
                             "symbol": "AAPL", "close_ma50": 1., "close_ma200": 1., "momentum_20": 1.})
    monkeypatch.setattr(runner.swing, "swing_features", lambda *args: features)
    selection_reports = {"baseline": {"max_drawdown": .32},
                         "trailing15_spy200": {"annualized_return": .08}}
    prior = {"chosen_policy": "trailing15_spy200", "results": {
        period: {"baseline": {"max_drawdown": .32}, "selected": {"annualized_return": .07}}
        for period in ("later", "full")}}
    monkeypatch.setattr(runner, "load_prior_risk", lambda *args: (prior, {"reports": selection_reports}, {}))
    calls = []

    def predict(frame, recipe, start, end):
        assert recipe == "rf_swing20"
        assert (output / "inputs.json").exists()
        if start == "2021-01-01":
            assert admitted
            assert json.loads((output / "selection.json").read_text())["admitted"]
        calls.append(("fit", start, end))
        predictions = features.loc[features.date.between(start, end), ["date", "symbol"]].copy()
        predictions["probability_up"] = .7
        return predictions, [{"candidate": recipe, "first_prediction": start}]

    def evaluate(prices, spy, events, orders, out, label, policy, start, end, cost=5):
        assert policy == "trailing15_spy200"
        assert orders.date.between(start, end).all()
        if label != "selection-candidate":
            assert admitted
            assert json.loads((output / "selection.json").read_text())["admitted"]
        calls.append(("evaluate", start, end))
        return {"annualized_return": .21 if admitted else .01, "max_drawdown": .20,
                "protective_sells": 1, "target_met": admitted}

    monkeypatch.setattr(runner.swing, "walk_forward_predict", predict)
    monkeypatch.setattr(runner, "evaluate_replay", evaluate)
    monkeypatch.setattr(sys, "argv", ["entry_signal_replay.py", "--study-dir", str(source),
        "--data-dir", str(source), "--dividend-dir", str(source), "--risk-dir", str(source), "--out", str(output)])
    runner.main()
    summary = json.loads((output / "summary.json").read_text())
    assert summary["admitted"] is admitted
    assert summary["all_objectives_met"] is admitted
    assert summary["selection_sha256"] == runner.sha256(output / "selection.json")
    fits = [call for call in calls if call[0] == "fit"]
    assert fits == [("fit", "2018-01-01", "2020-12-31")] + (
        [("fit", "2021-01-01", "2026-11-11")] if admitted else [])
    if not admitted:
        assert len(calls) == 2
        assert not (output / "later-predictions.csv").exists()


def test_prior_risk_reports_require_original_input_and_selection_bindings(runner, tmp_path):
    core = {str(path): runner.sha256(path) for path in (Path(runner.research.__file__), Path(runner.swing.__file__))}
    commitment = {"prediction_sha256": {"predictions.csv": "frozen"}, "code_sha256": core}
    inputs = tmp_path / "inputs.json"
    inputs.write_text(json.dumps(commitment), encoding="utf-8")
    selection = tmp_path / "selection.json"
    selection.write_text(json.dumps({"chosen_policy": runner.RISK_POLICY,
        "inputs_sha256": runner.sha256(inputs)}), encoding="utf-8")
    summary = {"protocol": "protective-exits-20261112-v1", "chosen_policy": runner.RISK_POLICY,
        "inputs_sha256": runner.sha256(inputs), "selection_sha256": runner.sha256(selection)}
    (tmp_path / "summary.json").write_text(json.dumps(summary), encoding="utf-8")
    _, _, hashes = runner.load_prior_risk(tmp_path, commitment)
    assert hashes["selection.json"] == runner.sha256(selection)
    with pytest.raises(ValueError, match="changed"):
        runner.load_prior_risk(tmp_path, dict(commitment, prediction_sha256={"predictions.csv": "changed"}))
    selection.write_text(selection.read_text().replace("trailing15_spy200", "trailing15"), encoding="utf-8")
    with pytest.raises(ValueError, match="changed"):
        runner.load_prior_risk(tmp_path, commitment)


@pytest.mark.parametrize("module", ["research", "swing"])
def test_prior_risk_core_math_must_match_even_with_valid_metadata_hashes(runner, tmp_path, module):
    core = {str(path): runner.sha256(path) for path in (Path(runner.research.__file__), Path(runner.swing.__file__))}
    commitment = {"prediction_sha256": {"predictions.csv": "frozen"}, "code_sha256": core}
    old = dict(core)
    old[str(Path(getattr(runner, module).__file__))] = "previous-different-source"
    inputs = tmp_path / "inputs.json"
    inputs.write_text(json.dumps(dict(commitment, code_sha256=old)), encoding="utf-8")
    selection = tmp_path / "selection.json"
    selection.write_text(json.dumps({"chosen_policy": runner.RISK_POLICY,
        "inputs_sha256": runner.sha256(inputs)}), encoding="utf-8")
    (tmp_path / "summary.json").write_text(json.dumps({"protocol": "protective-exits-20261112-v1",
        "chosen_policy": runner.RISK_POLICY, "inputs_sha256": runner.sha256(inputs),
        "selection_sha256": runner.sha256(selection)}), encoding="utf-8")
    with pytest.raises(ValueError, match="changed"):
        runner.load_prior_risk(tmp_path, commitment)
