"""Bounded training-only panel provenance and the existing gated replay flow."""

import hashlib
import importlib.util
import json
from pathlib import Path
import sys

import numpy as np
import pandas as pd
import pytest


@pytest.fixture
def runner(monkeypatch):
    examples = Path(__file__).parents[1] / "examples"
    monkeypatch.syspath_prepend(str(examples))
    spec = importlib.util.spec_from_file_location("broader_runner", examples / "downside_research.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2), encoding="utf-8")


def canonical(runner, rows):
    frame = pd.DataFrame(rows, columns=["date", "symbol"])
    frame["date"] = pd.to_datetime(frame.date).astype("datetime64[ns]")
    frame["symbol"] = frame.symbol.astype("string")
    for column in runner.downside.DOWNSIDE_FEATURES:
        frame[column] = .1
    frame["target_20"] = 1.
    frame["target_date_20"] = frame.date + pd.offsets.BDay(21)
    frame[runner.downside.LABEL] = 0.
    frame[runner.downside.KNOWN_DATE] = frame.date + pd.offsets.BDay(20)
    return frame


@pytest.fixture
def panel_bundle(runner, tmp_path):
    root = tmp_path / "acquisition"
    panel = root / "panel"
    panel.mkdir(parents=True)
    salt = "stocklab-broad-training-20261112-v1|"
    sample = [{"symbol": f"T{i:04d}", "mic_code": "XNYS", "exchange": "NYSE", "currency": "USD",
               "type": "Common Stock", "access": {"plan": "Basic"}, "catalog_active": bool(i % 2)}
              for i in range(256)]
    for record in sample:
        record["rank_sha256"] = hashlib.sha256((salt + "XNYS|" + record["symbol"]).encode()).hexdigest()
    sample.sort(key=lambda record: record["rank_sha256"])
    for index, record in enumerate(sample, 1):
        record["sample_index"] = index
    (root / "protocol-plan.md").write_text("Frozen acquisition protocol", encoding="utf-8")
    selection = {"protocol": "broader-training-20261112-v1", "salt": salt, "sample": sample,
                 "start": "2013-04-01", "end": "2026-11-11", "eligible": 9676,
                 "protocol_sha256": runner.sha256(root / "protocol-plan.md")}
    selection["probe_ids"] = [r["sample_index"] for active in (True, False)
                              for r in [v for v in sample if v["catalog_active"] == active][:3]]
    write_json(root / "selection.json", selection)
    outcomes, source_paths = [], []
    for record in sample:
        identity = "XNYS|" + record["symbol"]
        folder = root / "sources" / hashlib.sha256(identity.encode()).hexdigest()[:16]
        folder.mkdir(parents=True)
        outcome = {"sample_index": record["sample_index"], "identity": identity,
            "catalog_active": record["catalog_active"],
            "params": {"symbol": record["symbol"], "interval": "1day", "start_date": "2013-04-01",
                "end_date": "2026-11-11T23:59:59", "order": "asc", "format": "JSON", "adjust": "splits",
                "exchange": "NYSE", "outputsize": "5000", "mic_code": "XNYS", "country": "US", "type": "Common Stock"},
            "status": "observed_history" if record["sample_index"] <= 20 else "rejected_response"}
        if outcome["status"] == "observed_history":
            (folder / "raw.json").write_text("{}", encoding="utf-8")
            (folder / "cleaned.csv").write_text("fixture source", encoding="utf-8")
            outcome.update(raw_sha256=runner.sha256(folder / "raw.json"),
                           cleaned_sha256=runner.sha256(folder / "cleaned.csv"),
                           response_metadata={"symbol": record["symbol"], "exchange": "NYSE", "mic_code": "XNYS",
                               "currency": "USD", "type": "Common Stock", "interval": "1day", "exchange_timezone": "America/New_York"})
        write_json(folder / "manifest.json", outcome)
        outcomes.append(outcome)
        source_paths.append((folder / "manifest.json").relative_to(root).as_posix())
        write_json(panel / "reports" / f"{record['sample_index']}.json", {"status": outcome["status"]})
    write_json(root / "acquisition-results.json", outcomes)
    write_json(root / "probe-results.json", [outcomes[i - 1] for i in selection["probe_ids"]])
    spy = pd.DataFrame({"date": pd.bdate_range("2017-01-02", "2026-11-11").astype("datetime64[ns]"), "symbol": "SPY"})
    spy_sha = "frozen-spy-hash"
    progress = {"status": "complete", "terminal_outcomes": 256, "pending_indices": [],
        "outcomes_sha256": runner.sha256(root / "acquisition-results.json"),
        "manifest_sha256": {name: runner.sha256(root / name) for name in source_paths},
        "frozen_inputs_sha256": {name: runner.sha256(root / name)
                                for name in ("selection.json", "protocol-plan.md", "probe-results.json")}}
    progress["frozen_inputs_sha256"]["spy.csv"] = spy_sha
    write_json(root / "acquisition-progress.json", progress)
    (root / "compile-panel.py").write_text("# Frozen compiler", encoding="utf-8")
    (panel / "build-protocol.md").write_text("Frozen build protocol", encoding="utf-8")
    write_json(panel / "environment.json", {"python": runner.platform.python_version()} | {
        name: runner.version(name) for name in ("numpy", "pandas", "scikit-learn")})
    (panel / "attrition.csv").write_text("identity,status\n", encoding="utf-8")
    added = canonical(runner, [("2017-11-01", record["symbol"] + ":XNYS") for record in sample[:20]])
    added = added.sort_values(["date", "symbol"]).reset_index(drop=True)
    added.to_csv(panel / "training.csv", index=False)
    evaluation = canonical(runner, [("2018-01-02", symbol) for symbol in ("AAPL", "JNJ", "JPM", "MSFT", "XOM")])
    feature_sources = {f"ml/src/stocklab_ml/{name}.py": runner.sha256(runner.WORKSPACE / f"ml/src/stocklab_ml/{name}.py")
                       for name in ("research", "swing_research", "downside_research", "training_panel",
                                    "features/engineering", "features/models")}
    for name in feature_sources:
        path = panel / "source-snapshot" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes((runner.WORKSPACE / name).read_bytes())
    root_files = {path.relative_to(root).as_posix(): runner.sha256(path) for path in root.rglob("*")
                  if path.is_file() and path != panel / "training.csv"}
    manifest = {"schema_version": 1, "protocol": "broader-training-20261112-v1", "role": "training_only",
        "price_basis": "splits", "monthly_provenance": "daily_derived_complete_months", "sample_count": 256,
        "rows": len(added), "columns": list(added), "training_sha256": runner.sha256(panel / "training.csv"),
        "spy_sha256": spy_sha, "feature_code_sha256": feature_sources, "root_files": root_files}
    write_json(panel / "manifest.json", manifest)
    return panel, evaluation, spy, spy_sha


def manifest_edit(panel, change):
    path = panel / "manifest.json"
    manifest = json.loads(path.read_text())
    change(manifest)
    write_json(path, manifest)


def rewrite_training(panel, change):
    path = panel / "training.csv"
    frame = pd.read_csv(path)
    frame = change(frame)
    frame.to_csv(path, index=False)
    manifest_edit(panel, lambda m: m.update(rows=len(frame), training_sha256=hashlib.sha256(path.read_bytes()).hexdigest()))


def test_loader_returns_only_original_plus_frozen_added_training_rows_and_bound_evidence(runner, panel_bundle):
    panel, evaluation, spy, spy_sha = panel_bundle
    combined, commitment = runner.load_training_panel(panel, evaluation, spy, spy_sha)
    assert len(combined) == 25
    assert list(combined) == list(evaluation)
    assert combined.equals(combined.sort_values(["date", "symbol"]).reset_index(drop=True))
    pd.testing.assert_frame_equal(combined.loc[combined.symbol.isin(evaluation.symbol)].reset_index(drop=True), evaluation)
    assert commitment["pre2018_identities"] == 20
    assert commitment["manifest_sha256"] == runner.sha256(panel / "manifest.json")
    assert commitment["training_sha256"] == runner.sha256(panel / "training.csv")
    assert commitment["role"] == "training_only"


@pytest.mark.parametrize("bad", ["hash", "traversal", "missing_source", "feature_source", "incomplete", "outcomes"])
def test_loader_rejects_tampered_or_incomplete_frozen_acquisition(runner, panel_bundle, bad):
    panel, evaluation, spy, spy_sha = panel_bundle
    if bad == "hash":
        (panel / "training.csv").write_text("tampered", encoding="utf-8")
    elif bad == "traversal":
        manifest_edit(panel, lambda m: m["root_files"].update({"../outside.json": "x"}))
    elif bad == "missing_source":
        manifest_edit(panel, lambda m: m["root_files"].pop(next(k for k in m["root_files"] if k.startswith("sources/"))))
    elif bad == "feature_source":
        manifest_edit(panel, lambda m: m["feature_code_sha256"].update({"ml/src/stocklab_ml/swing_research.py": "stale"}))
    else:
        path = panel.parent / ("acquisition-progress.json" if bad == "incomplete" else "acquisition-results.json")
        data = json.loads(path.read_text())
        if bad == "incomplete":
            data.update(status="acquiring", terminal_outcomes=255, pending_indices=[256])
        else:
            data.pop()
        write_json(path, data)
        manifest_edit(panel, lambda m: m["root_files"].update({path.name: runner.sha256(path)}))
    with pytest.raises(ValueError):
        runner.load_training_panel(panel, evaluation, spy, spy_sha)


@pytest.mark.parametrize("bad", ["column", "duplicate", "nonfinite", "evaluation_symbol", "unknown_symbol", "joint_nan",
                               "label", "target_before", "up_horizon", "risk_horizon", "noncalendar", "too_few", "one_head"])
def test_loader_rejects_bad_schema_targets_and_insufficient_pre2018_identity_coverage(runner, panel_bundle, bad):
    panel, evaluation, spy, spy_sha = panel_bundle

    def change(frame):
        if bad == "column":
            return frame.rename(columns={"momentum_20": "unapproved"})
        if bad == "duplicate":
            return pd.concat([frame, frame.iloc[[0]]], ignore_index=True)
        if bad == "nonfinite":
            frame.loc[0, "momentum_20"] = np.inf
        elif bad == "evaluation_symbol":
            frame.loc[0, "symbol"] = "AAPL:XNYS"
        elif bad == "unknown_symbol":
            frame.loc[0, "symbol"] = "OUTSIDE:XNYS"
        elif bad == "joint_nan":
            frame.loc[0, "target_20"] = np.nan
        elif bad == "label":
            frame.loc[0, "target_20"] = 2.
        elif bad == "target_before":
            frame.loc[0, "target_date_20"] = frame.loc[0, "date"]
        elif bad == "up_horizon":
            frame.loc[0, "target_date_20"] = "2017-11-29"
        elif bad == "risk_horizon":
            frame.loc[0, runner.downside.KNOWN_DATE] = "2017-11-30"
        elif bad == "noncalendar":
            frame.loc[0, "date"] = "2017-11-04"
        elif bad == "too_few":
            return frame.iloc[:19].copy()
        else:
            frame.loc[0, [runner.downside.LABEL, runner.downside.KNOWN_DATE]] = [np.nan, None]
        return frame

    rewrite_training(panel, change)
    with pytest.raises(ValueError):
        runner.load_training_panel(panel, evaluation, spy, spy_sha)


def account_report(cagr, dd, down=0):
    return {"annualized_return": cagr, "max_drawdown": dd, "sells": 3, "downside_sells": down,
        "benchmark_annualized_return": .14, "benchmark_max_drawdown": .40, "target_met": cagr >= .20}


def test_strict_target_admission_rejects_sub_twenty_broader_dual(runner):
    control, dual = account_report(.18, .30), account_report(.19, .20, 2)
    forecast = {"auc": .7, "brier": .18, "reference_brier": .25}
    limits = {"protected_cagr": .0977, "original_drawdown": .3268}
    assert runner.admissions(control, dual, forecast, limits)["dual"]
    assert runner.admissions(control, dual, forecast, limits, strict_target=True) == {"control": False, "dual": False}


@pytest.mark.parametrize("mode", ["reject", "control", "dual"])
def test_broader_annual_fits_use_both_heads_and_later_work_follows_frozen_admissions(runner, tmp_path, monkeypatch, mode):
    output = tmp_path / "out"
    keys = pd.DataFrame({"date": pd.to_datetime(["2018-01-02", "2021-01-04"]), "symbol": "AAPL"})
    cached = keys.assign(probability_up=.01)
    features = keys.assign(close_ma50=.1, close_ma200=.1, momentum_20=.1)
    combined = keys.assign(symbol="ADDED:XNYS")
    panel_commitment = {"manifest_sha256": "panel", "training_sha256": "training", "role": "training_only"}
    monkeypatch.setattr(runner, "load_replay_inputs", lambda *a: ({}, None, None, None, {}, {
        "code_sha256": {}, "price_inputs_sha256": {"spy.csv": "spy"}}))
    monkeypatch.setattr(runner, "load_snapshot", lambda *a: {k: None for k in ("stocks.csv", "monthly.csv", "spy.csv")})
    monkeypatch.setattr(runner.swing, "swing_features", lambda *a: features)
    monkeypatch.setattr(runner.downside, "make_frame", lambda *a: features)
    monkeypatch.setattr(runner, "load_entry", lambda *a: (cached, {"protected_cagr": .0977, "original_drawdown": .3268}, {}))

    def load_panel(path, frame, spy, digest):
        assert path == tmp_path / "panel" and digest == "spy" and not output.exists()
        assert frame is features
        return combined, panel_commitment

    monkeypatch.setattr(runner, "load_training_panel", load_panel)
    fits, evaluations = [], []

    def fit(head, frame, start, end, options):
        assert frame is features and options["training_frame"] is combined and options["date_weighted"] is True
        assert options.get("refit_frequency", "annual") == "annual"
        inputs = json.loads((output / "inputs.json").read_text())
        assert inputs["training_panel"] == panel_commitment and inputs["refit_frequency"] == "annual"
        if start == "2021-01-01":
            assert json.loads((output / "selection.json").read_text())["selected_arm"] == mode != "reject"
            assert head == "up" or mode == "dual"
        fits.append((head, start))
        return keys.loc[keys.date.between(start, end)].copy(), []

    def up(frame, recipe, start, end, **options):
        assert recipe == "rf_swing20"
        rows, audits = fit("up", frame, start, end, options)
        return rows.assign(probability_up=.7), audits

    def down(frame, start, end, **options):
        rows, audits = fit("down", frame, start, end, options)
        return rows.assign(probability_downside=.2), audits

    def forecast(frame, predictions, audits, end, **options):
        assert options.get("refit_frequency", "annual") == "annual"
        assert "training_frame" not in options
        return {"auc": .7, "brier": .18, "reference_brier": .25}

    def orders(up, down, context):
        assert up.probability_up.eq(.7).all()
        return up.assign(action="BUY", allocation=1., reason="up_model", probability_downside=.2)

    def evaluate(prices, spy, events, details, out, label, start, end, cost=5):
        assert details.probability_up.eq(.7).all() and details.date.between(start, end).all()
        if label.startswith("selection"):
            for name in ("selection-upside-predictions.csv", "selection-downside-predictions.csv",
                         "selection-control-decisions.csv", "selection-dual-decisions.csv"):
                assert (out / name).exists()
        else:
            assert json.loads((out / "selection.json").read_text())["selected_arm"] == mode
        evaluations.append(label)
        dual = "-dual" in label
        cagr = (.195 if dual else .19) if mode == "reject" else (.25 if (mode == "dual") == dual else .22)
        return account_report(cagr, .20 if dual else .30, 2 if dual else 0)

    monkeypatch.setattr(runner.swing, "walk_forward_predict", up)
    monkeypatch.setattr(runner.downside, "predict", down)
    monkeypatch.setattr(runner.downside, "orders", orders)
    monkeypatch.setattr(runner, "forecast_report", forecast)
    monkeypatch.setattr(runner, "evaluate_account", evaluate)
    monkeypatch.setattr(sys, "argv", ["downside_research.py", "--study-dir", str(tmp_path), "--data-dir", str(tmp_path),
        "--dividend-dir", str(tmp_path), "--entry-dir", str(tmp_path), "--out", str(output), "--training-panel", str(tmp_path / "panel")])
    runner.main()
    summary = json.loads((output / "summary.json").read_text())
    inputs = json.loads((output / "inputs.json").read_text())
    assert summary["protocol"] == inputs["protocol"] == "broader-training-model-20261112-v1"
    assert summary["refit_frequency"] == "annual"
    assert inputs["entry_forecast_role"] == "contextual provenance only; broader decisions use refitted upside scores"
    parameters = runner.WORKSPACE / "ml/src/stocklab_ml/modeling/random_forest.py"
    archived = output / "source-snapshot" / parameters.relative_to(runner.WORKSPACE)
    assert inputs["code_sha256"][str(parameters)] == runner.sha256(parameters) == runner.sha256(archived)
    assert summary["selected_arm"] == (None if mode == "reject" else mode)
    assert set(fits[:2]) == {("up", "2018-01-01"), ("down", "2018-01-01")}
    assert set(fits[2:]) == ({("up", "2021-01-01"), ("down", "2021-01-01")} if mode == "dual" else (
        {("up", "2021-01-01")} if mode == "control" else set()))
    assert len(evaluations) == (2 if mode == "reject" else 6 if mode == "control" else 8)
    assert (output / "later-fits.json").exists() == (mode == "dual")
    assert (output / "full-upside-predictions.csv").exists() == (mode != "reject")
    assert "Broader Training Data" in (output / "protocol-plan.md").read_text()
    assert summary["selection_sha256"] == runner.sha256(output / "selection.json")


def test_monthly_and_broader_modes_are_mutually_exclusive_before_reading_sources(runner, tmp_path, monkeypatch):
    monkeypatch.setattr(runner, "load_replay_inputs", lambda *a: pytest.fail("No input reads after invalid CLI"))
    monkeypatch.setattr(sys, "argv", ["downside_research.py", "--study-dir", str(tmp_path), "--data-dir", str(tmp_path),
        "--dividend-dir", str(tmp_path), "--entry-dir", str(tmp_path), "--out", str(tmp_path / "out"),
        "--monthly", "--training-panel", str(tmp_path / "panel")])
    with pytest.raises(SystemExit) as error:
        runner.main()
    assert error.value.code == 2
