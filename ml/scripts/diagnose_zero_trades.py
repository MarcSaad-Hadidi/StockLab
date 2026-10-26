"""Reproduce a two-baseline catalog offline and explain its execution filters.

Recreates the existing estimators only in memory. Never writes artifacts, changes
thresholds, promotes a version, reads joblib, or accesses a broker/database.
"""
import argparse
from dataclasses import asdict
import json
from pathlib import Path
import sys

from stocklab_ml.backtesting import BacktestConfig, run_backtest
from stocklab_ml.data.cleaning import clean_historical_dataset
from stocklab_ml.data.storage import load_cleaning_csv
from stocklab_ml.features import engineer_features
from stocklab_ml.modeling import train_random_forest
from stocklab_ml.signals import add_confidence_scores, generate_trading_signals
from stocklab_ml.versioning import ModelVersionRegistry
from stocklab_ml.versioning.validation import canonical

BASELINES = ("logistic_regression_baseline", "random_forest_baseline")
SKIPS = ("skipped_low_confidence", "skipped_no_position", "skipped_max_positions",
         "skipped_exposure", "skipped_buying_power", "no_next_session")


class _ArgumentParser(argparse.ArgumentParser):
    def error(self, message):
        self.exit(2, "diagnostic_invalid_arguments: use --help for usage.\n")


def _read_report(path):
    def unique_object(pairs):
        value = {}
        for key, item in pairs:
            if key in value:
                raise ValueError("Duplicate report key.")
            value[key] = item
        return value

    def invalid_constant(_):
        raise ValueError("Nonfinite report value.")

    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_object,
                      parse_constant=invalid_constant)


def diagnose_catalog(catalog_dir: Path, *, as_of_date: str) -> dict:
    """Require exporter layout and exactly one version of each baseline.

    A successful result verifies full registered training/holdout metadata and
    each original backtest report before returning aggregate observations only.
    A different environment, input, estimator or policy fails without repair.
    """
    directory = Path(catalog_dir)
    registry = ModelVersionRegistry(directory / "registry.json")
    versions = {name: registry.list_versions(name) for name in BASELINES}
    if any(len(records) != 1 for records in versions.values()):
        raise ValueError("Exactly one version per baseline is required.")
    records = {name: values[0] for name, values in versions.items()}
    fractions = [record.training_report["split"]["test_fraction"] for record in records.values()]
    if fractions[0] != fractions[1]:
        raise ValueError("Baseline holdout policies must match.")
    originals = {name: _read_report(directory / name / "backtest/report.json") for name in BASELINES}
    cleaned = clean_historical_dataset(load_cleaning_csv(directory / "cleaned.csv"), as_of_date=as_of_date)
    features = engineer_features(cleaned.dataframe)
    forest = train_random_forest(features.dataframe, test_fraction=fractions[0])
    models = []
    for result in (forest.baseline_result, forest):
        name = result.report.model_name
        record, original = records[name], originals[name]
        context = registry.bind_version(result, model_name=name, model_version=record.model_version)
        predictions = context.historical_predictions().predictions
        signals = add_confidence_scores(generate_trading_signals(predictions, model_name=name,
            buy_threshold=original["buy_threshold"], sell_threshold=original["sell_threshold"]))
        config = BacktestConfig(**original["config"])
        backtest = run_backtest(signals, cleaned, config=config)
        # Canonical JSON preserves type differences such as false versus 0.
        if canonical(asdict(backtest.report)) != canonical(original):
            raise ValueError("Original backtest report was not reproduced.")
        frame, report = signals.signals, backtest.report
        confidence = {}
        for action in ("BUY", "SELL"):
            values = frame.loc[frame.signal.eq(action), "confidence"]
            confidence[action] = {"min": float(values.min()) if len(values) else None,
                "max": float(values.max()) if len(values) else None,
                "passes_minimum": int(values.ge(config.minimum_confidence).sum())}
        models.append({"model_name": name, "model_version": record.model_version, "reproduced": True,
            "decision_count": report.decision_count,
            "signal_counts": {"BUY": report.buy_decisions, "SELL": report.sell_decisions, "HOLD": report.hold_decisions},
            "probability_up": {"min": float(frame.probability_up.min()), "max": float(frame.probability_up.max())},
            "buy_threshold": report.buy_threshold, "sell_threshold": report.sell_threshold,
            "minimum_confidence": config.minimum_confidence, "confidence": confidence,
            "skipped": {key: getattr(report, key) for key in SKIPS}, "executed_trades": report.executed_trades})
    return {"schema_version": 1, "models": models}


def main(argv=None):
    parser = _ArgumentParser(description=__doc__)
    parser.add_argument("--catalog-dir", required=True, type=Path)
    parser.add_argument("--as-of-date", required=True, help="Explicit inclusive YYYY-MM-DD input cutoff")
    args = parser.parse_args(argv)
    try:
        result = diagnose_catalog(args.catalog_dir, as_of_date=args.as_of_date)
        print(json.dumps(result, indent=2, allow_nan=False))
        return 0
    except Exception:
        print("diagnostic_failed: check catalog, date and original model environment.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
