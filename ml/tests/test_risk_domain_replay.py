"""Original-stock risk fitting retains broader causal forecast references."""

import json
import sys

import numpy as np
import pandas as pd
import pytest

from test_boosted_replay import arguments
from test_broader_replay import account_report, runner
from test_market_risk_replay import MARKET_FEATURES, market_inputs


@pytest.mark.parametrize("flags", [[], ["--boosted", "--training-panel", "unused"],
    ["--market-risk-dir", "unused", "--training-panel", "unused"],
    ["--boosted", "--market-risk-dir", "unused"], ["--monthly"],
    ["--boosted", "--market-risk-dir", "unused", "--training-panel", "unused", "--calibrate-risk"]])
def test_original_risk_history_requires_raw_annual_boosted_market_panel_before_input_reads(
        runner, tmp_path, monkeypatch, capsys, flags):
    monkeypatch.setattr(runner, "load_replay_inputs", lambda *a: pytest.fail("Invalid flags cannot read inputs"))
    monkeypatch.setattr(sys, "argv", arguments(tmp_path, *flags, "--original-risk-history"))
    with pytest.raises(SystemExit) as error:
        runner.main()
    assert error.value.code == 2
    message = capsys.readouterr().err
    assert "--original-risk-history requires --boosted, --market-risk-dir and --training-panel" in message
    assert "--calibrate-risk" in message
    assert not (tmp_path / "out").exists()


@pytest.fixture
def original_inputs(runner, monkeypatch, market_inputs):
    evaluation, panel, *_ = market_inputs
    for symbol in sorted(set(runner.SYMBOLS) - {"AAPL"}):
        for _, row in evaluation.iloc[:4].iterrows():
            evaluation.loc[len(evaluation)] = row
            evaluation.loc[len(evaluation) - 1, "symbol"] = symbol
    boundary = panel.loc[panel.symbol.eq("FUTURE")].iloc[0].copy()
    boundary["symbol"] = "BOUNDARY"
    boundary[runner.downside.KNOWN_DATE] = pd.Timestamp("2018-01-02")
    panel.loc[len(panel)] = boundary
    monkeypatch.setattr(sys, "argv", [*sys.argv, "--original-risk-history"])
    return market_inputs


def fake_predictions(runner, frame, recipe, start, end, label, score):
    rows = frame.loc[frame.date.between(start, end), ["date", "symbol"]].copy()
    rows[score] = .7 if score == "probability_up" else np.resize([.2, .8], len(rows))
    audit = {"recipe": recipe, "label": label, "first_prediction": str(rows.date.min().date()),
        "last_target": str((rows.date.min() - pd.offsets.BDay(1)).date()),
        "training_positive_rate": .8, "training_rows": 2, "training_dates": 2, "training_identities": 1}
    return rows, [audit]


@pytest.mark.parametrize("mode", ["reject", "control", "dual"])
def test_domain_route_keeps_raw_upside_and_broader_reference_with_frozen_admissions(
        runner, tmp_path, monkeypatch, original_inputs, mode):
    evaluation, panel, panel_commitment, macro, source_commitment, attached = original_inputs
    output = tmp_path / "out"
    calls, evaluations = [], []

    def predict(frame, recipe, start, end, kind, label, known, columns, score, **options):
        upside = score == "probability_up"
        assert recipe == ("boosted_market_swing20" if upside else "boosted_original_market_price_downside20")
        assert kind == "boosted"
        assert label == ("target_20" if upside else runner.downside.LABEL)
        assert known == ("target_date_20" if upside else runner.downside.KNOWN_DATE)
        assert columns == (runner.swing.SWING_FEATURES if upside else runner.downside.DOWNSIDE_FEATURES) + MARKET_FEATURES
        assert len(columns) == (28 if upside else 32)
        assert set(options) == {"training_frame", "date_weighted"} and options["date_weighted"] is True
        if upside:
            pd.testing.assert_frame_equal(options["training_frame"], panel.merge(macro, on="date", validate="many_to_one"))
        else:
            assert options["training_frame"] is frame and set(frame.symbol) == {"AAPL", "JNJ", "JPM", "MSFT", "XOM"}
            pd.testing.assert_frame_equal(frame, evaluation.merge(macro, on="date", validate="many_to_one"))
        inputs = json.loads((output / "inputs.json").read_text())
        assert inputs["risk_training_population"] == "original_five" and inputs["training_panel"] == panel_commitment
        assert "risk_calibration" not in inputs
        assert inputs["protocol"] == "boosted-original-risk-market-20261112-v1"
        assert inputs["market_risk"] == source_commitment | {"features_sha256": runner.sha256(output / "market-risk-features.csv")}
        if start == "2021-01-01":
            saved = json.loads((output / "selection.json").read_text())
            assert saved["selected_arm"] == mode != "reject"
            assert saved["inputs_sha256"] == runner.sha256(output / "inputs.json")
            assert upside or mode == "dual"
        calls.append((upside, start))
        return fake_predictions(runner, frame, recipe, start, end, label, score)

    monkeypatch.setattr(runner.swing, "_walk_forward_predict", predict)

    def evaluate(prices, spy, events, details, out, label, start, end, cost=5):
        assert details.probability_up.eq(.7).all() and details.date.between(start, end).all()
        if not label.startswith("selection"):
            assert json.loads((out / "selection.json").read_text())["selected_arm"] == mode
        evaluations.append((label, cost))
        dual = "-dual" in label
        cagr = (.195 if dual else .19) if mode == "reject" else (.25 if (mode == "dual") == dual else .22)
        return account_report(cagr, .20 if dual else .30, 2 if dual else 0)

    monkeypatch.setattr(runner, "evaluate_account", evaluate)
    runner.main()
    summary = json.loads((output / "summary.json").read_text())
    assert summary["risk_training_population"] == "original_five" and "risk_calibration" not in summary
    assert summary["protocol"] == "boosted-original-risk-market-20261112-v1"
    assert summary["selected_arm"] == (None if mode == "reject" else mode)
    assert summary["forecasts"]["selection"]["reference_brier"] == pytest.approx(5 / 18)
    assert (output / "protocol-plan.md").read_bytes() == (
        runner.WORKSPACE / "docs/superpowers/plans/2026-11-12-boosted-risk-domain.md").read_bytes()
    audit, = json.loads((output / "selection-fits.json").read_text())
    assert {key: value for key, value in audit.items() if key.startswith("forecast_reference_")} == {
        "forecast_reference_positive_rate": pytest.approx(1 / 3),
        "forecast_reference_unweighted_positive_rate": pytest.approx(3 / 7),
        "forecast_reference_first_fit": "2017-11-01", "forecast_reference_last_fit": "2017-12-04",
        "forecast_reference_last_target": "2018-01-01", "forecast_reference_rows": 7,
        "forecast_reference_dates": 4, "forecast_reference_identities": 4, "forecast_reference_date_weighted": True}
    assert audit["training_positive_rate"] == .8
    assert (audit["training_rows"], audit["training_dates"], audit["training_identities"]) == (2, 2, 1)
    up_audit, = json.loads((output / "selection-upside-fits.json").read_text())
    assert not any(key.startswith("forecast_reference_") for key in up_audit)
    assert calls[:2] == [(False, "2018-01-01"), (True, "2018-01-01")]
    assert calls[2:] == ([(True, "2021-01-01"), (False, "2021-01-01")] if mode == "dual" else
        [(True, "2021-01-01")] if mode == "control" else [])
    assert len(evaluations) == (2 if mode == "reject" else 6 if mode == "control" else 8)
    assert (output / "later-fits.json").exists() == (mode == "dual")
    assert (output / "full-upside-predictions.csv").exists() == (mode != "reject")
    assert (output / "full-downside-predictions.csv").exists() == (mode == "dual")


@pytest.mark.parametrize("defect", ["single_class", "empty", "nonbinary"])
def test_invalid_broader_reference_cannot_produce_risk_audit_or_later_work(
        runner, tmp_path, monkeypatch, original_inputs, defect):
    _, panel, *_ = original_inputs
    if defect == "empty":
        panel["date"] = pd.Timestamp("2010-01-04")
    elif defect == "single_class":
        panel[runner.downside.LABEL] = 0.
    else:
        panel.loc[panel.symbol.eq("AAA"), runner.downside.LABEL] = 2.
    calls = []

    def predict(frame, recipe, start, end, kind, label, known, columns, score, **options):
        calls.append((recipe, start))
        return fake_predictions(runner, frame, recipe, start, end, label, score)

    monkeypatch.setattr(runner.swing, "_walk_forward_predict", predict)
    monkeypatch.setattr(runner, "evaluate_account", lambda *a, **kw: pytest.fail("Invalid reference cannot evaluate"))
    with pytest.raises(ValueError, match="broader.*reference|reference.*both classes"):
        runner.main()
    assert not (tmp_path / "out/selection-fits.json").exists()
    assert not (tmp_path / "out/selection.json").exists()
    assert all(start == "2018-01-01" for _, start in calls)
