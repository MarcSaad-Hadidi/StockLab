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
from .contracts import ModelStatus, ModelVersionError, ModelVersionRecord
from .registry import ModelVersionRegistry
from .validation import canonical, decode_record, frame_digest, training_payload


def _validate_reference(record: ModelVersionRecord, registry_path: Path | None) -> None:
    if not isinstance(record, ModelVersionRecord):
        raise ModelVersionError("A registered ModelVersionRecord is required.")
    if record.model_type == "hist_gradient_boosting" and record.status is not ModelStatus.ARCHIVED:
        raise ModelVersionError("Experimental HGBT28 associations must remain Archived; activation is unsupported.")
    if registry_path is None:
        # Only explicitly trusted #76 cold-start contexts omit a local registry.
        decode_record(record.to_dict())
        return
    current = ModelVersionRegistry(registry_path).get_version(record.model_name, record.model_version)
    stored, supplied = current.to_dict(), record.to_dict()
    # Status is administrative and may change after a historical association.
    stored.pop("status")
    supplied.pop("status")
    if canonical(stored) != canonical(supplied):
        raise ModelVersionError("Version metadata differs from its permanent registered identity.")


def _boosted_policy_context_digest(predictions: pd.DataFrame, features: pd.DataFrame) -> str:
    from stocklab_ml.boosted_snapshot import _finite, _keys
    try:
        _keys(features)
        keys, columns = ["date", "symbol"], ["close_ma50", "close_ma200", "momentum_20"]
        rows = predictions[keys].merge(features[keys + columns], on=keys, how="left", validate="one_to_one")
        _finite(rows, columns)
        return frame_digest(rows)
    except (ValueError, TypeError, KeyError, AttributeError) as error:
        raise ModelVersionError("Every boosted prediction requires its exact finite keyed policy context.") from error


@dataclass(frozen=True)
class RegisteredModel:
    """In-memory result association, not a serialized artifact or binary provenance proof."""

    _result: object
    record: ModelVersionRecord
    _registry_path: Path

    def _validate(self) -> None:
        if self._registry_path is None:
            raise ModelVersionError("RegisteredModel requires its local registry.")
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
    _registry_path: Path | None
    _source_paths: tuple[Path, ...]
    _digest: str
    _policy_context_sha256: str | None

    def __init__(self, context: RegisteredModel, *, feature_dataframe: pd.DataFrame | None = None):
        from stocklab_ml.artifacts.serialization import LoadedModel
        if not isinstance(context, (RegisteredModel, LoadedModel)):
            raise ModelVersionError("A RegisteredModel or verified LoadedModel is required for versioned predictions.")
        context._validate()
        boosted = context.record.model_type == "hist_gradient_boosting"
        if boosted:
            if feature_dataframe is None:
                raise ModelVersionError("HGBT28 historical predictions require actual feature rows; no holdout is stored.")
            snapshot = context._boosted_snapshot if isinstance(context, LoadedModel) else context._result
            frame = snapshot.predict(feature_dataframe)
            sources = ((context.artifact_reference.artifact_path, context.artifact_reference.manifest_path)
                if isinstance(context, LoadedModel) else tuple(Path(name) for name in snapshot.metadata["sources_sha256"]))
        elif isinstance(context, LoadedModel):
            if feature_dataframe is None:
                raise ModelVersionError("Loaded models require actual target-free feature rows.")
            estimator = context.estimator
            frame = (predict_direction(estimator, feature_dataframe) if context.record.model_type == "logistic_regression"
                     else predict_random_forest_direction(estimator, feature_dataframe))
            sources = (context.artifact_reference.artifact_path, context.artifact_reference.manifest_path)
        elif feature_dataframe is None:
            frame = context._result.predictions
        elif context.record.model_type == "logistic_regression":
            frame = predict_direction(context._result.pipeline, feature_dataframe)
        else:
            frame = predict_random_forest_direction(context._result.model, feature_dataframe)
        if isinstance(context, RegisteredModel) and not boosted:
            source = context._result.source_path
            sources = (Path(source),) if source is not None else ()
        object.__setattr__(self, "_frame", frame.copy(deep=True))
        object.__setattr__(self, "record", context.record)
        object.__setattr__(self, "_registry_path", context._registry_path)
        object.__setattr__(self, "_source_paths", sources)
        object.__setattr__(self, "_digest", frame_digest(frame, include_index=True))
        object.__setattr__(self, "_policy_context_sha256",
            _boosted_policy_context_digest(frame, feature_dataframe) if boosted else None)
        self._validate()

    def _validate(self) -> None:
        _validate_reference(self.record, self._registry_path)
        try:
            if self.record.model_type == "hist_gradient_boosting":
                from stocklab_ml.boosted_snapshot import _scores
                _scores(self._frame)
            elif list(self._frame.columns) == PREDICTION_COLUMNS:
                _validate_predictions(self._frame)
            elif list(self._frame.columns) == REQUIRED_COLUMNS:
                _prediction_rows(self._frame)
            else:
                raise ModelVersionError("Exact historical or target-free prediction schema required.")
        except (ModelEvaluationError, TradingSignalError, ValueError) as error:
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

    def policy_decisions(self, features: pd.DataFrame) -> "VersionedBoostedDecisions":
        return VersionedBoostedDecisions(self, features)


@dataclass(frozen=True, init=False)
class VersionedBoostedDecisions:
    """Raw-score trend200 decisions bound to genuine HGBT28 versioned inference."""

    _frame: pd.DataFrame
    _predictions: VersionedPredictions
    _digest: str

    def __init__(self, predictions: VersionedPredictions, features: pd.DataFrame):
        if not isinstance(predictions, VersionedPredictions):
            raise ModelVersionError("Actual versioned HGBT28 predictions are required.")
        predictions._validate()
        if predictions.record.model_type != "hist_gradient_boosting":
            raise ModelVersionError("The snapshot trend policy supports HGBT28 only.")
        if _boosted_policy_context_digest(predictions.predictions, features) != predictions._policy_context_sha256:
            raise ModelVersionError("Policy context differs from the features that produced these boosted scores.")
        from stocklab_ml.boosted_snapshot import policy_decisions
        frame = policy_decisions(predictions.predictions, features)
        object.__setattr__(self, "_frame", frame.copy(deep=True))
        object.__setattr__(self, "_predictions", predictions)
        object.__setattr__(self, "_digest", frame_digest(frame, include_index=True))

    @property
    def record(self) -> ModelVersionRecord:
        return self._predictions.record

    def _validate(self):
        self._predictions._validate()
        if frame_digest(self._frame, include_index=True) != self._digest:
            raise ModelVersionError("The versioned boosted decision snapshot was modified.")

    @property
    def decisions(self) -> pd.DataFrame:
        self._validate()
        return self._frame.copy(deep=True)

    def to_dataframe(self) -> pd.DataFrame:
        frame = self.decisions
        frame.insert(2, "model_name", self.record.model_name)
        frame.insert(3, "model_version", self.record.model_version)
        return frame


@dataclass(frozen=True, init=False)
class VersionedConfidenceDecisions:
    """An unchanged #65 decision batch associated with its exact prediction version."""

    _frame: pd.DataFrame
    _report: object
    record: ModelVersionRecord
    _registry_path: Path | None
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
