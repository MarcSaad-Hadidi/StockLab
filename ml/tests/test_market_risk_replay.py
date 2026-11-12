"""Opt-in market features retain the boosted annual purges and admission flow."""

import json
import sys
from types import SimpleNamespace

import numpy as np
import pandas as pd
import pytest

from test_boosted_replay import arguments
from test_broader_replay import account_report, runner
from test_training_panel_fit import features, uneven_panel


MARKET_FEATURES = ["vix_level", "vix_change5", "vix_term_ratio", "vix_term_change5"]


@pytest.mark.parametrize("flags", [[], ["--training-panel", "unused"], ["--boosted"],
    ["--monthly"], ["--monthly", "--boosted"]])
def test_market_risk_requires_both_boosted_and_annual_panel_before_input_reads(
        runner, tmp_path, monkeypatch, capsys, flags):
    monkeypatch.setattr(runner, "load_replay_inputs", lambda *a: pytest.fail("Invalid flags must not read inputs"))
    monkeypatch.setattr(sys, "argv", arguments(tmp_path, *flags, "--market-risk-dir", "unused"))
    with pytest.raises(SystemExit) as error:
        runner.main()
    assert error.value.code == 2
    assert "--market-risk-dir requires --boosted and --training-panel" in capsys.readouterr().err
    assert not (tmp_path / "out").exists()


@pytest.fixture
def market_inputs(runner, tmp_path, monkeypatch):
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
    source_commitment = {"source_sha256": {"VIX": "vix", "VIX3M": "vix3m"}, "spy_sha256": "spy"}
    spy = pd.DataFrame({"date": pd.bdate_range("2013-01-01", "2026-11-11"), "symbol": "SPY"})
    macro = pd.DataFrame({"date": pd.concat([evaluation.date, panel.date]).drop_duplicates().sort_values()})
    macro = macro.reset_index(drop=True)
    for index, column in enumerate(MARKET_FEATURES, 1):
        macro[column] = index / 100
    macro["observation_date"] = macro.date - pd.offsets.BDay(2)
    macro["lookback_date"] = macro.date - pd.offsets.BDay(7)
    attached = []

    def load_market(path, calendar, digest):
        assert path == tmp_path / "market" and calendar is spy and digest == "spy"
        assert not output.exists()
        return macro, source_commitment.copy()

    def attach(frame, market_frame):
        assert market_frame is macro and not output.exists()
        attached.append(frame)
        return frame.merge(macro, on="date", how="left", validate="many_to_one")

    market = SimpleNamespace(FEATURES=MARKET_FEATURES, load_market_risk=load_market, attach=attach,
        __file__=str(runner.WORKSPACE / "ml/src/stocklab_ml/market_risk_research.py"))
    monkeypatch.setattr(runner, "market", market, raising=False)
    monkeypatch.setenv("OMP_NUM_THREADS", "1")
    monkeypatch.setattr(runner, "load_replay_inputs", lambda *a: ({}, None, None, None, {}, {
        "code_sha256": {}, "price_inputs_sha256": {"spy.csv": "spy"}}))
    monkeypatch.setattr(runner, "load_snapshot", lambda *a: {"stocks.csv": None, "monthly.csv": None, "spy.csv": spy})
    monkeypatch.setattr(runner.swing, "swing_features", lambda *a: evaluation)
    monkeypatch.setattr(runner.downside, "make_frame", lambda *a: evaluation)
    monkeypatch.setattr(runner, "load_entry", lambda *a: (evaluation[["date", "symbol"]].assign(probability_up=.01),
        {"protected_cagr": .0977, "original_drawdown": .3268}, {}))

    def load_panel(path, frame, calendar, digest):
        assert path == tmp_path / "panel" and frame is evaluation and calendar is spy and digest == "spy"
        assert not output.exists()
        return panel, panel_commitment

    monkeypatch.setattr(runner, "load_training_panel", load_panel)
    monkeypatch.setattr(sys, "argv", arguments(tmp_path, "--training-panel", str(tmp_path / "panel"),
        "--boosted", "--market-risk-dir", str(tmp_path / "market")))
    return evaluation, panel, panel_commitment, macro, source_commitment, attached


@pytest.mark.parametrize("defect", ["source", "attach"])
def test_market_risk_source_or_attachment_failure_precedes_output_creation(
        runner, tmp_path, monkeypatch, market_inputs, defect):
    def fail(*args):
        raise ValueError("Rejected market source" if defect == "source" else "Missing required feature date")

    monkeypatch.setattr(runner.market, "load_market_risk" if defect == "source" else "attach", fail)
    monkeypatch.setattr(runner.swing, "_walk_forward_predict", lambda *a, **kw: pytest.fail("Invalid market inputs cannot fit"))
    with pytest.raises(ValueError, match="Rejected market|Missing required"):
        runner.main()
    assert not (tmp_path / "out").exists()


@pytest.mark.parametrize("enabled", [False, True], ids=["unchanged-boosted", "market-boosted"])
@pytest.mark.parametrize("mode", ["reject", "control", "dual"])
def test_market_heads_keep_feature_sets_purges_provenance_and_frozen_admissions(
        runner, tmp_path, monkeypatch, market_inputs, enabled, mode):
    evaluation, panel, panel_commitment, macro, source_commitment, attached = market_inputs
    output = tmp_path / "out"
    if not enabled:
        monkeypatch.setattr(sys, "argv", arguments(tmp_path, "--training-panel", str(tmp_path / "panel"), "--boosted"))
        monkeypatch.setattr(runner.market, "load_market_risk", lambda *a: pytest.fail("Default boosted cannot read market sources"))
    fits, evaluations = [], []

    class Classifier:
        classes_ = np.array([0, 1])

        def __init__(self, **parameters):
            assert parameters == {"max_iter": 200, "learning_rate": .05, "max_leaf_nodes": 15,
                "l2_regularization": 1., "early_stopping": False, "random_state": 42}

        def fit(self, x, y, *, sample_weight):
            self.head = "upside" if y.name == "target_20" else "downside"
            base = runner.swing.SWING_FEATURES if self.head == "upside" else runner.downside.DOWNSIDE_FEATURES
            assert list(x) == base + (MARKET_FEATURES if enabled else [])
            assert len(x.columns) == (28 if self.head == "upside" else 32) - (0 if enabled else 4)
            assert "observation_date" not in x and "lookback_date" not in x
            inputs = json.loads((output / "inputs.json").read_text())
            assert inputs["training_panel"] == panel_commitment
            assert "risk_calibration" not in inputs
            assert inputs["refit_frequency"] == "annual" and inputs["date_weighted"] is True
            assert inputs["boosted"] is True and inputs["omp_num_threads"] == "1"
            assert inputs["plan_sha256"] == runner.sha256(output / "protocol-plan.md")
            if enabled:
                dates = ["date", "observation_date", "lookback_date"]
                saved = pd.read_csv(output / "market-risk-features.csv", parse_dates=dates).astype(
                    {name: "datetime64[ns]" for name in dates})
                pd.testing.assert_frame_equal(saved, macro)
                assert inputs["market_risk"] == source_commitment | {
                    "features_sha256": runner.sha256(output / "market-risk-features.csv")}
                assert len(attached) == 3 and attached[0] is evaluation and attached[1] is evaluation and attached[2] is panel
            else:
                assert "market_risk" not in inputs and not attached
                assert not (output / "market-risk-features.csv").exists()
            for source, digest in inputs["code_sha256"].items():
                archived = output / "source-snapshot" / runner.Path(source).relative_to(runner.WORKSPACE)
                assert runner.sha256(archived) == digest == runner.sha256(runner.Path(source))
            market_source = str(runner.Path(runner.market.__file__))
            assert (market_source in inputs["code_sha256"]) == enabled
            if len(fits) < 2:
                symbols = ["AAA", "AAA", "BBB", "AAA", "BBB", "CCC"]
                weights = [2., 1., 1., 2 / 3, 2 / 3, 2 / 3]
                if self.head == "downside":
                    symbols += ["EDGE"]
                    weights = [7 / 4, 7 / 8, 7 / 8, 7 / 12, 7 / 12, 7 / 12, 7 / 4]
                assert panel.loc[x.index, "symbol"].tolist() == symbols
                np.testing.assert_allclose(sample_weight, weights, rtol=0, atol=1e-15)
            else:
                selection = json.loads((output / "selection.json").read_text())
                assert selection["selected_arm"] == mode != "reject"
                assert selection["inputs_sha256"] == runner.sha256(output / "inputs.json")
                assert self.head == "upside" or mode == "dual"
            fits.append(self.head)
            return self

        def predict_proba(self, x):
            scores = np.full(len(x), .7) if self.head == "upside" else np.resize([.2, .8], len(x))
            return np.column_stack([1 - scores, scores])

    monkeypatch.setattr(runner.swing, "HistGradientBoostingClassifier", Classifier)
    original_forecast = runner.forecast_report

    def forecast(frame, predictions, audits, end, **options):
        assert options == {}
        return original_forecast(frame, predictions, audits, end)

    monkeypatch.setattr(runner, "forecast_report", forecast)

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
    inputs = json.loads((output / "inputs.json").read_text())
    protocol = "broader-boosted-market-risk-20261112-v1" if enabled else "broader-boosted-model-20261112-v1"
    assert summary["protocol"] == inputs["protocol"] == protocol
    assert summary["boosted"] is True
    assert set(summary) == {"protocol", "selected_arm", "admissions", "selection_sha256", "inputs_sha256", "results",
        "forecasts", "all_objectives_met", "costs_bps_per_side", "higher_costs_bps_per_side", "limitations",
        "refit_frequency", "date_weighted", "training_panel", "boosted"} | ({"market_risk"} if enabled else set())
    if enabled:
        assert summary["market_risk"] == inputs["market_risk"]
        assert "the lag does not certify point-in-time availability" in summary["limitations"]
    else:
        assert "Market indices" not in summary["limitations"]
    plan = "2026-11-12-boosted-market-risk.md" if enabled else "2026-11-12-broader-boosted.md"
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
        assert audit["recipe"] == ("boosted_market_" if enabled else "boosted_") + recipe
        assert audit["training_positive_rate"] == pytest.approx(rate)
        assert audit["last_target"] == ("2017-12-05" if head == "upside" else "2018-01-01")
        assert audit["first_fit"] == "2017-11-01"
        assert audit["training_dates"] == (3 if head == "upside" else 4)
        assert audit["training_rows"] == (6 if head == "upside" else 7)
