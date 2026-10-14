"""Scheduler-friendly one-shot CLI with structured output and explicit trust."""

import argparse
from dataclasses import replace
from datetime import datetime
import json
import logging
from pathlib import Path

from stocklab_ml.artifacts import S3ArtifactConfig, S3ModelArtifactStore
from stocklab_ml.data.storage import load_cleaning_csv
from .contracts import RetrainingConfig, RetrainingDependencies, RetrainingError, RetrainingConfigurationError
from .orchestrator import run_retraining_check
from .scheduler import validate_execution_context
from .state import read_json


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="One #77 retraining check; never activates a model.")
    parser.add_argument("--once", action="store_true", required=True)
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument("--local-data", type=Path, help="Explicit offline OHLCV source; no provider call")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--retry-run-id")
    parser.add_argument("--now-utc", help="Explicit UTC test clock in ISO format")
    parser.add_argument("--trigger-source", choices=["manual", "scheduler", "offline_smoke"], default="manual")
    parser.add_argument("--approve-private-s3", action="store_true",
                        help="Acknowledges independent approval of the configured private #76 bucket/prefix")
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    try:
        payload = read_json(args.config, limit=65536)
        if not isinstance(payload, dict):
            raise RetrainingConfigurationError("A JSON configuration object is required.")
        try:
            config = RetrainingConfig(**payload)
            if args.dry_run:
                config = replace(config, dry_run=True)
            now = datetime.fromisoformat(args.now_utc) if args.now_utc else None
        except (TypeError, ValueError):
            raise RetrainingConfigurationError("Invalid retraining configuration or UTC clock.") from None
        validate_execution_context(config)
        store = None
        if config.upload_to_s3 and not config.dry_run:
            if not args.approve_private_s3:
                raise RetrainingConfigurationError("Private S3 configuration must be explicitly approved before use.")
            store = S3ModelArtifactStore(S3ArtifactConfig.from_environment(trusted_private_source=True))
        local = load_cleaning_csv(args.local_data) if args.local_data else None
        result = run_retraining_check(config, dependencies=RetrainingDependencies(local, store), now_utc=now,
            retry_run_id=args.retry_run_id, trigger_source=args.trigger_source)
        print(json.dumps(result.to_dict(), allow_nan=False))
        return 1 if result.status in {"FAILED", "PARTIAL_PUBLICATION"} else 0
    except Exception as error:
        # Do not expose raw paths, arguments, payloads or chained AWS/provider errors.
        category = type(error).__name__ if isinstance(error, RetrainingError) else "ConfigurationOrStorageError"
        print(json.dumps({"status": "FAILED", "failure_category": category}))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
