"""Replay frozen long-study decisions with verified cash dividend events; no fitting."""

import argparse
import json
from pathlib import Path

import numpy as np
import pandas as pd

from stocklab_ml import research, swing_research as swing
from swing_research import SYMBOLS, load_snapshot, probability_orders, sha256


def load_dividends(root: Path) -> pd.DataFrame:
    manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    path = root / "dividends.csv"
    if (manifest.get("schema_version") != 1 or manifest.get("coverage_start") != "2018-01-01"
            or manifest.get("coverage_end") != "2026-11-11" or manifest.get("currency") != "USD"
            or manifest.get("amount_basis") != "current_split_adjusted" or manifest.get("symbols") != SYMBOLS
            or manifest.get("rows") != 176 or manifest.get("files", {}).get("dividends.csv") != sha256(path)):
        raise ValueError("Incomplete or incompatible cash dividend snapshot.")
    events = pd.read_csv(path, parse_dates=["ex_date", "pay_date"], float_precision="round_trip",
                         dtype={"symbol": "string", "amount": "float64"}).astype({
                             "ex_date": "datetime64[ns]", "pay_date": "datetime64[ns]"})
    if (list(events.columns) != ["symbol", "ex_date", "pay_date", "amount"] or len(events) != 176
            or set(events.symbol) != set(SYMBOLS) or not events.ex_date.between("2018-01-01", "2026-11-11").all()):
        raise ValueError("Cash-event coverage differs from its frozen manifest.")
    for symbol in SYMBOLS:
        for year in range(2018, 2027):
            observed = events.loc[events.symbol.eq(symbol) & events.ex_date.dt.year.eq(year), "ex_date"].dt.quarter.tolist()
            expected = [1, 2, 3, 4] if year < 2026 or symbol == "JPM" else [1, 2, 3]
            if sorted(observed) != expected:
                raise ValueError("A required quarterly dividend is missing or duplicated.")
    return events


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--study-dir", type=Path, required=True)
    parser.add_argument("--data-dir", type=Path, required=True)
    parser.add_argument("--dividend-dir", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    parent = json.loads((args.study_dir / "summary.json").read_text(encoding="utf-8"))
    model, policy = parent["chosen_model"], parent["exit_policy"]
    if (parent["protocol"] not in ("long-horizon-20261111-v3", "long-horizon-20261111-v4")
            or model not in swing.RECIPES | swing.CALIBRATED_RECIPES
            or policy not in (None, "trend50", "persistent5", "trend200")
            or sha256(args.study_dir / "selection.json") != parent["selection_sha256"]):
        raise ValueError("A compatible, frozen selection is required.")
    if set(parent["inputs_sha256"]) != {"stocks.csv", "monthly.csv", "spy.csv", "manifest.json"}:
        raise ValueError("All original price sources must remain bound to the selection.")
    for name, digest in parent["inputs_sha256"].items():
        if name not in ("stocks.csv", "monthly.csv", "spy.csv", "manifest.json") or sha256(args.data_dir / name) != digest:
            raise ValueError("Price evidence changed after original selection.")
    frames = load_snapshot(args.data_dir)
    prices, monthly, spy = (frames[name] for name in ("stocks.csv", "monthly.csv", "spy.csv"))
    features = swing.swing_features(prices, monthly, spy)
    expected = features.loc[features.date.between("2018-01-01", "2026-11-11"), ["date", "symbol"]].reset_index(drop=True)
    events = load_dividends(args.dividend_dir)
    orders, prediction_hashes = {}, {}
    for name in dict.fromkeys(("rf_daily_v1", model)):
        path = args.study_dir / f"test-{name}-predictions.csv"
        predictions = pd.read_csv(path, parse_dates=["date"], float_precision="round_trip",
            dtype={"symbol": "string", "probability_up": "float64"}).astype({"date": "datetime64[ns]"})
        if (list(predictions.columns) != ["date", "symbol", "probability_up"]
                or not predictions[["date", "symbol"]].equals(expected)
                or not np.isfinite(predictions.probability_up).all() or not predictions.probability_up.between(0, 1).all()):
            raise ValueError("Frozen predictions must cover the exact finite same-date universe.")
        prediction_hashes[path.name] = sha256(path)
        orders[name] = (swing.filtered_orders(predictions, features, policy)
                        if name == model and policy is not None else probability_orders(predictions))
    args.out.mkdir(parents=True, exist_ok=False)
    commitment = {"parent_summary_sha256": sha256(args.study_dir / "summary.json"),
        "parent_selection_sha256": parent["selection_sha256"], "prediction_sha256": prediction_hashes,
        "price_inputs_sha256": parent["inputs_sha256"], "dividends_sha256": sha256(args.dividend_dir / "dividends.csv"),
        "dividend_manifest_sha256": sha256(args.dividend_dir / "manifest.json"),
        "code_sha256": {str(path): sha256(path) for path in [Path(__file__), Path(research.__file__), Path(swing.__file__)]}}
    (args.out / "inputs.json").write_text(json.dumps(commitment, indent=2), encoding="utf-8")
    results = {}
    for label, name, cost in [("baseline", "rf_daily_v1", 5), ("selected", model, 5), ("selected_higher_costs", model, 25)]:
        curve, trades = research.simulate(prices, orders[name], "2018-01-01", "2026-11-11",
            fee_bps=cost, slippage_bps=cost, dividends=events)
        benchmark = research.spy_curve(spy, curve.date, fee_bps=cost, slippage_bps=cost)
        result = research.assess(curve, benchmark)
        result.update(buys=int(trades.action.eq("BUY").sum()), sells=int(trades.action.eq("SELL").sum()),
            dividends_earned=float(curve.dividends_earned.iloc[-1]), dividends_paid=float(curve.dividends_paid.iloc[-1]),
            dividends_receivable=float(curve.dividends_receivable.iloc[-1]), final_cash=float(curve.cash.iloc[-1]),
            calendar_returns=swing.calendar_returns(curve))
        curve.to_csv(args.out / f"{label}-equity.csv", index=False)
        trades.to_csv(args.out / f"{label}-trades.csv", index=False)
        benchmark.to_csv(args.out / f"{label}-spy.csv", index=False)
        results[label] = result
        print(f"{label}: CAGR={result['annualized_return']:.6%}, DD={result['max_drawdown']:.6%}, buys={result['buys']}, sells={result['sells']}, target={result['target_met']}", flush=True)
    (args.out / "summary.json").write_text(json.dumps({"protocol": "frozen-cash-dividend-replay-v1",
        "chosen": parent["chosen"], "inputs_sha256": sha256(args.out / "inputs.json"), "results": results,
        "decision_policy": "Unchanged frozen predictions/thresholds/filter; no fitting or post-result reselection.",
        "cash_timing": "Entitlement before ex-date orders; spendable strictly after actual pay date; no taxes."}, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
