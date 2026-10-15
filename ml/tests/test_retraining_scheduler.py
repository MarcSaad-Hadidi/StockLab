"""One-shot worker exit codes, conservative cutoff and persistent AWS prerequisites."""

from dataclasses import replace
from importlib import import_module
import json
from pathlib import Path
import os
import subprocess
import sys
from uuid import uuid4
from types import SimpleNamespace

import pytest

from test_retraining import api, config, history, NOW, run


def scheduler():
    return import_module("stocklab_ml.retraining.scheduler")


def test_schedule_has_three_day_rate_and_no_resource_creation(config):
    assert scheduler().schedule_expression(config) == "rate(3 days)"
    assert scheduler().schedule_expression(replace(config, check_interval_days=1)) == "rate(1 day)"
    assert not config.state_path.exists()


def test_aws_context_without_mounted_persistent_state_fails_closed(config, monkeypatch):
    root = config.dataset_path.parent
    aws = replace(config, aws_enabled=True, upload_to_s3=True, persistent_root=root,
                  persistent_volume_id=str(uuid4()))
    with pytest.raises(api().RetrainingConfigurationError):
        run(aws)
    assert not aws.state_path.exists()
    monkeypatch.setenv("AWS_EXECUTION_ENV", "AWS_ECS_FARGATE")
    with pytest.raises(api().RetrainingConfigurationError):
        run(config)


def test_cli_requires_once_and_valid_config_without_creating_state(tmp_path, capsys):
    cli = import_module("stocklab_ml.retraining.cli")
    with pytest.raises(SystemExit):
        cli.main([])
    assert cli.main(["--once", "--config", str(tmp_path / "missing.json")]) == 2
    output = json.loads(capsys.readouterr().out)
    assert output["status"] == "FAILED"


def test_cli_dry_run_reads_local_source_without_writes(config, tmp_path, capsys):
    from stocklab_ml.retraining.state import config_identity
    cli = import_module("stocklab_ml.retraining.cli")
    path = tmp_path / "config.json"
    path.write_text(json.dumps(config_identity(config)))
    source = tmp_path / "incoming.csv"
    history(103).to_csv(source, index=False, date_format="%Y-%m-%d")
    assert cli.main(["--once", "--config", str(path), "--local-data", str(source), "--dry-run",
                     "--now-utc", NOW.isoformat()]) == 0
    output = json.loads(capsys.readouterr().out)
    assert output["status"] == "DRY_RUN_READY" and output["report"]["api_request_count"] == 0
    assert not config.state_path.exists() and not config.registry_path.exists()


def test_cli_dry_run_reports_insufficient_new_sessions(config, tmp_path, capsys):
    from stocklab_ml.retraining.state import config_identity
    cli = import_module("stocklab_ml.retraining.cli")
    config_path = tmp_path / "config.json"
    config_path.write_text(json.dumps(config_identity(replace(config, minimum_new_sessions=4))))
    source = tmp_path / "incoming.csv"
    history(103).to_csv(source, index=False, date_format="%Y-%m-%d")
    assert cli.main(["--once", "--config", str(config_path), "--local-data", str(source), "--dry-run",
                     "--now-utc", NOW.isoformat()]) == 0
    result = json.loads(capsys.readouterr().out)
    assert result["status"] == "DRY_RUN_INSUFFICIENT_NEW_SESSIONS"
    assert result["report"]["new_session_counts"] == {"AAPL": 3}
    assert not config.state_path.parent.exists() and not config.registry_path.exists()


def test_cli_failures_have_nonzero_exit_and_no_secret_echo(config, tmp_path, capsys):
    cli = import_module("stocklab_ml.retraining.cli")
    path = tmp_path / "config.json"
    path.write_text('{"symbols":[],"credential":"private-secret"}')
    assert cli.main(["--once", "--config", str(path)]) == 2
    assert "private-secret" not in capsys.readouterr().out


def test_worker_import_has_no_training_or_state_writes(tmp_path):
    environment = os.environ.copy()
    environment["PYTHONPATH"] = str(Path(__file__).resolve().parents[1] / "src")
    command = "import socket; socket.socket.connect=lambda *a: (_ for _ in ()).throw(AssertionError()); import stocklab_ml.retraining"
    completed = subprocess.run([sys.executable, "-c", command], cwd=tmp_path, env=environment,
                               capture_output=True, text=True, timeout=30)
    assert completed.returncode == 0 and list(tmp_path.iterdir()) == []


def test_declarative_aws_template_has_resolvable_parameters_and_existing_infrastructure():
    path = Path(__file__).resolve().parents[1] / "aws" / "automatic-retraining.template.json"
    template = json.loads(path.read_text())
    parameters = template["Parameters"]
    resources = template["Resources"]
    assert {value["Type"] for value in resources.values()} == {"AWS::Scheduler::Schedule", "AWS::ECS::TaskDefinition"}
    schedule = resources["RetrainingSchedule"]["Properties"]
    assert schedule["State"] == "DISABLED" and schedule["ScheduleExpression"] == "rate(3 days)"
    target = schedule["Target"]["EcsParameters"]
    assert target["TaskCount"] == 1 and target["LaunchType"] == "FARGATE" and target["PlatformVersion"] == "1.4.0"
    definition = resources["RetrainingWorker"]["Properties"]
    volume = definition["Volumes"][0]["EFSVolumeConfiguration"]
    assert volume["TransitEncryption"] == "ENABLED" and volume["AuthorizationConfig"]["IAM"] == "ENABLED"
    assert "RootDirectory" not in volume or volume["RootDirectory"] == "/"
    def refs(value):
        if isinstance(value, dict):
            for name, item in value.items():
                if name == "Ref":
                    assert item in parameters or item in resources or item.startswith("AWS::")
                refs(item)
        elif isinstance(value, list):
            for item in value: refs(item)
    refs(template)


def test_cli_failure_result_returns_nonzero(config, tmp_path, capsys):
    from stocklab_ml.retraining.state import config_identity
    cli = import_module("stocklab_ml.retraining.cli")
    config_path = tmp_path / "config.json"
    config_path.write_text(json.dumps(config_identity(config)))
    source = tmp_path / "changed.csv"
    changed = history(103)
    changed.loc[99, ["open", "close"]] += .1
    changed.to_csv(source, index=False, date_format="%Y-%m-%d")
    assert cli.main(["--once", "--config", str(config_path), "--local-data", str(source),
                     "--now-utc", NOW.isoformat()]) == 1
    assert json.loads(capsys.readouterr().out)["status"] == "FAILED"


def test_full_offline_smoke_exercises_real_models_and_s3_recovery():
    ml_root = Path(__file__).resolve().parents[1]
    environment = os.environ.copy()
    environment["PYTHONPATH"] = str(ml_root / "src")
    completed = subprocess.run([sys.executable, str(ml_root / "examples" / "automatic_retraining_smoke.py")],
        env=environment, capture_output=True, text=True, timeout=120)
    assert completed.returncode == 0, completed.stderr
    report = json.loads(completed.stdout)
    assert report["synthetic_data_only"] and report["active_preserved"]
    assert report["network_calls"] == report["live_aws_calls"] == 0
    assert report["run"]["status"] == "COMPLETED" and report["partial_publication"] == "PARTIAL_PUBLICATION"
    assert report["replay"] == "SKIPPED_ALREADY_PROCESSED" and report["no_new_data"] == "SKIPPED_NO_NEW_DATA"


def test_aws_preflight_accepts_only_verified_existing_history_on_selected_volume(config, monkeypatch):
    assert run(config).status == "COMPLETED"
    module = scheduler()
    volume_id = str(uuid4())
    root = config.dataset_path.parent
    marker = root / ".stocklab-retraining-volume.json"
    marker.write_text(json.dumps({"schema_version": 1, "volume_id": volume_id}))
    selected = replace(config, aws_enabled=True, upload_to_s3=True, persistent_root=root,
                       persistent_volume_id=volume_id)
    monkeypatch.setattr(module, "_nfs_mount", lambda path: path == root)
    before = selected.state_path.read_bytes(), selected.registry_path.read_bytes()
    module.validate_execution_context(selected)
    assert before == (selected.state_path.read_bytes(), selected.registry_path.read_bytes())
    marker.write_text(json.dumps({"schema_version": 1, "volume_id": str(uuid4())}))
    with pytest.raises(api().RetrainingConfigurationError):
        module.validate_execution_context(selected)
    marker.write_text(json.dumps({"schema_version": 1, "volume_id": volume_id}))
    selected.registry_path.unlink()
    with pytest.raises(api().RetrainingConfigurationError):
        module.validate_execution_context(selected)


@pytest.mark.parametrize("fs_type,mounted,matching,expected", [
    ("nfs4", True, True, True), ("nfs", True, True, False), ("ext4", True, True, False),
    ("nfs4", False, True, False), ("nfs4", True, False, False),
])
def test_mount_preflight_parses_kernel_mount_metadata(config, monkeypatch, fs_type, mounted, matching, expected):
    module = scheduler()
    root = config.dataset_path.parent
    mountpoint = str(root) if matching else str(root) + "-other"
    line = f"35 27 0:42 / {mountpoint} rw,relatime - {fs_type} fs-test:/ rw,vers=4.1\n"
    monkeypatch.setattr(module, "os", SimpleNamespace(name="posix", path=SimpleNamespace(ismount=lambda path: mounted)))
    monkeypatch.setattr(module, "Path", lambda path: SimpleNamespace(read_text=lambda: line))
    assert module._nfs_mount(root) is expected
