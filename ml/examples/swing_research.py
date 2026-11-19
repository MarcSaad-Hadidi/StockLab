"""Frozen long-history executable-target experiment; see its plan before running."""

import argparse
import hashlib
import json
from pathlib import Path
import platform
import subprocess
import sys

import numpy as np
import pandas as pd
import sklearn

from stocklab_ml import research, swing_research as swing
from stocklab_ml.features.engineering import _validate_input

SYMBOLS = ["AAPL", "JNJ", "JPM", "MSFT", "XOM"]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_snapshot(root: Path) -> dict[str, pd.DataFrame]:
    manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    adjustments = {"stocks.csv": "splits", "monthly.csv": "splits", "spy.csv": "all"}
    if (manifest.get("schema_version") != 1 or manifest.get("protocol") != "long-horizon-20261111-v1"
            or manifest.get("symbols") != SYMBOLS or manifest.get("history_start") != "2010-01-01"
            or manifest.get("as_of") != "2026-11-11" or manifest.get("adjustments") != adjustments
            or set(manifest.get("files", {})) != set(adjustments)):
        raise ValueError("Snapshot does not match the frozen long-history protocol.")
    frames = {}
    for name in adjustments:
        path = root / name
        if sha256(path) != manifest["files"][name]:
            raise ValueError("Snapshot CSV hash differs from its manifest.")
        frame = pd.read_csv(path, parse_dates=["date"], dtype={"symbol": "string", "volume": "Int64",
            **{column: "float64" for column in ("open", "high", "low", "close")}}).astype({"date": "datetime64[ns]"})
        _validate_input(frame)
        expected_symbols = ["SPY"] if name == "spy.csv" else SYMBOLS
        if sorted(frame.symbol.unique()) != expected_symbols or frame.date.min().year != 2010:
            raise ValueError("Snapshot universe or historical start differs from the protocol.")
        if name != "monthly.csv" and frame.date.max() != pd.Timestamp("2026-11-11"):
            raise ValueError("Incomplete final observed period.")
        frames[name] = frame
    return frames


def probability_orders(predictions: pd.DataFrame) -> pd.DataFrame:
    frame = predictions[["date", "symbol"]].copy()
    frame["action"] = np.select([predictions.probability_up >= .65, predictions.probability_up <= .35],
                                 ["BUY", "SELL"], default="HOLD")
    frame["allocation"] = 1.
    return frame.loc[frame.action.ne("HOLD")].reset_index(drop=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-dir", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True, help="New directory; never overwrite prior evidence")
    args = parser.parse_args()
    frames = load_snapshot(args.data_dir)
    daily, monthly, spy = (frames[name] for name in ("stocks.csv", "monthly.csv", "spy.csv"))
    features = swing.swing_features(daily, monthly, spy)
    args.out.mkdir(parents=True, exist_ok=False)
    inputs = {name: sha256(args.data_dir / name) for name in (*frames, "manifest.json")}
    supporting = ["stocklab_ml.features.engineering", "stocklab_ml.features.models",
        "stocklab_ml.modeling.random_forest", "stocklab_ml.modeling.logistic",
        "stocklab_ml.backtesting.accounting", "stocklab_ml.backtesting.contracts"]
    code = {str(path): sha256(path) for path in [Path(__file__), Path(swing.__file__), Path(research.__file__),
        *(Path(sys.modules[name].__file__) for name in supporting)]}
    environment = {"python": platform.python_version(), "sklearn": sklearn.__version__,
        "numpy": np.__version__, "pandas": pd.__version__, "git_head": subprocess.run(
            ["git", "rev-parse", "HEAD"], check=True, capture_output=True, text=True).stdout.strip()}

    def evaluate(name: str, orders: pd.DataFrame, start: str, end: str, cost: int = 5) -> dict:
        curve, trades = research.simulate(daily, orders, start, end, fee_bps=cost, slippage_bps=cost)
        benchmark = research.spy_curve(spy, curve.date, fee_bps=cost, slippage_bps=cost)
        result = research.assess(curve, benchmark)
        result.update(trades=len(trades), buys=int(trades.action.eq("BUY").sum()), sells=int(trades.action.eq("SELL").sum()),
            fees=float(trades.fee.sum()), final_cash=float(curve.cash.iloc[-1]),
            calendar_returns=swing.calendar_returns(curve), benchmark_calendar_returns=swing.calendar_returns(benchmark))
        curve.to_csv(args.out / f"{name}-equity.csv", index=False)
        trades.to_csv(args.out / f"{name}-trades.csv", index=False)
        benchmark.to_csv(args.out / f"{name}-spy.csv", index=False)
        print(f"{name}: CAGR={result['annualized_return']:.6%}, DD={result['max_drawdown']:.6%}, trades={len(trades)}, target={result['target_met']}", flush=True)
        return result

    selection, selection_fits, rf5_predictions = {}, [], None
    for name in swing.RECIPES:
        predictions, audit = swing.walk_forward_predict(features, name, "2015-01-01", "2017-12-31")
        predictions.to_csv(args.out / f"selection-{name}-predictions.csv", index=False)
        if name == "rf_swing5":
            rf5_predictions = predictions
        selection_fits.extend(audit)
        selection[name] = evaluate(f"selection-{name}", probability_orders(predictions), "2015-01-01", "2017-12-31")
    chosen_model = swing.choose_recipe(selection)
    policy_reports, exit_policy = {}, None
    if chosen_model == "rf_daily_v1":
        for policy in ("trend50", "persistent5", "trend200"):
            orders = swing.filtered_orders(rf5_predictions, features, policy)
            policy_reports[policy] = evaluate(f"selection-rf_swing5-{policy}", orders, "2015-01-01", "2017-12-31")
        policy_choice = swing.choose_recipe({"rf_daily_v1": selection["rf_daily_v1"], **policy_reports})
        if policy_choice != "rf_daily_v1":
            chosen_model, exit_policy = "rf_swing5", policy_choice
    incumbent = chosen_model if exit_policy is None else f"{chosen_model}-{exit_policy}"
    incumbent_report = selection[chosen_model] if exit_policy is None else policy_reports[exit_policy]
    calibrated_reports = {}
    if not incumbent_report["target_met"]:
        for name in ("rf_calibrated5", "rf_calibrated20"):
            predictions, audit = swing.walk_forward_predict(features, name, "2015-01-01", "2017-12-31")
            predictions.to_csv(args.out / f"selection-{name}-predictions.csv", index=False)
            selection_fits.extend(audit)
            calibrated_reports[name] = evaluate(f"selection-{name}-trend200",
                swing.filtered_orders(predictions, features, "trend200"), "2015-01-01", "2017-12-31")
        choice = swing.choose_recipe({"rf_daily_v1": selection["rf_daily_v1"],
                                       incumbent: incumbent_report, **calibrated_reports})
        if choice in calibrated_reports:
            chosen_model, exit_policy = choice, "trend200"
    current = chosen_model if exit_policy is None else f"{chosen_model}-{exit_policy}"
    current_report = (calibrated_reports[chosen_model] if chosen_model in calibrated_reports
                      else selection[chosen_model] if exit_policy is None else policy_reports[exit_policy])
    quarterly_report = None
    if not current_report["target_met"]:
        predictions, audit = swing.walk_forward_predict(features, "rf_calibrated63", "2015-01-01", "2017-12-31")
        predictions.to_csv(args.out / "selection-rf_calibrated63-predictions.csv", index=False)
        selection_fits.extend(audit)
        quarterly_report = evaluate("selection-rf_calibrated63-trend200",
            swing.filtered_orders(predictions, features, "trend200"), "2015-01-01", "2017-12-31")
        choice = swing.choose_recipe({"rf_daily_v1": selection["rf_daily_v1"], current: current_report,
                                       "rf_calibrated63": quarterly_report})
        if choice == "rf_calibrated63":
            chosen_model, exit_policy = choice, "trend200"
    chosen = chosen_model if exit_policy is None else f"{chosen_model}-{exit_policy}"
    selection_path = args.out / "selection.json"
    selection_path.write_text(json.dumps({"chosen": chosen, "chosen_model": chosen_model,
                                         "exit_policy": exit_policy, "reports": selection,
                                         "exit_policy_reports": policy_reports, "calibrated_reports": calibrated_reports,
                                         "quarterly_report": quarterly_report,
                                         "fits": selection_fits, "environment": environment,
                                         "inputs_sha256": inputs, "code_sha256": code}, indent=2), encoding="utf-8")
    print(f"Frozen selection: {chosen}; SHA256={sha256(selection_path)}", flush=True)
    final, final_fits, chosen_orders = {}, [], None
    for name in dict.fromkeys(("rf_daily_v1", chosen_model)):
        predictions, audit = swing.walk_forward_predict(features, name, "2018-01-01", "2026-11-11")
        predictions.to_csv(args.out / f"test-{name}-predictions.csv", index=False)
        final_fits.extend(audit)
        orders = (swing.filtered_orders(predictions, features, exit_policy)
                  if exit_policy is not None and name == chosen_model else probability_orders(predictions))
        result_name = chosen if name == chosen_model else name
        final[result_name] = evaluate(f"test-{result_name}", orders, "2018-01-01", "2026-11-11")
        if name == chosen_model:
            chosen_orders = orders
    sensitivity = evaluate(f"test-{chosen}-higher-costs", chosen_orders, "2018-01-01", "2026-11-11", cost=25)
    summary = {"protocol": "long-horizon-20261111-v4", "chosen": chosen,
        "chosen_model": chosen_model, "exit_policy": exit_policy,
        "selection_sha256": sha256(selection_path), "inputs_sha256": inputs, "code_sha256": code, "environment": environment,
        "test": final, "higher_costs": sensitivity, "fits": final_fits,
        "costs_bps_per_side": {"fee": 5, "slippage": 5},
        "higher_costs_bps_per_side": {"fee": 25, "slippage": 25},
        "limitations": "Exploratory surviving-five-stock history; no stock dividend cash; SPY adjusted proxy; no live promotion."}
    (args.out / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
