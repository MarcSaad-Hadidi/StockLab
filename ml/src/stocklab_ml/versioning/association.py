"""Bind existing estimators to exact registered identities; preserve #61-#65 tables."""

from copy import deepcopy
from dataclasses import dataclass
from pathlib import Path

import pandas as pd

from stocklab_ml.modeling import predict_direction, predict_random_forest_direction
from stocklab_ml.modeling.contracts import ModelEvaluationError
from stocklab_ml.modeling.evaluation import PREDICTION_COLUMNS, _validate_predictions
from stocklab_ml.signals import ConfidenceSignalsResult
from stocklab_ml.signals.confidence import _validate_confidence_result
from stocklab_ml.signals.contracts import ConfidenceScoreError, TradingSignalError
from stocklab_ml.signals.validation import REQUIRED_COLUMNS, _prediction_rows
from .contracts import ModelVersionError, ModelVersionRecord
from .registry import ModelVersionRegistry
from .validation import canonical, frame_digest, training_payload


def _validate_reference(record: ModelVersionRecord, registry_path: Path) -> None:
    if not isinstance(record, ModelVersionRecord):
        raise ModelVersionError("A registered ModelVersionRecord is required.")
    current = ModelVersionRegistry(registry_path).get_version(record.model_name, record.model_version)
    stored, supplied = current.to_dict(), record.to_dict()
    # Status is administrative and may change after a historical association.
    stored.pop("status")
    supplied.pop("status")
    if canonical(stored) != canonical(supplied):
        raise ModelVersionError("Version metadata differs from its permanent registered identity.")


@dataclass(frozen=True)
class RegisteredModel:
    """In-memory result association, not a serialized artifact or binary provenance proof."""

    _result: object
    record: ModelVersionRecord
    _registry_path: Path

    def _validate(self) -> None:
        _validate_reference(self.record, self._registry_path)
        payload = training_payload(self._result)
        stored = self.record.to_dict()
        if canonical(payload) != canonical({key: stored[key] for key in payload}):
            raise ModelVersionError("The bound estimator/result no longer matches its registered training metadata.")

    def historical_predictions(self) -> "VersionedPredictions":
        return VersionedPredictions(self)

    def predict(self, feature_dataframe: pd.DataFrame) -> "VersionedPredictions":
        return VersionedPredictions(self, feature_dataframe=feature_dataframe)


@dataclass(frozen=True, init=False)
class VersionedPredictions:
    """Copied historical rows or actual target-free inference through a registered context.

    Construction requires the supplied result/model association, never an arbitrary
    prediction DataFrame plus a version string. Public DataFrame access returns copies.
    """

    _frame: pd.DataFrame
    record: ModelVersionRecord
    _registry_path: Path
    _source_paths: tuple[Path, ...]
    _digest: str

    def __init__(self, context: RegisteredModel, *, feature_dataframe: pd.DataFrame | None = None):
        if not isinstance(context, RegisteredModel):
            raise ModelVersionError("A RegisteredModel context is required for versioned predictions.")
        context._validate()
        if feature_dataframe is None:
            frame = context._result.predictions
        elif context.record.model_type == "logistic_regression":
            frame = predict_direction(context._result.pipeline, feature_dataframe)
        else:
            frame = predict_random_forest_direction(context._result.model, feature_dataframe)
        source = context._result.source_path
        object.__setattr__(self, "_frame", frame.copy(deep=True))
        object.__setattr__(self, "record", context.record)
        object.__setattr__(self, "_registry_path", context._registry_path)
        object.__setattr__(self, "_source_paths", (Path(source),) if source is not None else ())
        object.__setattr__(self, "_digest", frame_digest(frame, include_index=True))
        self._validate()

    def _validate(self) -> None:
        _validate_reference(self.record, self._registry_path)
        try:
            if list(self._frame.columns) == PREDICTION_COLUMNS:
                _validate_predictions(self._frame)
            elif list(self._frame.columns) == REQUIRED_COLUMNS:
                _prediction_rows(self._frame)
            else:
                raise ModelVersionError("Exact historical or target-free prediction schema required.")
        except (ModelEvaluationError, TradingSignalError) as error:
            raise ModelVersionError("Invalid versioned prediction batch.") from error
        if frame_digest(self._frame, include_index=True) != self._digest:
            raise ModelVersionError("Versioned prediction snapshot was modified.")

    @property
    def predictions(self) -> pd.DataFrame:
        return self._frame.copy(deep=True)

    def to_dataframe(self) -> pd.DataFrame:
        self._validate()
        frame = self.predictions
        position = frame.columns.get_loc("symbol") + 1
        frame.insert(position, "model_name", pd.array([self.record.model_name] * len(frame), dtype="string"))
        frame.insert(position + 1, "model_version", pd.array([self.record.model_version] * len(frame), dtype="string"))
        return frame


@dataclass(frozen=True, init=False)
class VersionedConfidenceDecisions:
    """An unchanged #65 decision batch associated with its exact prediction version."""

    _frame: pd.DataFrame
    _report: object
    record: ModelVersionRecord
    _registry_path: Path
    _source_paths: tuple[Path, ...]
    _digest: str

    def __init__(self, decisions: ConfidenceSignalsResult, *, predictions: VersionedPredictions):
        if not isinstance(predictions, VersionedPredictions):
            raise ModelVersionError("VersionedPredictions from a registered model are required.")
        predictions._validate()
        try:
            _validate_confidence_result(decisions)
            expected = _prediction_rows(predictions.predictions).sort_values(["date", "symbol"]).reset_index(drop=True)
            supplied = decisions.signals[REQUIRED_COLUMNS].sort_values(["date", "symbol"]).reset_index(drop=True)
            if (decisions.report.model_name != predictions.record.model_name or not expected.equals(supplied)):
                raise ModelVersionError("Decisions must match the exact bound prediction batch and model_name.")
        except (ConfidenceScoreError, TradingSignalError) as error:
            raise ModelVersionError("Invalid confidence decision batch.") from error
        object.__setattr__(self, "_frame", decisions.signals.copy(deep=True))
        object.__setattr__(self, "_report", deepcopy(decisions.report))
        object.__setattr__(self, "record", predictions.record)
        object.__setattr__(self, "_registry_path", predictions._registry_path)
        object.__setattr__(self, "_source_paths", predictions._source_paths)
        object.__setattr__(self, "_digest", frame_digest(decisions.signals, include_index=True))

    @property
    def result(self) -> ConfidenceSignalsResult:
        return ConfidenceSignalsResult(self._frame.copy(deep=True), deepcopy(self._report))

    def _validate(self) -> None:
        _validate_reference(self.record, self._registry_path)
        try:
            _validate_confidence_result(self.result)
        except ConfidenceScoreError as error:
            raise ModelVersionError("Invalid versioned confidence decisions.") from error
        if (self._report.model_name != self.record.model_name
                or frame_digest(self._frame, include_index=True) != self._digest):
            raise ModelVersionError("Versioned decision snapshot or model identity was modified.")

    def to_dataframe(self) -> pd.DataFrame:
        self._validate()
        frame = self.result.signals
        frame.insert(3, "model_version", pd.array([self.record.model_version] * len(frame), dtype="string"))
        return frame


def version_confidence_decisions(
    decisions: ConfidenceSignalsResult, *, predictions: VersionedPredictions,
) -> VersionedConfidenceDecisions:
    return VersionedConfidenceDecisions(decisions, predictions=predictions)
