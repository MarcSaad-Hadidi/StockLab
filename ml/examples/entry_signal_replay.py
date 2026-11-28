"""Single predeclared raw-RF entry ablation with frozen protective exits."""

import argparse
import json
from pathlib import Path
import sys

import pandas as pd

from stocklab_ml import research, swing_research as swing
from dividend_replay import load_replay_inputs
from risk_exit_replay import evaluate_replay
from swing_research import load_snapshot, sha256

RECIPE = "rf_swing20"
RISK_POLICY = "trailing15_spy200"


def admit_candidate(candidate: dict, protected: dict, original: dict) -> bool:
    return bool(candidate["annualized_return"] > protected["annualized_return"]
                and candidate["max_drawdown"] < original["max_drawdown"]
                and candidate["protective_sells"] > 0)


def load_prior_risk(root: Path, commitment: dict) -> tuple:
    """Reuse incumbent reports only with identical original data and predictions."""
    summary = json.loads((root / "summary.json").read_text(encoding="utf-8"))
    selection = json.loads((root / "selection.json").read_text(encoding="utf-8"))
    inputs = json.loads((root / "inputs.json").read_text(encoding="utf-8"))
    if (summary.get("protocol") != "protective-exits-20261112-v1"
            or summary.get("chosen_policy") != RISK_POLICY or selection.get("chosen_policy") != RISK_POLICY
            or summary.get("selection_sha256") != sha256(root / "selection.json")
            or summary.get("inputs_sha256") != sha256(root / "inputs.json")
            or selection.get("inputs_sha256") != summary["inputs_sha256"]
            or any(inputs.get("code_sha256", {}).get(str(path)) != sha256(path)
                   for path in (Path(research.__file__), Path(swing.__file__)))
            or any(inputs.get(key) != value for key, value in commitment.items() if key != "code_sha256")):
        raise ValueError("Prior protective-exit evidence changed or differs from the frozen experiment.")
    hashes = {path.name: sha256(path) for path in sorted(root.glob("*.csv"))}
    hashes.update({name: sha256(root / name) for name in ("summary.json", "selection.json", "inputs.json")})
    return summary, selection, hashes


def signal_diagnostics(predictions: pd.DataFrame, orders: pd.DataFrame) -> dict:
    reports = {}
    for year, rows in predictions.groupby(predictions.date.dt.year, sort=True):
        eligible = orders.loc[orders.date.dt.year.eq(year)]
        reports[str(year)] = {"predictions": len(rows), "minimum_score": float(rows.probability_up.min()),
            "maximum_score": float(rows.probability_up.max()), "buy_score_hits": int(rows.probability_up.ge(.65).sum()),
            "sell_score_hits": int(rows.probability_up.le(.35).sum()),
            "eligible_buys": int(eligible.action.eq("BUY").sum()), "eligible_sells": int(eligible.action.eq("SELL").sum())}
    return reports


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("study-dir", "data-dir", "dividend-dir", "risk-dir", "out"):
        parser.add_argument(f"--{name}", type=Path, required=True)
    args = parser.parse_args()
    parent, prices, spy, events, _, commitment = load_replay_inputs(args.study_dir, args.data_dir, args.dividend_dir)
    if parent["chosen_model"] != "rf_calibrated20" or parent["exit_policy"] != "trend200":
        raise ValueError("The fixed ablation requires the calibrated20/trend200 incumbent.")
    prior, prior_selection, prior_hashes = load_prior_risk(args.risk_dir, commitment)
    frames = load_snapshot(args.data_dir)
    features = swing.swing_features(frames["stocks.csv"], frames["monthly.csv"], frames["spy.csv"])
    plan = Path(__file__).resolve().parents[2] / "docs/superpowers/plans/2026-11-12-ml-risk-exits.md"
    for path in (Path(__file__), Path(sys.modules["risk_exit_replay"].__file__), Path(sys.modules["swing_research"].__file__)):
        commitment["code_sha256"][str(path)] = sha256(path)
    commitment.update(protocol="entry-signal-ablation-20261112-v1", recipe=RECIPE, exit_policy="trend200",
        risk_policy=RISK_POLICY, prior_risk_sha256=prior_hashes)
    args.out.mkdir(parents=True, exist_ok=False)
    (args.out / "protocol-plan.md").write_bytes(plan.read_bytes())
    commitment["plan_sha256"] = sha256(args.out / "protocol-plan.md")
    (args.out / "inputs.json").write_text(json.dumps(commitment, indent=2), encoding="utf-8")

    def fit_period(label: str, start: str, end: str):
        predictions, audits = swing.walk_forward_predict(features, RECIPE, start, end)
        predictions.to_csv(args.out / f"{label}-predictions.csv", index=False)
        (args.out / f"{label}-fits.json").write_text(json.dumps(audits, indent=2), encoding="utf-8")
        orders = swing.filtered_orders(predictions, features, "trend200")
        diagnostics = signal_diagnostics(predictions, orders)
        return predictions, orders, audits, diagnostics

    predictions, orders, audits, diagnostics = fit_period("selection", "2018-01-01", "2020-12-31")
    selected = evaluate_replay(prices, spy, events, orders, args.out, "selection-candidate", RISK_POLICY,
                               "2018-01-01", "2020-12-31")
    protected = prior_selection["reports"][RISK_POLICY]
    original = prior_selection["reports"]["baseline"]
    admitted = admit_candidate(selected, protected, original)
    selection_path = args.out / "selection.json"
    selection_path.write_text(json.dumps({"recipe": RECIPE, "exit_policy": "trend200", "risk_policy": RISK_POLICY,
        "admitted": admitted, "report": selected, "protected_incumbent": protected, "original_incumbent": original,
        "prediction_sha256": sha256(args.out / "selection-predictions.csv"), "fit_audits": audits,
        "signal_diagnostics": diagnostics, "inputs_sha256": sha256(args.out / "inputs.json")}, indent=2), encoding="utf-8")
    selection_hash = sha256(selection_path)
    print(f"Frozen entry admission: {admitted}; SHA256={selection_hash}", flush=True)
    results = {}
    if admitted:
        later, later_orders, _, later_diagnostics = fit_period("later", "2021-01-01", "2026-11-11")
        combined = pd.concat([predictions, later], ignore_index=True)
        combined.to_csv(args.out / "full-predictions.csv", index=False)
        full_orders = swing.filtered_orders(combined, features, "trend200")
        diagnostics.update(later_diagnostics)
        for label, period_orders, start in [("later", later_orders, "2021-01-01"), ("full", full_orders, "2018-01-01")]:
            report = evaluate_replay(prices, spy, events, period_orders, args.out, f"{label}-candidate", RISK_POLICY,
                                     start, "2026-11-11")
            report["all_objectives_met"] = bool(report["target_met"] and report["protective_sells"] > 0
                and report["max_drawdown"] < prior["results"][label]["baseline"]["max_drawdown"])
            results[label] = {"original_incumbent": prior["results"][label]["baseline"],
                "protected_incumbent": prior["results"][label]["selected"], "candidate": report,
                "candidate_higher_costs": evaluate_replay(prices, spy, events, period_orders, args.out,
                    f"{label}-candidate-higher-costs", RISK_POLICY, start, "2026-11-11", cost=25)}
    summary = {"protocol": "entry-signal-ablation-20261112-v1", "recipe": RECIPE, "risk_policy": RISK_POLICY,
        "admitted": admitted, "selection_sha256": selection_hash, "inputs_sha256": sha256(args.out / "inputs.json"),
        "results": results, "signal_diagnostics": diagnostics,
        "costs_bps_per_side": {"fee": 5, "slippage": 5}, "higher_costs_bps_per_side": {"fee": 25, "slippage": 25},
        "all_objectives_met": bool(admitted and all(results[p]["candidate"]["all_objectives_met"] for p in results)),
        "limitations": "Single adaptive historical ablation removes calibration and fits the latest eligible year; raw RF scores are not validated probabilities. Five surviving stocks, no pristine holdout, taxes, final liquidation or live promotion."}
    (args.out / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
