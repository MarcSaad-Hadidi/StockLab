"""Frozen dual-model admission, causal diagnostics and execution provenance."""

import hashlib
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
    spec = importlib.util.spec_from_file_location("downside_runner", examples / "downside_research.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def report(cagr=.25, dd=.20, down=2):
    return {"annualized_return": cagr, "max_drawdown": dd, "sells": 3,
            "downside_sells": down, "benchmark_annualized_return": .14, "target_met": cagr >= .20}


def test_admission_requires_incremental_return_risk_forecast_and_executed_sales(runner):
    control, dual = report(.15, .30, 0), report()
    forecast = {"auc": .7, "brier": .18, "reference_brier": .25}
    thresholds = {"protected_cagr": .09772968, "original_drawdown": .32681374}
    assert runner.admissions(control, dual, forecast, thresholds) == {"control": False, "dual": True}
    for changed in ({"annualized_return": .149}, {"max_drawdown": .30}, {"downside_sells": 0}):
        assert not runner.admissions(control, dict(dual, **changed), forecast, thresholds)["dual"]
    for changed in ({"auc": .5}, {"auc": None}, {"brier": .25}):
        assert not runner.admissions(control, dual, dict(forecast, **changed), thresholds)["dual"]
    assert runner.admissions(report(.20, .30, 0), report(.01), forecast, thresholds)["control"]
    assert not runner.admissions(report(.20, .33, 0), dual, forecast, thresholds)["control"]


def test_forecast_brier_uses_causal_annual_rate_and_never_labels_beyond_period(runner):
    frame = pd.DataFrame({"date": pd.to_datetime(["2019-01-02", "2020-01-02", "2020-12-30"]),
        "symbol": "AAPL", "target_price_downside20": [0., 1., 1.],
        "target_date_price_downside20": pd.to_datetime(["2019-02-01", "2020-02-03", "2021-02-01"])})
    predictions = frame[["date", "symbol"]].assign(probability_downside=[.1, .9, .99])
    audits = [{"first_prediction": f"{year}-01-02", "last_target": f"{year-1}-12-31",
               "training_positive_rate": rate} for year, rate in [(2019, .2), (2020, .3)]]
    result = runner.forecast_report(frame, predictions, audits, "2020-12-31")
    assert result["rows"] == 2
    assert result["auc"] == 1
    assert result["brier"] == pytest.approx(.01)
    assert result["reference_brier"] == pytest.approx((.2**2 + .7**2) / 2)
    changed = frame.copy()
    changed.loc[2, "target_price_downside20"] = 0
    assert runner.forecast_report(changed, predictions, audits, "2020-12-31") == result


def entry_fixture(runner, tmp_path, monkeypatch):
    monkeypatch.setattr(runner, "WORKSPACE", tmp_path)
    root = tmp_path / "entry"
    root.mkdir()
    code = tmp_path / "ml/src/stocklab_ml/swing_research.py"
    archived = root / "source-snapshot/ml/src/stocklab_ml/swing_research.py"
    archived.parent.mkdir(parents=True)
    archived.write_text("frozen source")
    digest = hashlib.sha256(archived.read_bytes()).hexdigest()
    commitment = {"price_inputs_sha256": {"stocks.csv": "prices"}, "dividends_sha256": "events"}
    (root / "protocol-plan.md").write_text("frozen protocol")
    inputs = dict(commitment, code_sha256={str(code): digest}, plan_sha256=runner.sha256(root / "protocol-plan.md"))
    (root / "inputs.json").write_text(json.dumps(inputs))
    full = pd.DataFrame({"date": pd.to_datetime([f"{year}-01-02" for year in range(2018, 2027)]),
        "symbol": pd.Series(["AAPL"] * 9, dtype="string"), "probability_up": .7}).astype({"date": "datetime64[ns]"})
    for name, rows in [("selection", full.iloc[:3]), ("later", full.iloc[3:]), ("full", full)]:
        rows.to_csv(root / f"{name}-predictions.csv", index=False)
    fits = [{"recipe": "rf_swing20", "label": "target_20", "first_prediction": f"{year}-01-02",
        "first_fit": f"{year-4}-01-02", "last_fit": f"{year-1}-11-30", "last_target": f"{year-1}-12-31",
        "train_rows": 1000} for year in range(2018, 2027)]
    for name, rows in [("selection", fits[:3]), ("later", fits[3:])]:
        (root / f"{name}-fits.json").write_text(json.dumps(rows))
    selection = {"admitted": True, "recipe": "rf_swing20", "prediction_sha256": runner.sha256(root / "selection-predictions.csv"),
        "report": {"annualized_return": .09772968}, "original_incumbent": {"max_drawdown": .32681374},
        "inputs_sha256": runner.sha256(root / "inputs.json")}
    (root / "selection.json").write_text(json.dumps(selection))
    summary = {"protocol": "entry-signal-ablation-20261112-v1", "recipe": "rf_swing20", "admitted": True,
        "selection_sha256": runner.sha256(root / "selection.json"), "inputs_sha256": runner.sha256(root / "inputs.json")}
    (root / "summary.json").write_text(json.dumps(summary))
    return root, full[["date", "symbol"]], commitment, archived


@pytest.mark.parametrize("changed", ["archive", "coverage", "fit_boundary"])
def test_entry_cache_checks_archived_sources_exact_coverage_and_purged_fits(runner, tmp_path, monkeypatch, changed):
    root, features, commitment, archived = entry_fixture(runner, tmp_path, monkeypatch)
    predictions, thresholds, hashes = runner.load_entry(root, features, commitment)
    assert len(predictions) == 9 and thresholds["protected_cagr"] == .09772968
    assert "full-predictions.csv" in hashes
    if changed == "archive":
        archived.write_text("changed source")
    elif changed == "coverage":
        frame = pd.read_csv(root / "full-predictions.csv")
        frame.iloc[:-1].to_csv(root / "full-predictions.csv", index=False)
    else:
        path = root / "later-fits.json"
        fits = json.loads(path.read_text())
        fits[0]["last_target"] = fits[0]["first_prediction"]
        path.write_text(json.dumps(fits))
    with pytest.raises(ValueError):
        runner.load_entry(root, features, commitment)


@pytest.mark.parametrize("mode", ["reject", "control", "dual"])
def test_later_fits_and_returns_follow_saved_admissions_only(runner, tmp_path, monkeypatch, mode):
    output = tmp_path / "out"
    keys = pd.DataFrame({"date": pd.to_datetime(["2018-01-02", "2021-01-04"]), "symbol": "AAPL"})
    up = keys.assign(probability_up=.7)
    features = keys.assign(close_ma50=.1, close_ma200=.1, momentum_20=.1)
    monkeypatch.setattr(runner, "load_replay_inputs", lambda *args: ({}, None, None, None, {}, {"code_sha256": {}}))
    monkeypatch.setattr(runner, "load_snapshot", lambda *args: {name: None for name in ("stocks.csv", "monthly.csv", "spy.csv")})
    monkeypatch.setattr(runner.swing, "swing_features", lambda *args: features)
    monkeypatch.setattr(runner.downside, "make_frame", lambda *args: features)
    monkeypatch.setattr(runner, "load_entry", lambda *args: (up, {"protected_cagr": .0977, "original_drawdown": .3268}, {}))
    monkeypatch.setattr(runner, "forecast_report", lambda *args: {"auc": .7, "brier": .18, "reference_brier": .25})
    fits, evaluations = [], []

    def predict(frame, start, end):
        assert (output / "inputs.json").exists()
        if start == "2021-01-01":
            saved = json.loads((output / "selection.json").read_text())
            assert saved["admissions"]["dual"] and mode == "dual"
        fits.append(start)
        return keys.loc[keys.date.between(start, end)].assign(probability_downside=.2), []

    def orders(up, down, context):
        return up[["date", "symbol", "probability_up"]].assign(action="BUY", allocation=1.,
            reason="downside_model", probability_downside=.2)[[
                "date", "symbol", "action", "allocation", "reason", "probability_up", "probability_downside"]]

    def evaluate(prices, spy, events, details, out, label, start, end, cost=5):
        assert details.date.between(start, end).all()
        if label.startswith("selection"):
            assert (out / "selection-control-decisions.csv").exists()
            assert (out / "selection-dual-decisions.csv").exists()
        else:
            saved = json.loads((out / "selection.json").read_text())
            assert any(saved["admissions"].values()) and mode != "reject"
        evaluations.append(label)
        if label.endswith("control"):
            return dict(report(.25 if mode == "control" else .15, .30, 0), benchmark_max_drawdown=.40)
        return dict(report(.25 if mode == "dual" else .10, .20, 2), benchmark_max_drawdown=.40)

    monkeypatch.setattr(runner.downside, "predict", predict)
    monkeypatch.setattr(runner.downside, "orders", orders)
    monkeypatch.setattr(runner, "evaluate_account", evaluate)
    monkeypatch.setattr(sys, "argv", ["downside_research.py", "--study-dir", str(tmp_path), "--data-dir", str(tmp_path),
        "--dividend-dir", str(tmp_path), "--entry-dir", str(tmp_path), "--out", str(output)])
    runner.main()
    summary = json.loads((output / "summary.json").read_text())
    assert summary["selected_arm"] == (None if mode == "reject" else mode)
    assert fits == ["2018-01-01"] + (["2021-01-01"] if mode == "dual" else [])
    assert (len(evaluations) == 2) == (mode == "reject")
    assert summary["selection_sha256"] == runner.sha256(output / "selection.json")
    with pytest.raises(FileExistsError):
        runner.main()


def test_default_accounting_join_counts_only_executed_downside_sales_and_keeps_dividend(runner, tmp_path):
    dates = pd.bdate_range("2024-12-24", periods=8).astype("datetime64[ns]")
    prices = pd.DataFrame({"date": dates, "symbol": pd.Series(["AAPL"] * 8, dtype="string"),
        "open": 100., "high": 101., "low": 99., "close": 100., "volume": pd.Series([1000] * 8, dtype="Int64")})
    spy = prices.assign(symbol=pd.Series(["SPY"] * 8, dtype="string"))
    details = pd.DataFrame({"date": dates[:4], "symbol": "AAPL", "action": ["BUY", "SELL", "SELL", "SELL"],
        "allocation": 1., "reason": ["up_model", "downside_model", "downside_model", "downside_model"],
        "probability_up": .8, "probability_downside": [.2, .8, .8, .8]})
    events = pd.DataFrame({"symbol": pd.Series(["AAPL"], dtype="string"), "ex_date": dates[[2]],
                           "pay_date": dates[[4]], "amount": [1.]})
    result = runner.evaluate_account(prices, spy, events, details, tmp_path, "account", str(dates[0].date()), str(dates[-1].date()))
    assert result["sells"] == result["downside_sells"] == 1
    assert result["up_model_sells"] == 0
    assert result["dividends_earned"] == pytest.approx(99.90007495)
    assert result["dividends_paid"] == result["dividends_earned"]
    assert result["longest_cash_only_sessions"] == 6
    assert result["annual_eligible"]["2025"] == {"buys": 0, "sells": 0}
    audit = pd.read_csv(tmp_path / "account-execution-audit.csv")
    assert audit.reason.tolist() == ["up_model", "downside_model"]
