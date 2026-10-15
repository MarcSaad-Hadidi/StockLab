"""Validated immutable configuration and safe public run results for #77."""

from dataclasses import dataclass
import math
from pathlib import Path
import re

import pandas as pd

from stocklab_ml.data.validation import SYMBOL_PATTERN
from stocklab_ml.versioning.validation import validate_uuid

ML_ROOT = Path(__file__).resolve().parents[3]
MODEL_NAMES = {"logistic_regression": "logistic_regression_baseline",
               "random_forest": "random_forest_baseline"}


class RetrainingError(Exception):
    """Controlled one-shot retraining error; messages never include provider payloads."""


class RetrainingConfigurationError(RetrainingError):
    """Invalid configuration or an unsafe AWS execution context."""


class RetrainingDataError(RetrainingError):
    """Missing bootstrap, conflicting history, or unusable incremental data."""


class RetrainingStateError(RetrainingError):
    """Corrupt/missing recovery state; no automatic reset."""


class RetrainingBusyError(RetrainingError):
    """Another process owns the same journal lock."""


@dataclass(frozen=True)
class RetrainingConfig:
    symbols: tuple[str, ...]
    dataset_path: Path
    model_families: tuple[str, ...] = ("logistic_regression", "random_forest")
    enabled: bool = True
    check_interval_days: int = 3
    minimum_new_sessions: int = 1
    test_fraction: float = .20
    state_path: Path = ML_ROOT / "models" / "retraining" / "state.json"
    registry_path: Path = ML_ROOT / "models" / "registry.json"
    artifacts_path: Path = ML_ROOT / "models" / "artifacts"
    overlap_days: int = 7
    maximum_refresh_days: int = 90
    aws_enabled: bool = False
    upload_to_s3: bool = False
    dry_run: bool = False
    persistent_root: Path | None = None
    persistent_volume_id: str | None = None

    def __post_init__(self):
        for name in ["enabled", "aws_enabled", "upload_to_s3", "dry_run"]:
            if type(getattr(self, name)) is not bool:
                raise RetrainingConfigurationError("Boolean configuration fields require actual booleans.")
        for name in ["check_interval_days", "minimum_new_sessions", "overlap_days", "maximum_refresh_days"]:
            if type(getattr(self, name)) is not int or not 1 <= getattr(self, name) <= 4998:
                raise RetrainingConfigurationError("Intervals and counts require positive bounded integers.")
        if self.overlap_days >= self.maximum_refresh_days:
            raise RetrainingConfigurationError("The overlap must be shorter than the maximum refresh window.")
        if (type(self.test_fraction) not in (int, float) or not math.isfinite(self.test_fraction)
                or not 0 < self.test_fraction < 1):
            raise RetrainingConfigurationError("test_fraction must be finite and strictly between zero and one.")
        for name in ["symbols", "model_families"]:
            values = getattr(self, name)
            if (not isinstance(values, (list, tuple)) or not values
                    or any(not isinstance(value, str) for value in values)
                    or len(set(values)) != len(values)):
                raise RetrainingConfigurationError("Explicit distinct symbols and model families are required.")
            object.__setattr__(self, name, tuple(sorted(values)))
        if (any(re.fullmatch(SYMBOL_PATTERN, symbol) is None for symbol in self.symbols)
                or any(family not in MODEL_NAMES for family in self.model_families)):
            raise RetrainingConfigurationError("Unsupported symbol or model family.")
        for name in ["dataset_path", "state_path", "registry_path", "artifacts_path", "persistent_root"]:
            value = getattr(self, name)
            if value is None and name == "persistent_root":
                continue
            if (not isinstance(value, (str, Path)) or not str(value).strip()
                    or ".." in Path(value).parts):
                raise RetrainingConfigurationError("Explicit safe filesystem paths are required.")
            try:
                path = Path(value).resolve()
            except (OSError, ValueError, RuntimeError):
                raise RetrainingConfigurationError("Could not resolve configuration paths.") from None
            object.__setattr__(self, name, path)
        files = [self.dataset_path, self.state_path, self.registry_path,
                 self.state_path.with_suffix(self.state_path.suffix + ".lock").resolve(),
                 self.registry_path.with_suffix(self.registry_path.suffix + ".lock").resolve()]
        if (len(set(files)) != len(files) or any(path.is_dir() for path in files)
                or any(path.exists() and path.stat().st_nlink != 1 for path in files)
                or self.artifacts_path.is_file()
                or any(path.is_relative_to(self.artifacts_path) for path in files)
                or any(self.artifacts_path.is_relative_to(path) for path in files)
                or any(path.is_relative_to(self.state_path.parent / subdir)
                       for path in files for subdir in ["snapshots", "ingestion"])):
            raise RetrainingConfigurationError("Dataset, registry, journal, lock and artifact paths must not alias.")
        if self.persistent_volume_id is not None:
            try:
                validate_uuid(self.persistent_volume_id)
            except Exception:
                raise RetrainingConfigurationError("A canonical persistent volume UUID is required.") from None
        if self.aws_enabled:
            if not self.persistent_root or not self.persistent_volume_id or not self.upload_to_s3:
                raise RetrainingConfigurationError("AWS requires explicit persistent storage identity and S3 publication.")
            if any(not path.is_relative_to(self.persistent_root) for path in files + [self.artifacts_path]):
                raise RetrainingConfigurationError("All AWS state, datasets, registry and artifacts must share the persistent root.")


@dataclass(frozen=True)
class RetrainingDependencies:
    # A local frame is the explicit offline source, never inferred from examples.
    local_data: pd.DataFrame | None = None
    s3_store: object | None = None


@dataclass(frozen=True)
class RetrainingRunResult:
    status: str
    run_id: str | None
    report: dict

    def to_dict(self) -> dict:
        return {"status": self.status, "run_id": self.run_id, "report": self.report}
