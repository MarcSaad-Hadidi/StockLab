"""Two fixed protective-exit policies on frozen forecasts; never live promotion."""

import argparse
import json
from pathlib import Path
import sys

from stocklab_ml import research, swing_research as swing
from dividend_replay import load_replay_inputs
from swing_research import sha256

POLICIES = ("trailing15", "trailing15_spy200")


def choose_policy(reports: dict[str, dict]) -> str | None:
    """Frozen selection rule: lower DD and genuine risk sales, then greatest CAGR."""
    eligible = [name for name in POLICIES if reports[name]["max_drawdown"] < reports["baseline"]["max_drawdown"]
                and reports[name]["protective_sells"] > 0]
    return max(eligible, key=lambda name: reports[name]["annualized_return"]) if eligible else None


def validate_frozen_replay(root: Path, commitment: dict) -> None:
    """Bind forecasts and cash events to the already-completed dividend replay."""
    summary = json.loads((root / "summary.json").read_text(encoding="utf-8"))
    path = root / "inputs.json"
    previous = json.loads(path.read_text(encoding="utf-8"))
    if (summary.get("protocol") != "frozen-cash-dividend-replay-v1"
            or summary.get("inputs_sha256") != sha256(path)
            or any(previous.get(key) != value for key, value in commitment.items() if key != "code_sha256")):
        raise ValueError("Frozen prediction, price or dividend evidence changed since its cash replay.")


def evaluate_replay(prices, spy, events, orders, out: Path, label: str, policy: str | None,
                    start: str, end: str, cost: int = 5) -> dict:
    """Costed shared-cash evaluation and evidence exports; no selection or fitting."""
    decisions = orders.loc[orders.date.between(start, end)].copy()
    curve, trades = research.simulate(prices, decisions, start, end,
        dividends=events, risk_policy=policy, spy=spy, fee_bps=cost, slippage_bps=cost)
    benchmark = research.spy_curve(spy, curve.date, fee_bps=cost, slippage_bps=cost)
    report = research.assess(curve, benchmark)
    risk_sells = trades.loc[trades.action.eq("SELL") & trades.reason.isin(
        ["trailing_stop", "spy_regime"])] if "reason" in trades else trades.iloc[:0]
    report.update(buys=int(trades.action.eq("BUY").sum()), sells=int(trades.action.eq("SELL").sum()),
        protective_sells=len(risk_sells), exit_reasons=risk_sells.reason.value_counts().to_dict() if len(risk_sells) else {},
        fees=float(trades.fee.sum()), dividends_earned=float(curve.dividends_earned.iloc[-1]),
        dividends_paid=float(curve.dividends_paid.iloc[-1]), dividends_receivable=float(curve.dividends_receivable.iloc[-1]),
        final_cash=float(curve.cash.iloc[-1]), calendar_returns=swing.calendar_returns(curve),
        benchmark_calendar_returns=swing.calendar_returns(benchmark))
    curve.to_csv(out / f"{label}-equity.csv", index=False)
    trades.to_csv(out / f"{label}-trades.csv", index=False)
    benchmark.to_csv(out / f"{label}-spy.csv", index=False)
    print(f"{label}: CAGR={report['annualized_return']:.6%}, DD={report['max_drawdown']:.6%}, "
          f"buys={report['buys']}, sells={report['sells']}, risk_sells={len(risk_sells)}", flush=True)
    return report


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("study-dir", "data-dir", "dividend-dir", "replay-dir", "out"):
        parser.add_argument(f"--{name}", type=Path, required=True)
    args = parser.parse_args()
    parent, prices, spy, events, orders, commitment = load_replay_inputs(
        args.study_dir, args.data_dir, args.dividend_dir)
    if parent["chosen_model"] != "rf_calibrated20" or parent["exit_policy"] != "trend200":
        raise ValueError("This fixed experiment requires calibrated20/trend200 forecasts.")
    validate_frozen_replay(args.replay_dir, commitment)
    plan = Path(__file__).resolve().parents[2] / "docs/superpowers/plans/2026-11-12-ml-risk-exits.md"
    for path in (Path(__file__), Path(sys.modules["swing_research"].__file__)):
        commitment["code_sha256"][str(path)] = sha256(path)
    commitment.update(protocol="protective-exits-20261112-v1", policies=list(POLICIES),
        prior_replay_summary_sha256=sha256(args.replay_dir / "summary.json"),
        prior_replay_inputs_sha256=sha256(args.replay_dir / "inputs.json"))
    args.out.mkdir(parents=True, exist_ok=False)
    (args.out / "protocol-plan.md").write_bytes(plan.read_bytes())
    commitment["plan_sha256"] = sha256(args.out / "protocol-plan.md")
    (args.out / "inputs.json").write_text(json.dumps(commitment, indent=2), encoding="utf-8")
    frozen_orders = orders[parent["chosen_model"]]

    def evaluate(label: str, policy: str | None, start: str, end: str, cost: int = 5) -> dict:
        return evaluate_replay(prices, spy, events, frozen_orders, args.out, label, policy, start, end, cost)

    selection = {"baseline": evaluate("selection-baseline", None, "2018-01-01", "2020-12-31")}
    for policy in POLICIES:
        selection[policy] = evaluate(f"selection-{policy}", policy, "2018-01-01", "2020-12-31")
    chosen = choose_policy(selection)
    selection_path = args.out / "selection.json"
    selection_path.write_text(json.dumps({"chosen_policy": chosen, "reports": selection,
        "inputs_sha256": sha256(args.out / "inputs.json")}, indent=2), encoding="utf-8")
    selection_hash = sha256(selection_path)
    print(f"Frozen risk selection: {chosen}; SHA256={selection_hash}", flush=True)
    results = {}
    for label, start, end in [("later", "2021-01-01", "2026-11-11"), ("full", "2018-01-01", "2026-11-11")]:
        baseline = evaluate(f"{label}-baseline", None, start, end)
        selected = evaluate(f"{label}-selected", chosen, start, end) if chosen else baseline.copy()
        selected["all_objectives_met"] = bool(selected["target_met"] and selected["protective_sells"] > 0
                                               and selected["max_drawdown"] < baseline["max_drawdown"])
        results[label] = {"baseline": baseline, "selected": selected,
            "selected_higher_costs": evaluate(f"{label}-higher-costs", chosen, start, end, cost=25)}
    summary = {"protocol": "protective-exits-20261112-v1", "chosen_policy": chosen,
        "selection_sha256": selection_hash, "inputs_sha256": sha256(args.out / "inputs.json"), "results": results,
        "costs_bps_per_side": {"fee": 5, "slippage": 5},
        "higher_costs_bps_per_side": {"fee": 25, "slippage": 25},
        "all_objectives_met": all(results[period]["selected"]["all_objectives_met"] for period in results),
        "limitations": "Adaptive five-surviving-stock historical research, not a pristine holdout; no taxes or final liquidation; no live promotion."}
    (args.out / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
