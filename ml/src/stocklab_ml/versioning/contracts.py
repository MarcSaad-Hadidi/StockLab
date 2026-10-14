"""Permanent model identities and deeply immutable training metadata."""

from collections.abc import Mapping
from dataclasses import asdict, dataclass
from datetime import datetime
from enum import StrEnum
from types import MappingProxyType

from stocklab_ml.modeling.contracts import ModelEvaluationMetrics, ModelingError


class ModelVersionError(ModelingError):
    """Invalid metadata, association, or unsafe local registry operation."""


class TrainingRunConflictError(ModelVersionError):
    """An existing training UUID was submitted with different metadata."""


class ModelVersionNotFoundError(ModelVersionError):
    """The exact requested model/version does not exist."""


class RegistryBusyError(ModelVersionError):
    """Another local writer holds the registry lock; retry explicitly."""


class ModelStatus(StrEnum):
    ACTIVE = "Active"
    ARCHIVED = "Archived"


def _freeze(value):
    if isinstance(value, Mapping):
        return MappingProxyType({key: _freeze(item) for key, item in value.items()})
    if isinstance(value, (list, tuple)):
        return tuple(_freeze(item) for item in value)
    return value


def _thaw(value):
    if isinstance(value, Mapping):
        return {key: _thaw(item) for key, item in value.items()}
    if isinstance(value, tuple):
        return [_thaw(item) for item in value]
    return value


@dataclass(frozen=True)
class ModelVersionRecord:
    model_name: str
    model_version: str
    version_number: int
    training_run_id: str
    model_type: str
    trained_at_utc: datetime
    registered_at_utc: datetime
    status: ModelStatus
    training_report: Mapping
    metrics: ModelEvaluationMetrics
    holdout_sha256: str

    def __post_init__(self):
        object.__setattr__(self, "training_report", _freeze(self.training_report))

    @property
    def training_date(self) -> str:
        return self.trained_at_utc.date().isoformat()

    @property
    def feature_columns(self) -> tuple[str, ...]:
        return self.training_report["feature_columns"]

    @property
    def target_name(self) -> str:
        return self.training_report["target_name"]

    @property
    def hyperparameters(self) -> Mapping:
        return self.training_report["hyperparameters"]

    @property
    def train_start(self) -> str:
        return self.training_report["split"]["train_start"]

    @property
    def train_end(self) -> str:
        return self.training_report["split"]["train_end"]

    @property
    def test_start(self) -> str:
        return self.training_report["split"]["test_start"]

    @property
    def test_end(self) -> str:
        return self.training_report["split"]["test_end"]

    @property
    def split_date(self) -> str:
        return self.training_report["split"]["split_date"]

    def to_dict(self) -> dict:
        """Return a detached JSON-safe snapshot, without an estimator or paths."""
        return {
            "model_name": self.model_name, "model_version": self.model_version,
            "version_number": self.version_number, "training_run_id": self.training_run_id,
            "model_type": self.model_type, "trained_at_utc": self.trained_at_utc.isoformat(),
            "registered_at_utc": self.registered_at_utc.isoformat(), "status": self.status.value,
            "training_report": _thaw(self.training_report), "metrics": asdict(self.metrics),
            "holdout_sha256": self.holdout_sha256,
        }
