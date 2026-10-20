"""Explicit immutable promotion requests, decisions, and controlled failures."""

from collections.abc import Mapping
from dataclasses import dataclass, field
from datetime import datetime

from stocklab_ml.backtesting import BacktestConfig
from stocklab_ml.data.models import CleanedDatasetResult
from stocklab_ml.versioning.contracts import _freeze, _thaw


class ModelPromotionError(Exception):
    """Invalid request or unreliable promotion evidence; no success is implied."""


class PromotionConflictError(ModelPromotionError):
    """An evaluation identity or its expected registry state has changed."""


class PromotionAuditError(ModelPromotionError):
    """Audit publication or integrity validation failed; never reset history."""


class PromotionRecoveryError(PromotionAuditError):
    """A durable intent has no conclusive application record; never replay it."""


@dataclass(frozen=True)
class ModelPromotionPolicy:
    minimum_evaluation_rows: int = 30
    minimum_accuracy: float = .50
    minimum_f1: float = .50
    minimum_return_improvement: float = 0.0
    maximum_drawdown_increase: float = 0.0
    require_benchmark_comparison: bool = True
    require_benchmark_outperformance: bool = False
    minimum_sample_symbols: int = 1
    minimum_forward_rows: int = 0

    def __post_init__(self):
        from .validation import validate_policy
        validate_policy(self)


@dataclass(frozen=True)
class ModelPromotionRequest:
    model_name: str
    candidate_version: str
    expected_active_version: str | None
    evaluation_dataset: CleanedDatasetResult
    promotion_policy: ModelPromotionPolicy
    evaluation_timestamp_utc: datetime
    evaluation_id: str
    evaluation_start: str
    evaluation_end: str
    initialize: bool = False
    backtest_config: BacktestConfig = field(default_factory=BacktestConfig)

    def __post_init__(self):
        from .validation import validate_request
        validate_request(self)


@dataclass(frozen=True)
class PromotionDecision:
    evaluation_id: str
    model_name: str
    active_model_version: str | None
    candidate_model_version: str
    decision: str
    reason_codes: tuple[str, ...]
    policy_snapshot: Mapping
    request_sha256: str
    evaluated_at_utc: str
    evidence: Mapping

    def __post_init__(self):
        from .validation import validate_decision
        object.__setattr__(self, "reason_codes", tuple(self.reason_codes))
        object.__setattr__(self, "policy_snapshot", _freeze(self.policy_snapshot))
        object.__setattr__(self, "evidence", _freeze(self.evidence))
        validate_decision(self)

    def to_dict(self):
        return {"evaluation_id": self.evaluation_id, "model_name": self.model_name,
                "active_model_version": self.active_model_version,
                "candidate_model_version": self.candidate_model_version, "decision": self.decision,
                "reason_codes": list(self.reason_codes), "policy_snapshot": _thaw(self.policy_snapshot),
                "request_sha256": self.request_sha256, "evaluated_at_utc": self.evaluated_at_utc,
                "evidence": _thaw(self.evidence)}

    @property
    def evidence_sha256(self):
        from .validation import digest
        return digest(self.to_dict())


@dataclass(frozen=True)
class PromotionApplication:
    evaluation_id: str
    evaluation_sha256: str
    model_name: str
    candidate_version: str
    expected_active_version: str | None
    status: str
    applied_at_utc: str
