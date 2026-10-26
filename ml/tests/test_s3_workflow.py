"""Actual training/signals/backtesting and exact recovery, entirely offline."""

import json

import pandas as pd
import pytest

from stocklab_ml.artifacts import S3ArtifactConfig, S3ModelArtifactStore, load_model_artifact
from stocklab_ml.versioning import ModelVersionRegistry
from test_s3_file_storage import FakeS3


def workflow():
    from stocklab_ml.backtesting import s3_workflow
    return s3_workflow


def test_synthetic_workflow_is_reproducible_and_uses_only_holdout(tmp_path):
    module = workflow()
    registry = tmp_path / "registry.json"
    one = module.run_storage_workflow(tmp_path / "one", registry_path=registry, backtest_id="one")
    two = module.run_storage_workflow(tmp_path / "two", registry_path=registry, backtest_id="two")
    assert one["synthetic"] is True
    assert one["data_source"] == "deterministic_synthetic"
    assert one["train_end"] < one["test_start"] == one["report"]["start_date"]
    assert one["report"]["executed_trades"] > 0
    assert one["report"] == two["report"]
    assert one["model_version"] != two["model_version"]
    for name in ["trades.csv", "equity_curve.csv", "positions.csv", "report.json"]:
        assert (tmp_path / "one" / "backtest" / name).read_bytes() == (tmp_path / "two" / "backtest" / name).read_bytes()
    trades = pd.read_csv(tmp_path / "one" / "backtest" / "trades.csv")
    assert (trades.execution_date > trades.decision_date).all()


def test_csv_input_requires_as_of_and_preserves_original_bytes(tmp_path):
    module = workflow()
    csv = tmp_path / "historical.csv"
    module.synthetic_history().to_csv(csv, index=False, lineterminator="\r\n", date_format="%Y-%m-%d")
    with pytest.raises(ValueError, match="as_of_date"):
        module.run_storage_workflow(tmp_path / "bad", csv_path=csv, registry_path=tmp_path / "registry.json")
    result = module.run_storage_workflow(tmp_path / "run", csv_path=csv, as_of_date="2024-12-31",
                                         registry_path=tmp_path / "registry.json")
    assert result["synthetic"] is False  # Caller supplied; no claim this fixture is real market evidence.
    assert result["data_source"] == "caller_supplied_csv"
    assert (tmp_path / "run" / "datasets" / "raw.csv").read_bytes() == csv.read_bytes()


def test_publish_and_recover_all_components_without_training_or_engine(tmp_path, monkeypatch):
    module = workflow()
    directory = tmp_path / "run"
    info = module.run_storage_workflow(directory, registry_path=tmp_path / "registry.json", backtest_id="recover")
    config = S3ArtifactConfig("stocklab-dev", "ca-central-1", "ml", trusted_private_source=True)
    fake = FakeS3()
    def forbidden(*args, **kwargs):
        pytest.fail("Publication and recovery must not train or execute the engine")
    monkeypatch.setattr(module, "train_logistic_regression", forbidden)
    monkeypatch.setattr(module, "run_backtest", forbidden)
    references = module.upload_storage_workflow(directory, config=config, client=fake)
    assert len(references["datasets"]) == 2
    assert references == module.upload_storage_workflow(directory, config=config, client=fake)
    from stocklab_ml.backtesting.s3_storage import S3BacktestStore
    from stocklab_ml.data.s3_storage import S3DatasetStore
    recovered = S3BacktestStore(config, client=fake).download_backtest("recover", output_dir=tmp_path / "backtest")
    assert recovered.metadata["synthetic"] is True
    assert recovered.metadata["model_version"] == info["model_version"]
    for name, path in recovered.files.items():
        assert path.read_bytes() == (directory / "backtest" / name).read_bytes()
    for kind in ["raw", "cleaned"]:
        dataset = S3DatasetStore(config, client=fake).download_dataset(
            symbol=info["symbol"], start_date=info["start_date"], end_date=info["end_date"],
            kind=kind, dataset_id="recover", output_dir=tmp_path / kind)
        assert dataset.files[f"{kind}.csv"].read_bytes() == (directory / "datasets" / f"{kind}.csv").read_bytes()
    registry = ModelVersionRegistry(info["registry_path"])
    artifact = S3ModelArtifactStore(config, client=fake).download_model_artifact(
        info["model_name"], info["model_version"], registry=registry, output_dir=tmp_path / "models")
    from sklearn.pipeline import Pipeline
    monkeypatch.setattr(Pipeline, "fit", forbidden)
    loaded = load_model_artifact(artifact, registry=registry, trusted_source=True)
    assert loaded.record.model_version == info["model_version"]


def test_default_cli_run_never_constructs_aws_client(tmp_path, monkeypatch, capsys):
    module = workflow()
    def forbidden(*args, **kwargs):
        pytest.fail("Default CLI run must remain offline")
    monkeypatch.setattr("boto3.client", forbidden)
    assert module.main(["run", "--output", str(tmp_path / "run"), "--registry", str(tmp_path / "registry.json")]) == 0
    result = json.loads(capsys.readouterr().out)
    assert result["synthetic"] is True
    assert result["report"]["executed_trades"] > 0


def test_cli_upload_requires_explicit_aws_flag_before_any_io(tmp_path):
    with pytest.raises(SystemExit):
        workflow().main(["upload", "--directory", str(tmp_path / "absent")])


def test_approved_live_configuration_rejects_other_targets_before_network(monkeypatch):
    module = workflow()
    monkeypatch.setenv("STOCKLAB_ML_S3_BUCKET", "different-bucket")
    monkeypatch.setenv("STOCKLAB_ML_AWS_REGION", "ca-central-1")
    monkeypatch.setenv("STOCKLAB_ML_S3_PREFIX", "ml")
    monkeypatch.setenv("AWS_PROFILE", "stocklab-dev")
    with pytest.raises(ValueError, match="approved"):
        module.approved_live_config()


def test_live_client_uses_one_explicit_profile_session_despite_ambient_credentials(monkeypatch):
    module = workflow()
    monkeypatch.setenv("AWS_ACCESS_KEY_ID", "unapproved-offline-env-key")
    monkeypatch.setenv("AWS_SECRET_ACCESS_KEY", "unapproved-offline-env-secret")
    monkeypatch.setenv("AWS_PROFILE", "stocklab-dev")
    calls = []
    s3 = object()
    class STS:
        def get_caller_identity(self):
            return {"Account": "248557779705", "Arn": "arn:aws:sts::248557779705:assumed-role/StockLabMLTaskRole/offline"}
    class Session:
        def __init__(self, *, profile_name):
            calls.append(("profile", profile_name))
        def client(self, service, **kwargs):
            calls.append(("client", service))
            return STS() if service == "sts" else s3
    monkeypatch.setattr(module.boto3, "Session", Session)
    def forbidden(*args, **kwargs):
        pytest.fail("Global SDK client may use unapproved ambient credentials")
    monkeypatch.setattr(module.boto3, "client", forbidden)
    config = S3ArtifactConfig("stocklab-ml-248557779705", "ca-central-1", "ml", trusted_private_source=True)
    assert module.approved_live_client(config) is s3
    assert calls == [("profile", "stocklab-dev"), ("client", "sts"), ("client", "s3")]


@pytest.mark.parametrize("identity", [
    {"Account": "248557779705", "Arn": "arn:aws:iam::248557779705:root"},
    {"Account": "000000000000", "Arn": "arn:aws:iam::000000000000:user/offline"},
])
def test_live_client_rejects_root_or_another_account_before_s3(monkeypatch, identity):
    module = workflow()
    class STS:
        def get_caller_identity(self):
            return identity
    class Session:
        def __init__(self, **kwargs):
            pass
        def client(self, service, **kwargs):
            assert service == "sts", "Rejected identity must not reach S3"
            return STS()
    monkeypatch.setattr(module.boto3, "Session", Session)
    config = S3ArtifactConfig("stocklab-ml-248557779705", "ca-central-1", "ml", trusted_private_source=True)
    with pytest.raises(ValueError, match="non-root"):
        module.approved_live_client(config)
