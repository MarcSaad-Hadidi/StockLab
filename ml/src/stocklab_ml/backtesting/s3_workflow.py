"""Offline-first issue #48 workflow and explicit, approved S3 transfer commands."""

import argparse
from dataclasses import asdict
from datetime import UTC, datetime
import json
from pathlib import Path
import sys
from uuid import uuid4

import boto3
from botocore.config import Config
from botocore.exceptions import BotoCoreError, ClientError
import numpy as np
import pandas as pd

from stocklab_ml.artifacts import S3ArtifactConfig, S3ModelArtifactStore, save_model_artifact
from stocklab_ml.artifacts.contracts import ArtifactConflictError, ArtifactStorageError, MAX_MANIFEST_BYTES
from stocklab_ml.artifacts.local_storage import artifact_paths, read_bounded
from stocklab_ml.artifacts.s3_files import safe_segment
from stocklab_ml.data.cleaning import clean_historical_dataset
from stocklab_ml.data.models import HistoricalDataError
from stocklab_ml.data.s3_storage import S3DatasetStore
from stocklab_ml.data.storage import load_cleaning_csv, save_processed, save_raw
from stocklab_ml.features import engineer_features
from stocklab_ml.modeling import train_logistic_regression
from stocklab_ml.modeling.contracts import ModelingError
from stocklab_ml.signals import add_confidence_scores, generate_trading_signals
from stocklab_ml.versioning import ModelVersionRegistry
from .contracts import BacktestError, SavedBacktest
from .engine import run_backtest
from .s3_storage import BACKTEST_FILES, S3BacktestStore
from .storage import save_backtest_result


def synthetic_history():
    """Deterministic invented observations, solely for validating software mechanics."""
    index = np.arange(240)
    close = 100 + index * .07 + 3 * np.sin(index * .7)
    return pd.DataFrame({
        "date": pd.date_range("2023-01-02", periods=len(index), freq="B").as_unit("ns"),
        "symbol": pd.array(["SYNTH"] * len(index), dtype="string"),
        "open": close, "high": close + 1, "low": close - 1, "close": close,
        "volume": pd.array(1000 + index, dtype="Int64"),
    })


def run_storage_workflow(output_dir, *, csv_path=None, as_of_date=None, registry_path=None, backtest_id=None):
    """Run the existing components once; never contact AWS, providers or execution systems.

    Use the existing shared model registry by default so repeated runs receive
    unique model versions. An isolated registry can be supplied explicitly.
    Financial outputs are reproducible; run UUIDs and registration times are not.
    """
    backtest_id = safe_segment(backtest_id if backtest_id is not None else str(uuid4()))
    if csv_path is not None and as_of_date is None:
        raise ValueError("Caller-supplied CSV requires an explicit as_of_date cutoff.")
    original = read_bounded(Path(csv_path), 64 * 1024 * 1024) if csv_path is not None else None
    directory = Path(output_dir).resolve()
    try:
        directory.mkdir(parents=True, exist_ok=False)
    except FileExistsError:
        raise ArtifactConflictError("Workflow output already exists; select a new directory.") from None
    datasets = directory / "datasets"
    datasets.mkdir()
    raw_path, cleaned_path = datasets / "raw.csv", datasets / "cleaned.csv"
    if original is None:
        save_processed(raw_path, synthetic_history())
    else:
        raw_path.write_bytes(original)
    cleaned = clean_historical_dataset(load_cleaning_csv(raw_path), as_of_date=as_of_date or "2024-12-31")
    if len(cleaned.report.symbols) != 1:
        raise ValueError("This workflow requires a single-symbol historical CSV; run each symbol separately.")
    save_processed(cleaned_path, cleaned.dataframe)
    save_raw(datasets / "cleaning_report.json", asdict(cleaned.report))
    features = engineer_features(cleaned.dataframe).dataframe
    trained = train_logistic_regression(features)
    registry = ModelVersionRegistry(registry_path)
    record = registry.register_version(trained, training_run_id=str(uuid4()), trained_at_utc=datetime.now(UTC))
    context = registry.bind_version(trained, model_name=record.model_name, model_version=record.model_version)
    save_model_artifact(context, output_dir=directory / "models")
    # The existing training function fits the purged train partition only.
    # Its historical predictions contain holdout rows; #64 removes labels.
    predictions = context.historical_predictions().predictions
    decisions = add_confidence_scores(generate_trading_signals(predictions, model_name=record.model_name))
    result = run_backtest(decisions, cleaned)
    save_backtest_result(result, output_dir=directory / "backtest", source_paths=(raw_path, cleaned_path))
    save_processed(directory / "predictions.csv", predictions)
    save_processed(directory / "signals.csv", decisions.signals)
    info = {
        "schema_version": 1, "backtest_id": backtest_id, "dataset_id": backtest_id,
        "synthetic": original is None,
        "data_source": "deterministic_synthetic" if original is None else "caller_supplied_csv",
        "disclaimer": "Simulation only; synthetic results are not real financial performance. Local CSV provenance is caller supplied.",
        "as_of_date": as_of_date or "2024-12-31", "symbol": cleaned.report.symbols[0],
        "start_date": cleaned.report.date_min, "end_date": cleaned.report.date_max,
        "cleaning_report": asdict(cleaned.report),
        "model_name": record.model_name, "model_version": record.model_version,
        "registry_path": str(registry.path), "train_end": record.train_end, "test_start": record.test_start,
        "report": asdict(result.report), "market_api_calls": 0,
    }
    save_raw(directory / "workflow.json", info)
    # Return exactly the JSON contract persisted on disk (tuples become arrays).
    return json.loads((directory / "workflow.json").read_text(encoding="utf-8"))


def upload_storage_workflow(directory, *, config, client=None):
    """Publish completed local files without re-training or re-running a simulation.

    Retry the same directory after a transient failure. Each bundle is immutable;
    the workflow as a whole is not an S3 transaction.
    """
    directory = Path(directory)
    info = json.loads(read_bounded(directory / "workflow.json", MAX_MANIFEST_BYTES))
    safe_segment(info["backtest_id"])
    safe_segment(info["dataset_id"])
    model_store = S3ModelArtifactStore(config, client=client)
    client = model_store.client  # Reuse the configured client and credentials for every store.
    metadata = {key: info[key] for key in ["backtest_id", "synthetic", "data_source", "disclaimer",
                                          "model_name", "model_version", "train_end", "test_start"]}
    datasets = S3DatasetStore(config, client=client)
    references = {"datasets": []}
    for kind in ["raw", "cleaned"]:
        reference = datasets.upload_dataset(directory / "datasets" / f"{kind}.csv", symbol=info["symbol"],
            start_date=info["start_date"], end_date=info["end_date"], kind=kind, dataset_id=info["dataset_id"],
            metadata=metadata | {"as_of_date": info["as_of_date"], "cleaning_report": info["cleaning_report"]})
        references["datasets"].append(asdict(reference))
    registry = ModelVersionRegistry(info["registry_path"])
    artifact = artifact_paths(info["model_name"], info["model_version"], directory / "models")
    references["model"] = asdict(model_store.upload_model_artifact(artifact, registry=registry))
    saved = SavedBacktest(*(directory / "backtest" / name for name in BACKTEST_FILES))
    references["backtest"] = asdict(S3BacktestStore(config, client=client).upload_backtest(
        saved, backtest_id=info["backtest_id"], metadata=metadata))
    return references


def approved_live_config():
    """Issue #48 live commands only target the resources/profile approved in the prompt."""
    import os
    config = S3ArtifactConfig.from_environment(trusted_private_source=True)
    if ((config.bucket, config.region, config.prefix) != ("stocklab-ml-248557779705", "ca-central-1", "ml")
            or os.environ.get("AWS_PROFILE") != "stocklab-dev"):
        raise ValueError("Live commands require the approved bucket, region, prefix and stocklab-dev profile.")
    return config


def approved_live_client(config):
    """Reject root credentials before S3 I/O; never print an identity or credentials."""
    sdk_config = Config(connect_timeout=5, read_timeout=30,
                        retries={"mode": "standard", "total_max_attempts": 3},
                        ignore_configured_endpoint_urls=True)
    try:
        session = boto3.Session(profile_name="stocklab-dev")
        identity = session.client("sts", region_name=config.region, use_ssl=True,
                                  config=sdk_config).get_caller_identity()
        if identity.get("Account") != "248557779705" or identity.get("Arn", "").endswith(":root"):
            raise ValueError("Live validation requires an approved non-root identity in the approved AWS account.")
        return session.client("s3", region_name=config.region, use_ssl=True,
                              config=sdk_config.merge(Config(signature_version="s3v4")))
    except (BotoCoreError, ClientError, OSError):
        raise ArtifactStorageError("AWS identity check failed; renew the approved temporary login/profile.") from None


def _parser():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    run = commands.add_parser("run", help="Local synthetic run or explicit historical CSV; zero AWS/API calls")
    run.add_argument("--output", required=True)
    run.add_argument("--csv")
    run.add_argument("--as-of", dest="as_of_date")
    run.add_argument("--registry")
    run.add_argument("--backtest-id")
    upload = commands.add_parser("upload", help="Upload an existing workflow without executing it again")
    upload.add_argument("--directory", required=True)
    backtest = commands.add_parser("download-backtest", help="Retrieve four files without model or engine")
    backtest.add_argument("--backtest-id", required=True)
    backtest.add_argument("--output", required=True)
    upload_backtest = commands.add_parser("upload-backtest", help="Publish existing engine output files")
    upload_backtest.add_argument("--directory", required=True)
    upload_backtest.add_argument("--backtest-id", required=True)
    for action in ["upload-dataset", "download-dataset"]:
        command = commands.add_parser(action)
        for argument in ["symbol", "start-date", "end-date", "dataset-id"]:
            command.add_argument(f"--{argument}", required=True)
        command.add_argument("--kind", choices=["raw", "cleaned"], required=True)
        command.add_argument("--file" if action.startswith("upload") else "--output", required=True)
    for name, command in commands.choices.items():
        if name != "run":
            command.add_argument("--live-aws", action="store_true", required=True,
                                 help="Explicit opt-in to approved private AWS requests/storage costs")
    return parser


def main(argv=None):
    args = _parser().parse_args(argv)
    try:
        if args.command == "run":
            result = run_storage_workflow(args.output, csv_path=args.csv, as_of_date=args.as_of_date,
                registry_path=args.registry, backtest_id=args.backtest_id)
        else:
            config = approved_live_config()
            client = approved_live_client(config)
            if args.command == "upload":
                result = upload_storage_workflow(args.directory, config=config, client=client)
            elif args.command == "download-backtest":
                bundle = S3BacktestStore(config, client=client).download_backtest(args.backtest_id, output_dir=args.output)
                result = {"directory": str(bundle.directory), "metadata": bundle.metadata}
            elif args.command == "upload-backtest":
                saved = SavedBacktest(*(Path(args.directory) / name for name in BACKTEST_FILES))
                result = asdict(S3BacktestStore(config, client=client).upload_backtest(saved, backtest_id=args.backtest_id))
            else:
                store = S3DatasetStore(config, client=client)
                identity = dict(symbol=args.symbol, start_date=args.start_date, end_date=args.end_date,
                                kind=args.kind, dataset_id=args.dataset_id)
                if args.command == "upload-dataset":
                    result = asdict(store.upload_dataset(args.file, **identity))
                else:
                    bundle = store.download_dataset(**identity, output_dir=args.output)
                    result = {"directory": str(bundle.directory), "metadata": bundle.metadata}
        print(json.dumps(result, indent=2, allow_nan=False))
        return 0
    except (ValueError, KeyError, TypeError, HistoricalDataError, ModelingError, BacktestError, OSError) as error:
        print(f"Storage workflow failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
