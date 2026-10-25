"""Export existing #61/#62/#73/#74/#75 results from an operator-supplied OHLCV CSV."""

import argparse
from datetime import UTC, datetime
import hashlib
from pathlib import Path
from uuid import uuid4

from stocklab_ml.backtesting import (
    compare_to_buy_and_hold, run_backtest, save_backtest_result, save_benchmark_comparison,
)
from stocklab_ml.data.cleaning import clean_processed_dataset
from stocklab_ml.data.storage import save_raw
from stocklab_ml.features import engineer_features
from stocklab_ml.modeling import train_random_forest
from stocklab_ml.signals import add_confidence_scores, generate_trading_signals
from stocklab_ml.versioning import ModelVersionRegistry


def export_catalogs(input_path: Path, output_dir: Path) -> Path:
    source = input_path.resolve(strict=True)
    destination = output_dir.resolve()
    # A new run never overwrites an existing registry, report or input dataset.
    destination.mkdir(parents=True, exist_ok=False)
    cleaned = clean_processed_dataset(source, output_path=destination / "cleaned.csv",
                                     report_path=destination / "cleaning.json")
    features = engineer_features(cleaned.dataframe)
    forest = train_random_forest(features.dataframe)
    registry = ModelVersionRegistry(destination / "registry.json")
    records = []
    for result in (forest.baseline_result, forest):
        record = registry.register_version(result, training_run_id=str(uuid4()),
                                           trained_at_utc=datetime.now(UTC))
        decisions = add_confidence_scores(generate_trading_signals(
            result.predictions, model_name=record.model_name))
        backtest = run_backtest(decisions, cleaned)
        saved = save_backtest_result(backtest, output_dir=destination / record.model_name / "backtest",
                                    source_paths=(source,))
        benchmark = save_benchmark_comparison(compare_to_buy_and_hold(backtest, cleaned),
            output_dir=destination / record.model_name / "benchmark", source_paths=(source,))
        records.append({"model_name": record.model_name, "model_version": record.model_version,
                        "training_run_id": record.training_run_id,
                        "backtest_report": str(saved.report_path.relative_to(destination)),
                        "benchmark_comparison": str(benchmark.comparison_path.relative_to(destination))})
    provenance = destination / "provenance.json"
    save_raw(provenance, {"source_kind": "operator-supplied OHLCV CSV",
        "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "observed_start": cleaned.report.date_min, "observed_end": cleaned.report.date_max,
        "symbols": cleaned.report.symbols, "observations": cleaned.report.output_rows,
        "registered_models": records}, overwrite=False)
    return provenance


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-path", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    args = parser.parse_args()
    print(export_catalogs(args.input_path, args.output_dir))


if __name__ == "__main__":
    main()
