"""Reproduce the fixed offline experiment documented in ml/docs/dca-research.md."""

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
import pandas as pd

from stocklab_ml import research

SYMBOLS = ("AAPL", "JNJ", "JPM", "MSFT", "XOM")


def read_prices(path: Path) -> pd.DataFrame:
    return pd.read_csv(path, parse_dates=["date"], dtype={"symbol": "string", "volume": "Int64",
        **{column: "float64" for column in ("open", "high", "low", "close")}}).astype({"date": "datetime64[ns]"})


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_source(path: Path, symbol: str, interval: str, adjustment: str) -> pd.DataFrame:
    manifest = json.loads((path.parent / "manifest.json").read_text(encoding="utf-8"))
    params = manifest.get("params", {})
    if (any(params.get(key) != value for key, value in {
            "symbol": symbol, "interval": interval, "adjust": adjustment}.items())
            or manifest.get("files", {}).get(path.name) != sha256(path)):
        raise ValueError("Source adjustment, interval, symbol or CSV hash differs from its manifest.")
    prices = read_prices(path)
    if set(prices.symbol) != {symbol}:
        raise ValueError("Source CSV symbol differs from its manifest.")
    return prices


def read_daily_snapshot(path: Path) -> pd.DataFrame:
    manifest = json.loads((path.parent / "manifest.json").read_text(encoding="utf-8"))
    if (manifest.get("schema_version") != 1 or manifest.get("decision_date") != "2026-11-11"
            or manifest.get("history_start") != "2021-01-01" or manifest.get("symbols") != list(SYMBOLS)
            or manifest.get("files", {}).get(path.name) != sha256(path)):
        raise ValueError("Daily cycle snapshot lineage or CSV hash differs from the frozen protocol.")
    daily = read_prices(path)
    if tuple(sorted(daily.symbol.unique())) != SYMBOLS or daily.date.max() != pd.Timestamp("2026-11-11"):
        raise ValueError("This frozen protocol requires the five named symbols through2026-11-11.")
    return daily


def model_decisions(predictions: pd.DataFrame) -> pd.DataFrame:
    frame = predictions[["date", "symbol"]].copy()
    p = predictions.probability_up
    frame["action"] = np.select([p >= .65, p <= .35], ["BUY", "SELL"], default="HOLD")
    frame["allocation"] = 1.
    return frame.loc[frame.action.ne("HOLD")].reset_index(drop=True)


def rule_decisions(features: pd.DataFrame, start: str, end: str, *, dca: bool) -> pd.DataFrame:
    rows = features.loc[features.date.between(start, end)].copy()
    rows["month"] = rows.date.dt.to_period("M")
    rows = rows.drop_duplicates(["symbol", "month"])
    rows["action"] = "BUY"
    rows["allocation"] = (rows.monthly_multiplier if dca else 1.) / (12 * len(SYMBOLS))
    return rows.loc[rows.allocation.gt(0), ["date", "symbol", "action", "allocation"]].reset_index(drop=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--daily", type=Path, required=True, help="Five-symbol split-adjusted cleaned CSV")
    parser.add_argument("--monthly-dir", type=Path, required=True, help="Contains SYMBOL-1month-splits/cleaned.csv")
    parser.add_argument("--spy", type=Path, required=True, help="SPY daily adjust=all cleaned CSV")
    parser.add_argument("--out", type=Path, required=True, help="New output directory; never overwritten")
    args = parser.parse_args()
    monthly_paths = [args.monthly_dir / f"{symbol}-1month-splits" / "cleaned.csv" for symbol in SYMBOLS]
    daily = read_daily_snapshot(args.daily)
    monthly = pd.concat([read_source(path, symbol, "1month", "splits")
                         for symbol, path in zip(SYMBOLS, monthly_paths)], ignore_index=True)
    spy = read_source(args.spy, "SPY", "1day", "all")
    features = research.research_features(daily, monthly)
    args.out.mkdir(parents=True, exist_ok=False)
    inputs = {str(path.resolve()): sha256(path) for path in [args.daily, args.spy, *monthly_paths,
              Path(__file__), Path(research.__file__)]}
    reports, fits = {}, []

    def evaluate(name: str, orders: pd.DataFrame, start: str, end: str) -> dict:
        curve, trades = research.simulate(daily, orders, start, end)
        benchmark = research.spy_curve(spy, curve.date)
        result = research.assess(curve, benchmark)
        result.update(trades=len(trades), buys=int(trades.action.eq("BUY").sum()),
                      sells=int(trades.action.eq("SELL").sum()), final_cash=float(curve.cash.iloc[-1]),
                      fees=float(trades.fee.sum()), final_value=float(curve.total_value.iloc[-1]))
        curve.to_csv(args.out / f"{name}-equity.csv", index=False)
        trades.to_csv(args.out / f"{name}-trades.csv", index=False)
        benchmark.to_csv(args.out / f"{name}-spy.csv", index=False)
        print(f"{name}: CAGR={result['annualized_return']:.6%}, DD={result['max_drawdown']:.6%}, trades={len(trades)}", flush=True)
        return result

    for name in research.CANDIDATES:
        predictions, audit = research.fit_predict(features, name, "2024-01-01", "2024-12-31")
        predictions.to_csv(args.out / f"selection-{name}-predictions.csv", index=False)
        fits.extend(audit)
        reports[name] = evaluate(f"selection-{name}", model_decisions(predictions), "2024-01-01", "2024-12-31")
    selected = research.select_candidate(reports)
    selection_path = args.out / "selection.json"
    selection_path.write_text(json.dumps({"selected": selected, "reports": reports,
        "fit_boundaries": fits, "inputs_sha256": inputs}, indent=2), encoding="utf-8")
    print(f"Frozen selection: {selected}; SHA256={sha256(selection_path)}", flush=True)
    final, final_fits = {}, []
    for name in dict.fromkeys(("rf_base", selected)):
        predictions, audit = research.fit_predict(features, name, "2025-01-01", "2026-11-11")
        predictions.to_csv(args.out / f"test-{name}-predictions.csv", index=False)
        final_fits.extend(audit)
        final[name] = evaluate(f"test-{name}", model_decisions(predictions), "2025-01-01", "2026-11-11")
    for name, dca in (("regular_monthly_dca", False), ("range_rsi_monthly_dca", True)):
        final[name] = evaluate(f"test-{name}", rule_decisions(features, "2025-01-01", "2026-11-11", dca=dca),
                               "2025-01-01", "2026-11-11")
    summary = {"protocol": "dca-research-20261111-v1", "selected": selected,
               "selection_sha256": sha256(selection_path), "inputs_sha256": inputs,
               "test": final, "fit_boundaries": final_fits,
               "costs_bps_per_side": {"fee": 5, "slippage": 5},
               "limits": "Exploratory historical study; five surviving stocks; stocks exclude dividends; SPY provider-adjusted proxy includes dividends; no live promotion."}
    (args.out / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
