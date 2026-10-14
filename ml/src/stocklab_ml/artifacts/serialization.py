"""Persist the complete fitted estimator; load only trusted, verified bytes."""

from dataclasses import dataclass
import hashlib
import io
from pathlib import Path
import warnings

import joblib
import pandas as pd
from sklearn.exceptions import InconsistentVersionWarning

from stocklab_ml.versioning import ModelVersionRecord, RegisteredModel, VersionedPredictions
from stocklab_ml.versioning.association import _validate_reference
from .contracts import (
    ArtifactCompatibilityError, ArtifactIntegrityError, ArtifactStorageError,
    DEFAULT_MAX_ARTIFACT_BYTES, LocalModelArtifact, ModelArtifactError,
)
from .local_storage import artifact_paths, publish_local, read_local
from .validation import (
    metadata_digest, resolve_registry, runtime_environment, validate_estimator, validate_limit,
)


@dataclass(frozen=True)
class LoadedModel:
    """Verified estimator plus exact version; no fabricated holdout or training result."""

    estimator: object
    record: ModelVersionRecord
    artifact_sha256: str
    artifact_reference: LocalModelArtifact
    _registry_path: Path | None

    def _validate(self) -> None:
        _validate_reference(self.record, self._registry_path)
        validate_estimator(self.estimator, self.record)

    def predict(self, feature_dataframe: pd.DataFrame) -> VersionedPredictions:
        return VersionedPredictions(self, feature_dataframe=feature_dataframe)


def save_model_artifact(context: RegisteredModel, *, output_dir=None, overwrite=False,
                        max_artifact_bytes=DEFAULT_MAX_ARTIFACT_BYTES) -> LocalModelArtifact:
    if not isinstance(context, RegisteredModel):
        raise ModelArtifactError("A validated RegisteredModel is required to save an artifact.")
    if type(overwrite) is not bool:
        raise ModelArtifactError("overwrite must be boolean.")
    validate_limit(max_artifact_bytes)
    context._validate()
    estimator = context._result.pipeline if context.record.model_type == "logistic_regression" else context._result.model
    validate_estimator(estimator, context.record)
    try:
        stream = io.BytesIO()
        joblib.dump(estimator, stream, protocol=5, compress=0)
        data = stream.getvalue()
    except Exception as error:
        raise ArtifactStorageError("Could not serialize the fitted estimator.") from error
    if not 0 < len(data) <= max_artifact_bytes:
        raise ArtifactIntegrityError("Serialized artifact exceeds the configured size limit.")
    manifest = {
        "schema_version": 1, "model_name": context.record.model_name,
        "model_version": context.record.model_version, "training_run_id": context.record.training_run_id,
        "model_type": context.record.model_type, "serialization_format": "joblib",
        "artifact_sha256": hashlib.sha256(data).hexdigest(), "artifact_size_bytes": len(data),
        "artifact_filename": "model.joblib", "environment": runtime_environment(),
        "model_record": context.record.to_dict(), "metadata_sha256": metadata_digest(context.record),
    }
    artifact = artifact_paths(context.record.model_name, context.record.model_version, output_dir)
    sources = tuple(path for path in [context._registry_path, context._result.source_path] if path is not None)
    from stocklab_ml.versioning import ModelVersionRegistry
    return publish_local(artifact, manifest, data, registry=ModelVersionRegistry(context._registry_path),
                         reuse=overwrite, sources=sources, max_artifact_bytes=max_artifact_bytes)


def load_model_artifact(artifact: LocalModelArtifact, *, registry=None, trusted_source=False,
                        allow_without_registry=False, max_artifact_bytes=DEFAULT_MAX_ARTIFACT_BYTES) -> LoadedModel:
    """Trust is explicit: SHA-256 cannot authenticate a replaced manifest and binary."""
    if trusted_source is not True:
        raise ModelArtifactError("Explicitly trust the private source before loading executable joblib content.")
    registry = resolve_registry(registry, allow_without_registry)
    manifest, record, data = read_local(artifact, registry=registry, max_artifact_bytes=max_artifact_bytes)
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("error", InconsistentVersionWarning)
            # Deserialize these exact verified bytes, never reopen a possibly replaced path.
            estimator = joblib.load(io.BytesIO(data))
    except InconsistentVersionWarning as error:
        raise ArtifactCompatibilityError("Serialized estimator reports an incompatible sklearn version.") from error
    except Exception as error:
        raise ArtifactIntegrityError("Verified bytes could not be loaded as a supported fitted estimator.") from error
    validate_estimator(estimator, record)
    return LoadedModel(estimator, record, manifest["artifact_sha256"], artifact,
                       registry.path if registry is not None else None)
