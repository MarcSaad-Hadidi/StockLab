"""One dedicated downside head, frozen entry forecasts and a paired control."""

import argparse
from importlib.metadata import version
import json
from pathlib import Path
import sys

import numpy as np
import pandas as pd
from sklearn.metrics import brier_score_loss, roc_auc_score

from stocklab_ml import downside_research as downside, research, swing_research as swing
from dividend_replay import load_replay_inputs
from risk_exit_replay import evaluate_replay
from swing_research import load_snapshot, sha256

WORKSPACE = Path(__file__).resolve().parents[2]
SIM_COLUMNS = ["date", "symbol", "action", "allocation"]


def load_entry(root: Path, features: pd.DataFrame, commitment: dict) -> tuple:
    """Validate historical code archives; changed current code never replaces them."""
    summary = json.loads((root / "summary.json").read_text(encoding="utf-8"))
    selection = json.loads((root / "selection.json").read_text(encoding="utf-8"))
    inputs = json.loads((root / "inputs.json").read_text(encoding="utf-8"))
    if (summary.get("protocol") != "entry-signal-ablation-20261112-v1" or summary.get("recipe") != "rf_swing20"
            or summary.get("admitted") is not True or selection.get("admitted") is not True
            or summary.get("selection_sha256") != sha256(root / "selection.json")
            or summary.get("inputs_sha256") != sha256(root / "inputs.json")
            or selection.get("inputs_sha256") != summary["inputs_sha256"]
            or inputs.get("plan_sha256") != sha256(root / "protocol-plan.md")
            or not inputs.get("code_sha256")
            or any(inputs.get(key) != value for key, value in commitment.items() if key != "code_sha256")):
        raise ValueError("Entry forecasts differ from their original frozen inputs or selection.")
    for source, digest in inputs["code_sha256"].items():
        archived = root / "source-snapshot" / Path(source).relative_to(WORKSPACE)
        if sha256(archived) != digest:
            raise ValueError("The original entry-model source archive changed.")
    frames = {}
    for name in ("selection", "later", "full"):
        frame = pd.read_csv(root / f"{name}-predictions.csv", parse_dates=["date"], float_precision="round_trip",
            dtype={"symbol": "string", "probability_up": "float64"}).astype({"date": "datetime64[ns]"})
        if (list(frame.columns) != ["date", "symbol", "probability_up"]
                or not np.isfinite(frame.probability_up).all() or not frame.probability_up.between(0, 1).all()):
            raise ValueError("Invalid frozen raw-RF scores.")
        frames[name] = frame
    expected = features.loc[features.date.between("2018-01-01", "2026-11-11"), ["date", "symbol"]].reset_index(drop=True)
    if (not frames["full"][["date", "symbol"]].equals(expected)
            or not pd.concat([frames["selection"], frames["later"]], ignore_index=True).equals(frames["full"])
            or not frames["selection"].date.between("2018-01-01", "2020-12-31").all()
            or not frames["later"].date.between("2021-01-01", "2026-11-11").all()
            or selection.get("prediction_sha256") != sha256(root / "selection-predictions.csv")):
        raise ValueError("Entry forecasts must cover exactly the same ordered dates and symbols.")
    fits = sum((json.loads((root / f"{name}-fits.json").read_text(encoding="utf-8"))
                for name in ("selection", "later")), [])
    if len(fits) != 9 or [pd.Timestamp(row["first_prediction"]).year for row in fits] != list(range(2018, 2027)):
        raise ValueError("Nine annual entry fits are required.")
    for audit in fits:
        first = pd.Timestamp(audit["first_prediction"])
        rows = frames["full"].loc[frames["full"].date.dt.year.eq(first.year)]
        if (audit["recipe"] != "rf_swing20" or audit["label"] != "target_20" or audit["train_rows"] <= 0
                or first != rows.date.min() or not pd.Timestamp(audit["last_target"]) < first
                or not first - pd.DateOffset(years=4) <= pd.Timestamp(audit["first_fit"])
                    <= pd.Timestamp(audit["last_fit"]) < first):
            raise ValueError("Entry fit metadata violates its chronological contract.")
    names = ["summary.json", "inputs.json", "selection.json", "protocol-plan.md"]
    names += [f"{name}-predictions.csv" for name in frames]
    names += [f"{name}-fits.json" for name in ("selection", "later")]
    return frames["full"], {"protected_cagr": selection["report"]["annualized_return"],
        "original_drawdown": selection["original_incumbent"]["max_drawdown"]}, {name: sha256(root / name) for name in names}


def forecast_report(frame: pd.DataFrame, predictions: pd.DataFrame, audits: list[dict], end: str) -> dict:
    rows = predictions.merge(frame[["date", "symbol", downside.LABEL, downside.KNOWN_DATE]],
                             on=["date", "symbol"], how="left", validate="one_to_one")
    rows = rows.loc[rows[downside.KNOWN_DATE].le(pd.Timestamp(end)) & rows[downside.LABEL].notna()]
    rates = {}
    for audit in audits:
        first = pd.Timestamp(audit["first_prediction"])
        rate = audit["training_positive_rate"]
        if first.year in rates or not 0 < rate < 1 or not pd.Timestamp(audit["last_target"]) < first:
            raise ValueError("Forecast references need one causal training prevalence per annual fit.")
        rates[first.year] = rate
    reference = rows.date.dt.year.map(rates)
    if rows.empty or reference.isna().any():
        raise ValueError("Observed risk labels and causal reference forecasts are required.")
    labels, scores = rows[downside.LABEL], rows.probability_downside
    return {"rows": len(rows), "auc": float(roc_auc_score(labels, scores)) if labels.nunique() == 2 else None,
        "brier": float(brier_score_loss(labels, scores)),
        "reference_brier": float(brier_score_loss(labels, reference)),
        "actual_positive_rate": float(labels.mean()), "mean_score": float(scores.mean())}


def admissions(control: dict, dual: dict, forecast: dict, thresholds: dict) -> dict:
    return {"control": bool(control["annualized_return"] >= .20
                and control["annualized_return"] > control["benchmark_annualized_return"]
                and control["max_drawdown"] < thresholds["original_drawdown"] and control["sells"] > 0),
        "dual": bool(dual["annualized_return"] >= control["annualized_return"]
            and dual["max_drawdown"] < control["max_drawdown"] and dual["downside_sells"] > 0
            and forecast["auc"] is not None and forecast["auc"] > .5
            and forecast["brier"] < forecast["reference_brier"]
            and dual["annualized_return"] > thresholds["protected_cagr"]
            and dual["max_drawdown"] < thresholds["original_drawdown"])}


def control_details(up: pd.DataFrame, features: pd.DataFrame) -> pd.DataFrame:
    return swing.filtered_orders(up, features, "trend200").merge(up, on=["date", "symbol"], validate="one_to_one").assign(reason="up_model")


def evaluate_account(prices, spy, events, details: pd.DataFrame, out: Path, label: str,
                     start: str, end: str, cost: int = 5) -> dict:
    report = evaluate_replay(prices, spy, events, details[SIM_COLUMNS], out, label, None, start, end, cost)
    trades = pd.read_csv(out / f"{label}-trades.csv", parse_dates=["decision_date", "execution_date"], float_precision="round_trip")
    curve = pd.read_csv(out / f"{label}-equity.csv", parse_dates=["date"], float_precision="round_trip")
    benchmark = pd.read_csv(out / f"{label}-spy.csv", float_precision="round_trip")
    audit = trades.merge(details.rename(columns={"date": "decision_date"}).drop(columns="allocation"),
                         on=["decision_date", "symbol", "action"], how="left", validate="many_to_one")
    if audit.reason.isna().any() or not audit.reason.isin(["up_model", "downside_model"]).all():
        raise ValueError("Every executed order needs its actual model decision reason.")
    down = audit.loc[audit.action.eq("SELL") & audit.reason.eq("downside_model")]
    if len(down) and ("probability_downside" not in down or not down.probability_downside.ge(.65).all()):
        raise ValueError("A downside sale needs its genuine model score.")
    audit.to_csv(out / f"{label}-execution-audit.csv", index=False)
    actual = curve.loc[curve.date > pd.Timestamp(start)].copy()
    actual["invested"] = (actual.total_value - actual.cash - actual.dividends_receivable) / actual.total_value
    flat = (actual.total_value - actual.cash - actual.dividends_receivable).abs().le(1e-7)
    annual = {str(year): {"mean_invested_fraction": float(rows.invested.mean()),
                          "mean_cash_fraction": float((rows.cash / rows.total_value).mean())}
              for year, rows in actual.groupby(actual.date.dt.year, sort=True)}
    report.update(downside_sells=len(down), up_model_sells=int((audit.action.eq("SELL") & audit.reason.eq("up_model")).sum()),
        mean_invested_fraction=float(actual.invested.mean()), annual_exposure=annual,
        longest_cash_only_sessions=int(flat.groupby((~flat).cumsum()).sum().max()),
        benchmark_max_drawdown=float((1 - benchmark.total_value / benchmark.total_value.cummax()).max()),
        annual_eligible={str(year): {
            "buys": int((details.date.dt.year.eq(year) & details.action.eq("BUY")).sum()),
            "sells": int((details.date.dt.year.eq(year) & details.action.eq("SELL")).sum())}
            for year in sorted(actual.date.dt.year.unique())})
    return report


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("study-dir", "data-dir", "dividend-dir", "entry-dir", "out"):
        parser.add_argument(f"--{name}", type=Path, required=True)
    args = parser.parse_args()
    _, prices, spy, events, _, commitment = load_replay_inputs(args.study_dir, args.data_dir, args.dividend_dir)
    frames = load_snapshot(args.data_dir)
    features = swing.swing_features(frames["stocks.csv"], frames["monthly.csv"], frames["spy.csv"])
    up, thresholds, entry_hashes = load_entry(args.entry_dir, features, commitment)
    frame = downside.make_frame(features, prices, spy)
    args.out.mkdir(parents=True, exist_ok=False)
    protocol = WORKSPACE / "docs/superpowers/plans/2026-11-12-downside-ml.md"
    (args.out / "protocol-plan.md").write_bytes(protocol.read_bytes())
    paths = [Path(__file__), Path(downside.__file__), Path(research.__file__), Path(swing.__file__)]
    paths += [Path(sys.modules[name].__file__) for name in ("dividend_replay", "risk_exit_replay", "swing_research")]
    for path in paths:
        archived = args.out / "source-snapshot" / path.resolve().relative_to(WORKSPACE)
        archived.parent.mkdir(parents=True, exist_ok=True)
        archived.write_bytes(path.read_bytes())
        commitment["code_sha256"][str(path)] = sha256(archived)
    commitment.update(protocol="dedicated-downside-20261112-v1", entry_sha256=entry_hashes,
        plan_sha256=sha256(args.out / "protocol-plan.md"), thresholds=thresholds,
        environment={name: version(name) for name in ("numpy", "pandas", "scikit-learn")})
    (args.out / "inputs.json").write_text(json.dumps(commitment, indent=2), encoding="utf-8")

    def predict(label: str, start: str, end: str):
        predictions, audits = downside.predict(frame, start, end)
        predictions.to_csv(args.out / f"{label}-downside-predictions.csv", index=False)
        (args.out / f"{label}-fits.json").write_text(json.dumps(audits, indent=2), encoding="utf-8")
        return predictions, audits

    def evaluate(label: str, details: pd.DataFrame, start: str, end: str, cost: int = 5):
        details = details.loc[details.date.between(start, end)].copy()
        details.to_csv(args.out / f"{label}-decisions.csv", index=False)
        return evaluate_account(prices, spy, events, details, args.out, label, start, end, cost)

    down, fits = predict("selection", "2018-01-01", "2020-12-31")
    selection_up = up.loc[up.date.between("2018-01-01", "2020-12-31")]
    decisions = {"control": control_details(selection_up, features), "dual": downside.orders(selection_up, down, features)}
    for arm, details in decisions.items():
        details.to_csv(args.out / f"selection-{arm}-decisions.csv", index=False)
    forecast = forecast_report(frame, down, fits, "2020-12-31")
    selection = {arm: evaluate(f"selection-{arm}", details, "2018-01-01", "2020-12-31") for arm, details in decisions.items()}
    admitted = admissions(selection["control"], selection["dual"], forecast, thresholds)
    selected = "dual" if admitted["dual"] else "control" if admitted["control"] else None
    selection_path = args.out / "selection.json"
    selection_path.write_text(json.dumps({"admissions": admitted, "selected_arm": selected, "reports": selection,
        "forecast": forecast, "inputs_sha256": sha256(args.out / "inputs.json")}, indent=2), encoding="utf-8")
    print(f"Frozen admissions: {admitted}; selected={selected}; SHA256={sha256(selection_path)}", flush=True)
    results, forecasts = {}, {"selection": forecast}
    if selected:
        controls = control_details(up, features)
        if selected == "dual":
            later, later_fits = predict("later", "2021-01-01", "2026-11-11")
            down = pd.concat([down, later], ignore_index=True)
            down.to_csv(args.out / "full-downside-predictions.csv", index=False)
            forecasts["later"] = forecast_report(frame, later, later_fits, "2026-11-11")
            forecasts["full"] = forecast_report(frame, down, fits + later_fits, "2026-11-11")
            decisions = {"control": controls, "dual": downside.orders(up, down, features)}
        else:
            decisions = {"control": controls}
        for period, start in (("later", "2021-01-01"), ("full", "2018-01-01")):
            results[period] = {}
            for arm, details in decisions.items():
                report = evaluate(f"{period}-{arm}", details, start, "2026-11-11")
                limit = thresholds["original_drawdown"] if period == "full" else report["benchmark_max_drawdown"]
                report["all_objectives_met"] = bool(report["target_met"] and report["max_drawdown"] < limit
                    and (report["downside_sells"] > 0 if arm == "dual" else report["sells"] > 0))
                results[period][arm] = report
            results[period]["selected_higher_costs"] = evaluate(f"{period}-{selected}-higher-costs",
                decisions[selected], start, "2026-11-11", 25)
    summary = {"protocol": "dedicated-downside-20261112-v1", "selected_arm": selected, "admissions": admitted,
        "selection_sha256": sha256(selection_path), "inputs_sha256": sha256(args.out / "inputs.json"),
        "results": results, "forecasts": forecasts,
        "all_objectives_met": bool(selected and all(results[p][selected]["all_objectives_met"] for p in results)),
        "costs_bps_per_side": {"fee": 5, "slippage": 5}, "higher_costs_bps_per_side": {"fee": 25, "slippage": 25},
        "limitations": "Adaptive five-surviving-stock history; raw scores are not calibrated probabilities. Price-risk labels exclude cash dividends. No pristine holdout, taxes, final liquidation or live promotion."}
    (args.out / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
