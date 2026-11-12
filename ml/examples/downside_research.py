"""One dedicated downside head, frozen entry forecasts and a paired control."""

import argparse
import hashlib
from importlib.metadata import version
import json
import os
from pathlib import Path
import platform
import re
import sys

import numpy as np
import pandas as pd
from sklearn.metrics import brier_score_loss, roc_auc_score

from stocklab_ml import downside_research as downside, research, swing_research as swing, training_panel
from stocklab_ml.data.validation import SYMBOL_PATTERN
from dividend_replay import load_replay_inputs
from risk_exit_replay import evaluate_replay
from swing_research import SYMBOLS, load_snapshot, sha256

WORKSPACE = Path(__file__).resolve().parents[2]
SIM_COLUMNS = ["date", "symbol", "action", "allocation"]
TRAINING_COLUMNS = ["date", "symbol"] + downside.DOWNSIDE_FEATURES + [
    "target_20", "target_date_20", downside.LABEL, downside.KNOWN_DATE]
PANEL_FEATURE_SOURCES = [f"ml/src/stocklab_ml/{name}.py" for name in (
    "research", "swing_research", "downside_research", "training_panel", "features/engineering", "features/models")]


def load_training_panel(root: Path, frame: pd.DataFrame, spy: pd.DataFrame, spy_sha256: str) -> tuple[pd.DataFrame, dict]:
    """Verify a completed retrospective training sample before any model fit."""
    root = root.resolve()
    acquisition = root.parent
    manifest_path, csv_path = root / "manifest.json", root / "training.csv"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    expected = {"schema_version": 1, "protocol": "broader-training-20261112-v1", "role": "training_only",
        "price_basis": "splits", "monthly_provenance": "daily_derived_complete_months", "sample_count": 256,
        "columns": TRAINING_COLUMNS, "spy_sha256": spy_sha256}
    if (any(manifest.get(key) != value for key, value in expected.items())
            or manifest.get("training_sha256") != sha256(csv_path)):
        raise ValueError("Incompatible or changed training-only panel manifest/CSV.")
    sources = manifest.get("feature_code_sha256", {})
    if (set(sources) != set(PANEL_FEATURE_SOURCES)
            or any(sha256(WORKSPACE / name) != digest for name, digest in sources.items())):
        raise ValueError("Training panel feature sources differ from the current implementation.")
    files = manifest.get("root_files", {})
    prefix = root.name + "/"
    required = {"selection.json", "protocol-plan.md", "probe-results.json", "acquisition-progress.json",
                "acquisition-results.json", "compile-panel.py", prefix + "attrition.csv", prefix + "build-protocol.md",
                prefix + "environment.json"}
    required.update(prefix + "source-snapshot/" + name for name in PANEL_FEATURE_SOURCES)
    if not isinstance(files, dict) or not required.issubset(files):
        raise ValueError("Training panel is missing required acquisition/build evidence.")
    for relative, digest in files.items():
        if not isinstance(relative, str) or Path(relative).is_absolute():
            raise ValueError("Training panel evidence paths must be relative to the acquisition root.")
        path = (acquisition / relative).resolve()
        if not path.is_relative_to(acquisition) or not path.is_file() or sha256(path) != digest:
            raise ValueError("Training panel evidence path escapes its root or its hash changed.")
    if (sum(name.startswith(prefix + "reports/") and name.endswith(".json") for name in files) != 256
            or any(files[prefix + "source-snapshot/" + name] != digest for name, digest in sources.items())):
        raise ValueError("Training panel needs all sampled reports and matching archived feature sources.")
    environment = {"python": platform.python_version()} | {name: version(name) for name in ("numpy", "pandas", "scikit-learn")}
    if json.loads((root / "environment.json").read_text(encoding="utf-8")) != environment:
        raise ValueError("Training panel feature-generator environment differs from the current runtime.")
    selection = json.loads((acquisition / "selection.json").read_text(encoding="utf-8"))
    progress = json.loads((acquisition / "acquisition-progress.json").read_text(encoding="utf-8"))
    outcomes = json.loads((acquisition / "acquisition-results.json").read_text(encoding="utf-8"))
    frozen = {name: files[name] for name in ("selection.json", "protocol-plan.md", "probe-results.json")}
    frozen["spy.csv"] = spy_sha256
    sample = selection.get("sample", [])
    if (selection.get("protocol") != expected["protocol"]
            or selection.get("salt") != "stocklab-broad-training-20261112-v1|"
            or selection.get("start") != "2013-04-01" or selection.get("end") != "2026-11-11"
            or selection.get("protocol_sha256") != files["protocol-plan.md"]
            or len(sample) != 256 or [record.get("sample_index") for record in sample] != list(range(1, 257))
            or progress.get("status") != "complete" or progress.get("terminal_outcomes") != 256
            or progress.get("pending_indices") != [] or progress.get("frozen_inputs_sha256") != frozen
            or progress.get("outcomes_sha256") != files["acquisition-results.json"]
            or not isinstance(outcomes, list) or len(outcomes) != 256
            or [item.get("sample_index") for item in outcomes] != list(range(1, 257))):
        raise ValueError("Training panel requires the unchanged frozen sample and all 256 terminal outcomes.")
    identities, ranks, observed = set(), [], set()
    source_manifests = {}
    for record, outcome in zip(sample, outcomes):
        symbol, mic = record.get("symbol", ""), record.get("mic_code", "")
        identity = mic + "|" + symbol
        rank = hashlib.sha256((selection["salt"] + identity).encode()).hexdigest()
        if (not re.fullmatch(SYMBOL_PATTERN, symbol + ":" + record.get("exchange", ""))
                or not re.fullmatch(r"[A-Z0-9]{4}", mic) or record.get("exchange") not in ("NYSE", "NASDAQ")
                or symbol in SYMBOLS or record.get("currency") != "USD" or record.get("type") != "Common Stock"
                or record.get("access", {}).get("plan") != "Basic" or record.get("rank_sha256") != rank
                or identity in identities or type(record.get("catalog_active")) is not bool):
            raise ValueError("Invalid, duplicate or evaluation identity in the frozen training sample.")
        identities.add(identity)
        ranks.append((rank, mic, symbol))
        folder = "sources/" + hashlib.sha256(identity.encode()).hexdigest()[:16] + "/"
        source = folder + "manifest.json"
        params = {"symbol": symbol, "interval": "1day", "start_date": "2013-04-01",
            "end_date": "2026-11-11T23:59:59", "order": "asc", "format": "JSON", "adjust": "splits",
            "exchange": record["exchange"], "outputsize": "5000", "mic_code": mic, "country": "US", "type": "Common Stock"}
        if (source not in files or outcome.get("identity") != identity or outcome.get("params") != params
                or outcome.get("catalog_active") != record["catalog_active"]
                or outcome.get("status") not in ("observed_history", "rejected_response", "quarantined_identity_metadata")
                or json.loads((acquisition / source).read_text(encoding="utf-8")) != outcome):
            raise ValueError("A sampled training outcome differs from its bound source manifest or request.")
        source_manifests[source] = files[source]
        for key, filename in (("raw_sha256", "raw.json"), ("cleaned_sha256", "cleaned.csv")):
            name = folder + filename
            if (key in outcome and files.get(name) != outcome[key]) or ((acquisition / name).exists() and name not in files):
                raise ValueError("A training response/cleaned source is missing its hash binding.")
        if outcome["status"] == "observed_history":
            if not {"raw_sha256", "cleaned_sha256"}.issubset(outcome):
                raise ValueError("An observed training identity needs both raw and cleaned source hashes.")
            training_panel.validate_identity(record, outcome.get("response_metadata"))
            observed.add(symbol + ":" + mic)
    probe_ids = [record["sample_index"] for active in (True, False)
                 for record in [r for r in sample if r["catalog_active"] == active][:3]]
    if (ranks != sorted(ranks) or progress.get("manifest_sha256") != source_manifests
            or selection.get("probe_ids") != probe_ids
            or json.loads((acquisition / "probe-results.json").read_text(encoding="utf-8")) != [outcomes[i - 1] for i in probe_ids]):
        raise ValueError("Training sample ordering, original probes or source completion changed.")
    dates = ["date", "target_date_20", downside.KNOWN_DATE]
    added = pd.read_csv(csv_path, parse_dates=dates, float_precision="round_trip",
        dtype={"symbol": "string"} | {name: "float64" for name in downside.DOWNSIDE_FEATURES + ["target_20", downside.LABEL]})
    added = added.astype({name: "datetime64[ns]" for name in dates})
    if (list(added) != TRAINING_COLUMNS or type(manifest.get("rows")) is not int or len(added) != manifest["rows"]
            or added[["date", "symbol"]].isna().any().any() or added.duplicated(["date", "symbol"]).any()
            or not set(added.symbol).issubset(observed) or not added.date.eq(added.date.dt.normalize()).all()
            or not np.isfinite(added[downside.DOWNSIDE_FEATURES].to_numpy()).all()
            or not added.equals(added.sort_values(["date", "symbol"]).reset_index(drop=True))):
        raise ValueError("Training CSV must contain canonical finite chronological added-only feature rows.")
    calendar = pd.DatetimeIndex(spy.date)
    if (set(spy.symbol) != {"SPY"} or calendar.hasnans or calendar.has_duplicates or not calendar.is_monotonic_increasing
            or not calendar.equals(calendar.normalize()) or set(frame.symbol) != set(SYMBOLS)):
        raise ValueError("Training requires the original five-stock evaluation and verified SPY calendar.")
    positions = calendar.get_indexer(added.date)
    if (positions < 0).any():
        raise ValueError("A training feature date is absent from the SPY calendar.")
    for label, known, horizon in (("target_20", "target_date_20", 21), (downside.LABEL, downside.KNOWN_DATE, 20)):
        complete = added[label].notna()
        endpoints = calendar.get_indexer(added.loc[complete, known])
        if (not complete.eq(added[known].notna()).all() or not added.loc[complete, label].isin([0., 1.]).all()
                or (endpoints < 0).any() or not np.array_equal(endpoints - positions[complete], np.full(complete.sum(), horizon))):
            raise ValueError("Known training labels need joint binary/date values and exact full-session horizons.")
    pre2018 = added.loc[added.target_date_20.lt(pd.Timestamp("2018-01-01"))
        & added[downside.KNOWN_DATE].lt(pd.Timestamp("2018-01-01")), "symbol"].nunique()
    if pre2018 < 20:
        raise ValueError("At least 20 added identities need both fully known pre2018 targets before fitting.")
    combined = pd.concat([frame[TRAINING_COLUMNS], added], ignore_index=True).sort_values(["date", "symbol"]).reset_index(drop=True)
    return combined, {"role": "training_only", "manifest_sha256": sha256(manifest_path),
        "training_sha256": manifest["training_sha256"], "spy_sha256": spy_sha256,
        "root_files": files, "feature_code_sha256": sources, "rows": len(added),
        "added_identities": int(added.symbol.nunique()), "pre2018_identities": int(pre2018)}


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


def forecast_report(frame: pd.DataFrame, predictions: pd.DataFrame, audits: list[dict], end: str, *,
                    refit_frequency: str = "annual") -> dict:
    if refit_frequency not in ("annual", "monthly"):
        raise ValueError("Refit frequency must be annual or monthly.")
    rows = predictions.merge(frame[["date", "symbol", downside.LABEL, downside.KNOWN_DATE]],
                             on=["date", "symbol"], how="left", validate="one_to_one")
    rows = rows.loc[rows[downside.KNOWN_DATE].le(pd.Timestamp(end)) & rows[downside.LABEL].notna()]
    rates = {}
    monthly = refit_frequency == "monthly"
    first_sessions = predictions.groupby(predictions.date.dt.to_period("M")).date.min().to_dict() if monthly else {}
    for audit in audits:
        first = pd.Timestamp(audit["first_prediction"])
        period = first.to_period("M") if monthly else first.year
        rate = audit["training_positive_rate"]
        if period in rates or not 0 < rate < 1 or not pd.Timestamp(audit["last_target"]) < first:
            raise ValueError("Forecast references need one causal training prevalence per fit.")
        if monthly and first != first_sessions.get(period):
            raise ValueError("Monthly reference must start at its actual first prediction session.")
        rates[period] = rate
    if monthly and set(rates) != set(first_sessions):
        raise ValueError("Every prediction month needs its own fitted reference.")
    periods = rows.date.dt.to_period("M") if monthly else rows.date.dt.year
    reference = periods.map(rates)
    if rows.empty or reference.isna().any():
        raise ValueError("Observed risk labels and causal reference forecasts are required.")
    labels, scores = rows[downside.LABEL], rows.probability_downside
    return {"rows": len(rows), "auc": float(roc_auc_score(labels, scores)) if labels.nunique() == 2 else None,
        "brier": float(brier_score_loss(labels, scores)),
        "reference_brier": float(brier_score_loss(labels, reference)),
        "actual_positive_rate": float(labels.mean()), "mean_score": float(scores.mean())}


def admissions(control: dict, dual: dict, forecast: dict, thresholds: dict, *, strict_target: bool = False) -> dict:
    dual_return_gate = (dual["annualized_return"] >= .20
        and dual["annualized_return"] > dual["benchmark_annualized_return"] and dual["sells"] > 0
        if strict_target else dual["annualized_return"] > thresholds["protected_cagr"])
    return {"control": bool(control["annualized_return"] >= .20
                and control["annualized_return"] > control["benchmark_annualized_return"]
                and control["max_drawdown"] < thresholds["original_drawdown"] and control["sells"] > 0),
        "dual": bool(dual["annualized_return"] >= control["annualized_return"]
            and dual["max_drawdown"] < control["max_drawdown"] and dual["downside_sells"] > 0
            and forecast["auc"] is not None and forecast["auc"] > .5
            and forecast["brier"] < forecast["reference_brier"]
            and dual_return_gate
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
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--monthly", action="store_true", help="Refit both heads monthly; annual remains the default.")
    modes.add_argument("--training-panel", type=Path, help="Verified broader training-only panel; both heads refit annually.")
    parser.add_argument("--boosted", action="store_true", help="Use both fixed boosted heads with --training-panel.")
    args = parser.parse_args()
    if args.boosted and (args.training_panel is None or args.monthly):
        parser.error("--boosted requires --training-panel and cannot be combined with --monthly.")
    frequency = {"refit_frequency": "monthly"} if args.monthly else {}
    broader = args.training_panel is not None
    refit_upside = args.monthly or broader
    protocol_name = ("broader-boosted-model-20261112-v1" if args.boosted else
                     "broader-training-model-20261112-v1" if broader else
                     "monthly-dual-head-20261112-v1" if args.monthly else "dedicated-downside-20261112-v1")
    _, prices, spy, events, _, commitment = load_replay_inputs(args.study_dir, args.data_dir, args.dividend_dir)
    frames = load_snapshot(args.data_dir)
    features = swing.swing_features(frames["stocks.csv"], frames["monthly.csv"], frames["spy.csv"])
    up, thresholds, entry_hashes = load_entry(args.entry_dir, features, commitment)
    frame = downside.make_frame(features, prices, spy)
    fit_options = frequency.copy()
    if broader:
        combined, panel_commitment = load_training_panel(args.training_panel, frame, frames["spy.csv"],
                                                        commitment["price_inputs_sha256"]["spy.csv"])
        fit_options.update(training_frame=combined, date_weighted=True)
    args.out.mkdir(parents=True, exist_ok=False)
    protocol = WORKSPACE / ("docs/superpowers/plans/2026-11-12-broader-boosted.md" if args.boosted else
        "docs/superpowers/plans/2026-11-12-broader-training.md" if broader else
        "docs/superpowers/plans/2026-11-12-monthly-ml.md" if args.monthly else "docs/superpowers/plans/2026-11-12-downside-ml.md")
    (args.out / "protocol-plan.md").write_bytes(protocol.read_bytes())
    paths = [Path(__file__), Path(downside.__file__), Path(research.__file__), Path(swing.__file__)]
    paths += [Path(sys.modules[name].__file__) for name in ("dividend_replay", "risk_exit_replay", "swing_research")]
    if broader:
        paths += [WORKSPACE / name for name in PANEL_FEATURE_SOURCES]
        paths.append(WORKSPACE / "ml/src/stocklab_ml/modeling/random_forest.py")
    for path in paths:
        archived = args.out / "source-snapshot" / path.resolve().relative_to(WORKSPACE)
        archived.parent.mkdir(parents=True, exist_ok=True)
        archived.write_bytes(path.read_bytes())
        commitment["code_sha256"][str(path)] = sha256(archived)
    commitment.update(protocol=protocol_name, entry_sha256=entry_hashes,
        plan_sha256=sha256(args.out / "protocol-plan.md"), thresholds=thresholds,
        environment={name: version(name) for name in ("numpy", "pandas", "scikit-learn")})
    if args.monthly:
        commitment.update(refit_frequency="monthly",
            entry_forecast_role="contextual provenance only; monthly decisions use refitted upside scores")
    elif broader:
        commitment.update(refit_frequency="annual", date_weighted=True, training_panel=panel_commitment,
            entry_forecast_role="contextual provenance only; broader decisions use refitted upside scores")
    if args.boosted:
        commitment.update(boosted=True, omp_num_threads=os.environ.get("OMP_NUM_THREADS"))
    (args.out / "inputs.json").write_text(json.dumps(commitment, indent=2), encoding="utf-8")

    def predict(label: str, start: str, end: str, *, upside: bool = False):
        if upside:
            predictions, audits = swing.walk_forward_predict(features,
                "boosted_swing20" if args.boosted else "rf_swing20", start, end, **fit_options)
        elif args.boosted:
            predictions, audits = swing._walk_forward_predict(frame, "boosted_price_downside20", start, end,
                "boosted", downside.LABEL, downside.KNOWN_DATE, downside.DOWNSIDE_FEATURES,
                "probability_downside", **fit_options)
        else:
            predictions, audits = downside.predict(frame, start, end, **fit_options)
        head = "upside" if upside else "downside"
        fit_name = f"{label}-upside-fits.json" if upside else f"{label}-fits.json"
        predictions.to_csv(args.out / f"{label}-{head}-predictions.csv", index=False)
        (args.out / fit_name).write_text(json.dumps(audits, indent=2), encoding="utf-8")
        return predictions, audits

    def evaluate(label: str, details: pd.DataFrame, start: str, end: str, cost: int = 5):
        details = details.loc[details.date.between(start, end)].copy()
        details.to_csv(args.out / f"{label}-decisions.csv", index=False)
        return evaluate_account(prices, spy, events, details, args.out, label, start, end, cost)

    down, fits = predict("selection", "2018-01-01", "2020-12-31")
    if refit_upside:
        selection_up, _ = predict("selection", "2018-01-01", "2020-12-31", upside=True)
    else:
        selection_up = up.loc[up.date.between("2018-01-01", "2020-12-31")]
    decisions = {"control": control_details(selection_up, features), "dual": downside.orders(selection_up, down, features)}
    for arm, details in decisions.items():
        details.to_csv(args.out / f"selection-{arm}-decisions.csv", index=False)
    forecast = forecast_report(frame, down, fits, "2020-12-31", **frequency)
    selection = {arm: evaluate(f"selection-{arm}", details, "2018-01-01", "2020-12-31") for arm, details in decisions.items()}
    admitted = admissions(selection["control"], selection["dual"], forecast, thresholds, strict_target=refit_upside)
    selected = "dual" if admitted["dual"] else "control" if admitted["control"] else None
    selection_path = args.out / "selection.json"
    selection_path.write_text(json.dumps({"admissions": admitted, "selected_arm": selected, "reports": selection,
        "forecast": forecast, "inputs_sha256": sha256(args.out / "inputs.json")}, indent=2), encoding="utf-8")
    print(f"Frozen admissions: {admitted}; selected={selected}; SHA256={sha256(selection_path)}", flush=True)
    results, forecasts = {}, {"selection": forecast}
    if selected:
        if refit_upside:
            later_up, _ = predict("later", "2021-01-01", "2026-11-11", upside=True)
            up = pd.concat([selection_up, later_up], ignore_index=True)
            up.to_csv(args.out / "full-upside-predictions.csv", index=False)
        controls = control_details(up, features)
        if selected == "dual":
            later, later_fits = predict("later", "2021-01-01", "2026-11-11")
            down = pd.concat([down, later], ignore_index=True)
            down.to_csv(args.out / "full-downside-predictions.csv", index=False)
            forecasts["later"] = forecast_report(frame, later, later_fits, "2026-11-11", **frequency)
            forecasts["full"] = forecast_report(frame, down, fits + later_fits, "2026-11-11", **frequency)
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
    summary = {"protocol": protocol_name, "selected_arm": selected, "admissions": admitted,
        "selection_sha256": sha256(selection_path), "inputs_sha256": sha256(args.out / "inputs.json"),
        "results": results, "forecasts": forecasts,
        "all_objectives_met": bool(selected and all(results[p][selected]["all_objectives_met"] for p in results)),
        "costs_bps_per_side": {"fee": 5, "slippage": 5}, "higher_costs_bps_per_side": {"fee": 25, "slippage": 25},
        "limitations": "Adaptive five-surviving-stock history; raw scores are not calibrated probabilities. Price-risk labels exclude cash dividends. No pristine holdout, taxes, final liquidation or live promotion."}
    if args.monthly:
        summary["refit_frequency"] = "monthly"
    elif broader:
        summary.update(refit_frequency="annual", date_weighted=True, training_panel=panel_commitment)
        summary["limitations"] += " Broader training is a retrospective provider-available sample; historical identity continuity and terminal proceeds are unverified."
    if args.boosted:
        summary["boosted"] = True
    (args.out / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
