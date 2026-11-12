"""Fixed boosted heads share the broader panel, purges and admission flow."""

import json
import sys

import numpy as np
import pandas as pd
import pytest

from test_broader_replay import account_report, runner
from test_training_panel_fit import features, uneven_panel


def arguments(root, *flags):
    return ["downside_research.py", "--study-dir", str(root), "--data-dir", str(root),
        "--dividend-dir", str(root), "--entry-dir", str(root), "--out", str(root / "out"), *flags]


@pytest.mark.parametrize("flags", [["--boosted"], ["--monthly", "--boosted"],
    ["--monthly", "--training-panel", "unused", "--boosted"]])
def test_boosted_requires_annual_training_panel_before_input_reads(runner, tmp_path, monkeypatch, capsys, flags):
    monkeypatch.setattr(runner, "load_replay_inputs", lambda *a: pytest.fail("Invalid flags must not read inputs"))
    monkeypatch.setattr(sys, "argv", arguments(tmp_path, *flags))
    with pytest.raises(SystemExit) as error:
        runner.main()
    assert error.value.code == 2
    message = capsys.readouterr().err
    assert ("requires --training-panel" if "--training-panel" not in flags else "not allowed") in message
    assert not (tmp_path / "out").exists()


@pytest.mark.parametrize("boosted", [False, True], ids=["unchanged-rf", "boosted"])
@pytest.mark.parametrize("mode", ["reject", "control", "dual"])
def test_both_heads_keep_weighted_purges_and_only_admitted_later_work(
        runner, tmp_path, monkeypatch, boosted, mode):
    output = tmp_path / "out"
    evaluation = features([(day, "AAPL") for day in ("2018-01-02", "2018-01-03", "2021-01-04", "2021-01-05")])
    evaluation[["close_ma50", "close_ma200", "momentum_20"]] = .1
    panel = uneven_panel()
    panel["date"] = panel.date - pd.DateOffset(years=6)
    for known in ("target_date_20", runner.downside.KNOWN_DATE):
        panel[known] = panel[known] - pd.DateOffset(years=6)
    excluded = features([("2013-12-31", "EXPIRED"), ("2017-12-04", "EDGE"),
        ("2017-12-05", "FUTURE"), ("2017-12-06", "CENSORED"), ("2017-12-07", "UNKNOWN")])
    for label, known, edge in (("target_20", "target_date_20", "2018-01-02"),
                              (runner.downside.LABEL, runner.downside.KNOWN_DATE, "2018-01-01")):
        evaluation[label] = [0., 1., 0., 1.]
        evaluation[known] = evaluation.date + pd.offsets.BDay(21 if label == "target_20" else 20)
        excluded[label] = [1., 0., 1., np.nan, np.nan]
        excluded[known] = pd.to_datetime(["2014-01-31", edge, "2018-01-03", None, "2017-12-29"])
    panel = pd.concat([panel, excluded], ignore_index=True).sort_values(["date", "symbol"]).reset_index(drop=True)
    panel_commitment = {"manifest_sha256": "panel", "training_sha256": "training", "role": "training_only"}
    monkeypatch.setenv("OMP_NUM_THREADS", "1")
    monkeypatch.setattr(runner, "load_replay_inputs", lambda *a: ({}, None, None, None, {}, {
        "code_sha256": {}, "price_inputs_sha256": {"spy.csv": "spy"}}))
    monkeypatch.setattr(runner, "load_snapshot", lambda *a: {k: None for k in ("stocks.csv", "monthly.csv", "spy.csv")})
    monkeypatch.setattr(runner.swing, "swing_features", lambda *a: evaluation)
    monkeypatch.setattr(runner.downside, "make_frame", lambda *a: evaluation)
    monkeypatch.setattr(runner, "load_entry", lambda *a: (evaluation[["date", "symbol"]].assign(probability_up=.01),
        {"protected_cagr": .0977, "original_drawdown": .3268}, {}))

    def load_panel(path, frame, spy, digest):
        assert path == tmp_path / "panel" and frame is evaluation and digest == "spy"
        assert not output.exists()
        return panel, panel_commitment

    monkeypatch.setattr(runner, "load_training_panel", load_panel)
    fits, evaluations = [], []

    def classifier(kind):
        class Classifier:
            classes_ = np.array([0, 1])

            def __init__(self, **parameters):
                assert kind == ("boosted" if boosted else "rf")
                if boosted:
                    assert parameters == {"max_iter": 200, "learning_rate": .05, "max_leaf_nodes": 15,
                        "l2_regularization": 1., "early_stopping": False, "random_state": 42}

            def fit(self, x, y, *, sample_weight):
                self.head = "upside" if list(x) == runner.swing.SWING_FEATURES else "downside"
                assert list(x) == (runner.swing.SWING_FEATURES if self.head == "upside" else runner.downside.DOWNSIDE_FEATURES)
                assert y.name == ("target_20" if self.head == "upside" else runner.downside.LABEL)
                inputs = json.loads((output / "inputs.json").read_text())
                assert inputs["training_panel"] == panel_commitment
                assert inputs["refit_frequency"] == "annual" and inputs["date_weighted"] is True
                assert inputs.get("boosted", False) == boosted
                assert inputs.get("omp_num_threads") == ("1" if boosted else None)
                assert inputs["plan_sha256"] == runner.sha256(output / "protocol-plan.md")
                for source, digest in inputs["code_sha256"].items():
                    archived = output / "source-snapshot" / runner.Path(source).relative_to(runner.WORKSPACE)
                    assert runner.sha256(archived) == digest == runner.sha256(runner.Path(source))
                if len(fits) < 2:
                    symbols = ["AAA", "AAA", "BBB", "AAA", "BBB", "CCC"]
                    weights = [2., 1., 1., 2 / 3, 2 / 3, 2 / 3]
                    if self.head == "downside":
                        symbols += ["EDGE"]
                        weights = [7 / 4, 7 / 8, 7 / 8, 7 / 12, 7 / 12, 7 / 12, 7 / 4]
                    assert panel.loc[x.index, "symbol"].tolist() == symbols
                    np.testing.assert_allclose(sample_weight, weights, rtol=0, atol=1e-15)
                else:
                    saved = json.loads((output / "selection.json").read_text())
                    assert saved["selected_arm"] == mode != "reject"
                    assert self.head == "upside" or mode == "dual"
                fits.append(self.head)
                return self

            def predict_proba(self, x):
                scores = np.full(len(x), .7) if self.head == "upside" else np.resize([.2, .8], len(x))
                return np.column_stack([1 - scores, scores])

        return Classifier

    monkeypatch.setattr(runner.swing, "HistGradientBoostingClassifier", classifier("boosted"))
    monkeypatch.setattr(runner.swing, "RandomForestClassifier", classifier("rf"))

    def evaluate(prices, spy, events, details, out, label, start, end, cost=5):
        assert details.probability_up.eq(.7).all() and details.date.between(start, end).all()
        if not label.startswith("selection"):
            assert json.loads((out / "selection.json").read_text())["selected_arm"] == mode
        evaluations.append((label, cost))
        dual = "-dual" in label
        cagr = (.195 if dual else .19) if mode == "reject" else (.25 if (mode == "dual") == dual else .22)
        return account_report(cagr, .20 if dual else .30, 2 if dual else 0)

    monkeypatch.setattr(runner, "evaluate_account", evaluate)
    flags = ["--training-panel", str(tmp_path / "panel")] + (["--boosted"] if boosted else [])
    monkeypatch.setattr(sys, "argv", arguments(tmp_path, *flags))
    runner.main()
    summary = json.loads((output / "summary.json").read_text())
    inputs = json.loads((output / "inputs.json").read_text())
    protocol = "broader-boosted-model-20261112-v1" if boosted else "broader-training-model-20261112-v1"
    assert summary["protocol"] == inputs["protocol"] == protocol
    assert summary.get("boosted", False) == boosted
    assert set(summary) == {"protocol", "selected_arm", "admissions", "selection_sha256", "inputs_sha256", "results",
        "forecasts", "all_objectives_met", "costs_bps_per_side", "higher_costs_bps_per_side", "limitations",
        "refit_frequency", "date_weighted", "training_panel"} | ({"boosted"} if boosted else set())
    if not boosted:
        assert "boosted" not in inputs and "omp_num_threads" not in inputs
    plan = "2026-11-12-broader-boosted.md" if boosted else "2026-11-12-broader-training.md"
    assert (output / "protocol-plan.md").read_bytes() == (runner.WORKSPACE / "docs/superpowers/plans" / plan).read_bytes()
    assert summary["selected_arm"] == (None if mode == "reject" else mode)
    assert fits[:2] == ["downside", "upside"]
    assert fits[2:] == (["upside", "downside"] if mode == "dual" else ["upside"] if mode == "control" else [])
    assert len(evaluations) == (2 if mode == "reject" else 6 if mode == "control" else 8)
    assert sum(cost == 25 for _, cost in evaluations) == (0 if mode == "reject" else 2)
    assert (output / "later-fits.json").exists() == (mode == "dual")
    assert (output / "full-upside-predictions.csv").exists() == (mode != "reject")
    assert (output / "full-downside-predictions.csv").exists() == (mode == "dual")
    assert summary["selection_sha256"] == runner.sha256(output / "selection.json")
    for head, name, recipe, rate in (("upside", "selection-upside-fits.json", "swing20", 4 / 9),
            ("downside", "selection-fits.json", "price_downside20", 1 / 3)):
        audit, = json.loads((output / name).read_text())
        assert audit["recipe"] == ("boosted_" if boosted else "rf_") + recipe
        assert audit["training_positive_rate"] == pytest.approx(rate)
        assert audit["last_target"] == ("2017-12-05" if head == "upside" else "2018-01-01")
        assert audit["first_fit"] == "2017-11-01"
        assert audit["training_dates"] == (3 if head == "upside" else 4)
        assert audit["training_rows"] == (6 if head == "upside" else 7)


@pytest.mark.parametrize("defect", ["below_target", "below_spy", "original_dd", "control_dd", "control_cagr",
    "no_sales", "no_downside_sales", "auc", "brier"])
def test_boosted_dual_retains_every_strict_admission_gate(runner, defect):
    control, dual = account_report(.22, .30), account_report(.25, .20, 2)
    forecast = {"auc": .7, "brier": .18, "reference_brier": .25}
    if defect == "below_target":
        control["annualized_return"], dual["annualized_return"] = .18, .19
    elif defect == "below_spy":
        dual["benchmark_annualized_return"] = .25
    elif defect == "original_dd":
        control["max_drawdown"], dual["max_drawdown"] = .4, .3268
    elif defect == "control_dd":
        dual["max_drawdown"] = .30
    elif defect == "control_cagr":
        dual["annualized_return"] = .21
    elif defect == "no_sales":
        dual["sells"] = 0
    elif defect == "no_downside_sales":
        dual["downside_sells"] = 0
    elif defect == "auc":
        forecast["auc"] = .5
    else:
        forecast["brier"] = .25
    assert not runner.admissions(control, dual, forecast,
        {"protected_cagr": .0977, "original_drawdown": .3268}, strict_target=True)["dual"]
