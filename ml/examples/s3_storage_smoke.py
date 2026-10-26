"""#48 actual model/backtest round-trip; offline by default, explicit --live-aws."""

import argparse
from dataclasses import asdict
import json
from pathlib import Path
import socket
from uuid import uuid4

from stocklab_ml.artifacts import S3ArtifactConfig, S3ModelArtifactStore, load_model_artifact
from stocklab_ml.backtesting.s3_storage import S3BacktestStore
from stocklab_ml.backtesting.s3_workflow import (
    approved_live_client, approved_live_config, run_storage_workflow, upload_storage_workflow,
)
from stocklab_ml.data.s3_storage import S3DatasetStore
from stocklab_ml.versioning import ModelVersionRegistry
from model_artifact_storage_smoke import FakeS3


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--live-aws", action="store_true")
    parser.add_argument("--output")
    args = parser.parse_args()
    if args.live_aws:
        config = approved_live_config()
        client = approved_live_client(config)
    else:
        def forbidden(*args, **kwargs):
            raise AssertionError("Network is forbidden in the offline smoke")
        socket.socket.connect = forbidden
        socket.socket.connect_ex = forbidden
        config = S3ArtifactConfig("stocklab-offline-dev", "ca-central-1", "ml", trusted_private_source=True)
        client = FakeS3()
    root = Path(args.output) if args.output else Path(__file__).resolve().parents[1] / "results" / "s3_storage_smoke" / str(uuid4())
    info = run_storage_workflow(root / "run", registry_path=root / "registry.json" if not args.live_aws else None)
    references = upload_storage_workflow(root / "run", config=config, client=client)
    recovered = S3BacktestStore(config, client=client).download_backtest(info["backtest_id"], output_dir=root / "recovered" / "backtest")
    for name, path in recovered.files.items():
        assert path.read_bytes() == (root / "run" / "backtest" / name).read_bytes()
    for kind in ["raw", "cleaned"]:
        bundle = S3DatasetStore(config, client=client).download_dataset(
            symbol=info["symbol"], start_date=info["start_date"], end_date=info["end_date"],
            kind=kind, dataset_id=info["dataset_id"], output_dir=root / "recovered" / kind)
        assert bundle.files[f"{kind}.csv"].read_bytes() == (root / "run" / "datasets" / f"{kind}.csv").read_bytes()
    registry = ModelVersionRegistry(info["registry_path"])
    artifact = S3ModelArtifactStore(config, client=client).download_model_artifact(
        info["model_name"], info["model_version"], registry=registry, output_dir=root / "recovered" / "models")
    from sklearn.pipeline import Pipeline
    def no_fit(*args, **kwargs):
        raise AssertionError("Retraining is forbidden after persistence")
    Pipeline.fit = no_fit
    loaded = load_model_artifact(artifact, registry=registry, trusted_source=True)
    assert loaded.record.model_version == info["model_version"]
    print(json.dumps({"synthetic": True, "real_financial_performance": False,
        "backtest_id": info["backtest_id"], "executed_trades": info["report"]["executed_trades"],
        "four_backtest_files": "byte-identical", "datasets": "byte-identical",
        "model": asdict(artifact), "references": references, "market_api_calls": 0,
        "live_aws_enabled": args.live_aws, "output": str(root)}, default=str, indent=2))


if __name__ == "__main__":
    main()
