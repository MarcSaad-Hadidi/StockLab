"""Risk calibration is opt-in and keeps raw upside forecasts and strict gates."""

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
    ["--boosted", "--market-risk-dir", "unused"], ["--monthly"]])
def test_risk_calibration_requires_annual_boosted_market_panel_before_input_reads(
        runner, tmp_path, monkeypatch, capsys, flags):
    monkeypatch.setattr(runner, "load_replay_inputs", lambda *a: pytest.fail("Invalid flags cannot read inputs"))
    monkeypatch.setattr(sys, "argv", arguments(tmp_path, *flags, "--calibrate-risk"))
    with pytest.raises(SystemExit) as error:
        runner.main()
    assert error.value.code == 2
    assert "--calibrate-risk requires --boosted, --market-risk-dir and --training-panel" in capsys.readouterr().err
    assert not (tmp_path / "out").exists()


def forecasts(runner):
    frame = pd.DataFrame({"date": pd.to_datetime(["2018-01-02", "2018-01-03", "2020-12-30"]),
        "symbol": "AAPL", runner.downside.LABEL: [0., 1., 1.],
        runner.downside.KNOWN_DATE: pd.to_datetime(["2018-02-01", "2018-02-02", "2021-01-29"])})
    predictions = frame[["date", "symbol"]].assign(probability_downside=[.1, .9, .99])
    audits = [{"first_prediction": "2018-01-02", "last_target": "2017-12-29", "training_positive_rate": .9}]
    return frame, predictions, audits


@pytest.mark.parametrize("override,expected", [(False, .41), (True, .3125)])
def test_forecast_reference_prefers_full_history_rate_and_keeps_legacy_fallback(runner, override, expected):
    frame, predictions, audits = forecasts(runner)
    if override:
        audits[0]["forecast_reference_positive_rate"] = .25
    result = runner.forecast_report(frame, predictions, audits, "2020-12-31")
    assert result["reference_brier"] == pytest.approx(expected)
    assert result["rows"] == 2 and result["brier"] == pytest.approx(.01) and result["auc"] == 1.
    frame.loc[2, runner.downside.LABEL] = 0.
    assert runner.forecast_report(frame, predictions, audits, "2020-12-31") == result


@pytest.mark.parametrize("bad", [0., 1., -.1, 1.1, np.nan, np.inf, -np.inf, "boundary", "duplicate"])
def test_new_forecast_reference_retains_finite_prevalence_and_causal_bounds(runner, bad):
    frame, predictions, audits = forecasts(runner)
    audits[0]["forecast_reference_positive_rate"] = .25
    if bad == "boundary":
        audits[0]["last_target"] = audits[0]["first_prediction"]
    elif bad == "duplicate":
        audits.append(dict(audits[0]))
    else:
        audits[0]["forecast_reference_positive_rate"] = bad
    with pytest.raises(ValueError, match="causal training prevalence"):
        runner.forecast_report(frame, predictions, audits, "2020-12-31")


@pytest.mark.parametrize("mode", ["reject", "control", "dual"])
def test_calibration_routes_only_downside_and_preserves_frozen_admissions(
        runner, tmp_path, monkeypatch, market_inputs, mode):
    evaluation, panel, panel_commitment, macro, source_commitment, attached = market_inputs
    output = tmp_path / "out"
    monkeypatch.setattr(sys, "argv", [*sys.argv, "--calibrate-risk"])
    calls, evaluations = [], []

    def predict(frame, recipe, start, end, kind, label, known, columns, score, **options):
        upside = score == "probability_up"
        assert recipe == ("boosted_market_swing20" if upside else "isotonic_boosted_market_price_downside20")
        assert kind == ("boosted" if upside else "calibrated_boosted")
        assert label == ("target_20" if upside else runner.downside.LABEL)
        assert known == ("target_date_20" if upside else runner.downside.KNOWN_DATE)
        assert columns == (runner.swing.SWING_FEATURES if upside else runner.downside.DOWNSIDE_FEATURES) + MARKET_FEATURES
        assert len(columns) == (28 if upside else 32)
        assert "observation_date" not in columns and "lookback_date" not in columns
        assert set(options) == {"training_frame", "date_weighted"} and options["date_weighted"] is True
        pd.testing.assert_frame_equal(options["training_frame"], panel.merge(macro, on="date", validate="many_to_one"))
        inputs = json.loads((output / "inputs.json").read_text())
        assert inputs["risk_calibration"] == "isotonic" and inputs["training_panel"] == panel_commitment
        assert inputs["protocol"] == "broader-boosted-isotonic-market-risk-20261112-v1"
        assert inputs["market_risk"] == source_commitment | {"features_sha256": runner.sha256(output / "market-risk-features.csv")}
        if start == "2021-01-01":
            saved = json.loads((output / "selection.json").read_text())
            assert saved["selected_arm"] == mode != "reject"
            assert saved["inputs_sha256"] == runner.sha256(output / "inputs.json")
            assert upside or mode == "dual"
        calls.append((upside, start))
        rows = frame.loc[frame.date.between(start, end), ["date", "symbol"]].copy()
        rows[score] = .7 if upside else [.2, .8]
        audits = [{"recipe": recipe, "label": label, "first_prediction": str(rows.date.min().date()),
            "last_target": str((rows.date.min() - pd.offsets.BDay(1)).date()),
            "training_positive_rate": .9, "forecast_reference_positive_rate": .25}]
        return rows, audits

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
    assert summary["risk_calibration"] == "isotonic"
    assert "Raw upside scores are uncalibrated; downside isotonic uses a shorter purged base and separate correlated holdout, without prospective reliability proof." in summary["limitations"]
    assert "raw scores are not calibrated probabilities" not in summary["limitations"]
    assert summary["protocol"] == "broader-boosted-isotonic-market-risk-20261112-v1"
    assert summary["selected_arm"] == (None if mode == "reject" else mode)
    assert summary["forecasts"]["selection"]["reference_brier"] == pytest.approx(.3125)
    assert (output / "protocol-plan.md").read_bytes() == (
        runner.WORKSPACE / "docs/superpowers/plans/2026-11-12-boosted-risk-calibration.md").read_bytes()
    assert calls[:2] == [(False, "2018-01-01"), (True, "2018-01-01")]
    assert calls[2:] == ([(True, "2021-01-01"), (False, "2021-01-01")] if mode == "dual" else
        [(True, "2021-01-01")] if mode == "control" else [])
    assert len(evaluations) == (2 if mode == "reject" else 6 if mode == "control" else 8)
    assert (output / "later-fits.json").exists() == (mode == "dual")
    assert (output / "full-upside-predictions.csv").exists() == (mode != "reject")
    assert (output / "full-downside-predictions.csv").exists() == (mode == "dual")
