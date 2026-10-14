"""Scheduling metadata and read-only AWS preflight; never deploy infrastructure."""

import os
from pathlib import Path

from stocklab_ml.versioning import ModelVersionRegistry
from .contracts import RetrainingConfigurationError
from .state import read_json, read_state


def schedule_expression(config) -> str:
    interval = config.check_interval_days
    return f"rate({interval} {'day' if interval == 1 else 'days'})"


def _nfs_mount(root: Path) -> bool:
    if os.name != "posix" or not os.path.ismount(root):
        return False
    try:
        for line in Path("/proc/self/mountinfo").read_text().splitlines():
            left, right = line.split(" - ", 1)
            if left.split()[4] == str(root) and right.split()[0] == "nfs4":
                return True
    except (OSError, ValueError, IndexError):
        return False
    return False


def validate_execution_context(config) -> None:
    in_ecs = os.environ.get("AWS_EXECUTION_ENV", "").startswith("AWS_ECS") or any(
        os.environ.get(name) for name in ["ECS_CONTAINER_METADATA_URI", "ECS_CONTAINER_METADATA_URI_V4"])
    if in_ecs and not config.aws_enabled:
        raise RetrainingConfigurationError("ECS execution must explicitly enable persistent AWS mode.")
    if not config.aws_enabled:
        return
    if not _nfs_mount(config.persistent_root):
        raise RetrainingConfigurationError("AWS requires the configured root to be an actual mounted NFSv4 shared volume.")
    marker = read_json(config.persistent_root / ".stocklab-retraining-volume.json", limit=4096)
    if marker != {"schema_version": 1, "volume_id": config.persistent_volume_id}:
        raise RetrainingConfigurationError("Persistent volume identity is missing or different; never initialize ephemeral state.")
    if not all(path.is_file() for path in [config.dataset_path, config.registry_path, config.state_path]):
        raise RetrainingConfigurationError("AWS bootstrap must pre-provision dataset, registry and journal on the shared volume.")
    if not ModelVersionRegistry(config.registry_path)._read() or read_state(config.state_path) is None:
        raise RetrainingConfigurationError("AWS requires verified pre-existing registry history and journal.")
