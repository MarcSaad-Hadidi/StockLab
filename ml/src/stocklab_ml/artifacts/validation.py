"""Strict manifest and fitted-estimator checks, reusing the #75 record contract."""

import hashlib
from importlib.metadata import version
import json
import platform
import re

import numpy as np
from sklearn.ensemble import RandomForestClassifier
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler
from sklearn.utils.validation import check_is_fitted

from stocklab_ml.features import FEATURE_COLUMNS
from stocklab_ml.versioning import ModelVersionError, ModelVersionRegistry
from stocklab_ml.versioning.registry import _unique_keys
from stocklab_ml.versioning.validation import canonical, decode_record, validate_name, validate_version
from .contracts import (
    ArtifactCompatibilityError, ArtifactIntegrityError, DEFAULT_MAX_ARTIFACT_BYTES,
    MAX_MANIFEST_BYTES, ModelArtifactError,
)

MANIFEST_FIELDS = {
    "schema_version", "model_name", "model_version", "training_run_id", "model_type",
    "serialization_format", "artifact_sha256", "artifact_size_bytes", "artifact_filename",
    "environment", "model_record", "metadata_sha256",
}


def runtime_environment() -> dict:
    return {"python": platform.python_version(), "scikit_learn": version("scikit-learn"),
            "numpy": version("numpy"), "scipy": version("scipy"), "joblib": version("joblib")}


def immutable_metadata(record) -> dict:
    payload = record.to_dict()
    payload.pop("status")
    return payload


def metadata_digest(record) -> str:
    return hashlib.sha256(canonical(immutable_metadata(record)).encode("utf-8")).hexdigest()


def validate_limit(limit: int) -> None:
    if type(limit) is not int or limit < 1:
        raise ModelArtifactError("max_artifact_bytes must be a positive integer.")


def resolve_registry(registry, allow_without_registry):
    if type(allow_without_registry) is not bool:
        raise ModelArtifactError("allow_without_registry must be boolean.")
    if registry is not None:
        if not isinstance(registry, ModelVersionRegistry):
            raise ModelArtifactError("A ModelVersionRegistry is required.")
        return registry
    default = ModelVersionRegistry()
    if default.path.exists() or not allow_without_registry:
        return default
    return None


def manifest_bytes(manifest: dict) -> bytes:
    # Preserve the training report's ordered feature-importances map (#75).
    return (json.dumps(manifest, ensure_ascii=False, allow_nan=False, indent=2) + "\n").encode("utf-8")


def decode_manifest(content: bytes, *, model_name: str, model_version: str, registry,
                    max_artifact_bytes: int = DEFAULT_MAX_ARTIFACT_BYTES):
    validate_limit(max_artifact_bytes)
    try:
        validate_name(model_name)
        validate_version(model_version)
        if len(content) > MAX_MANIFEST_BYTES:
            raise ArtifactIntegrityError("Manifest exceeds the size limit.")
        payload = json.loads(content.decode("utf-8"), object_pairs_hook=_unique_keys)
        if (not isinstance(payload, dict) or set(payload) != MANIFEST_FIELDS
                or type(payload["schema_version"]) is not int or payload["schema_version"] != 1
                or payload["serialization_format"] != "joblib" or payload["artifact_filename"] != "model.joblib"):
            raise ArtifactIntegrityError("Invalid or unsupported artifact manifest schema.")
        record = decode_record(payload["model_record"])
        if (record.model_name != model_name or record.model_version != model_version
                or any(payload[key] != getattr(record, key) for key in
                       ["model_name", "model_version", "training_run_id", "model_type"])):
            raise ArtifactIntegrityError("Manifest does not identify the exact requested model/version/run.")
        for key in ["artifact_sha256", "metadata_sha256"]:
            if not isinstance(payload[key], str) or re.fullmatch(r"[0-9a-f]{64}", payload[key]) is None:
                raise ArtifactIntegrityError("Invalid SHA-256 in artifact manifest.")
        if payload["metadata_sha256"] != metadata_digest(record):
            raise ArtifactIntegrityError("Manifest immutable metadata digest differs.")
        size = payload["artifact_size_bytes"]
        if type(size) is not int or not 0 < size <= max_artifact_bytes:
            raise ArtifactIntegrityError("Invalid artifact size or configured limit exceeded.")
        environment = payload["environment"]
        current = runtime_environment()
        if (not isinstance(environment, dict) or set(environment) != set(current)
                or any(not isinstance(value, str) or not value or len(value) > 64 for value in environment.values())):
            raise ArtifactIntegrityError("Invalid artifact runtime metadata.")
        if re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", environment["python"]) is None:
            raise ArtifactIntegrityError("Python runtime metadata must contain a complete numeric version.")
        if (environment["python"].split(".")[:2] != current["python"].split(".")[:2]
                or any(environment[key] != current[key] for key in current if key != "python")):
            raise ArtifactCompatibilityError("Artifact requires the recorded Python major/minor and exact package versions.")
        if registry is not None:
            registered = registry.get_version(model_name, model_version)
            if canonical(immutable_metadata(registered)) != canonical(immutable_metadata(record)):
                raise ArtifactIntegrityError("Artifact metadata conflicts with the local registered identity.")
            record = registered  # Preserve the local administrative status, without writing it.
        return payload, record
    except (ModelVersionError, ValueError, TypeError, KeyError, UnicodeError, OverflowError, RecursionError) as error:
        raise ArtifactIntegrityError("Invalid artifact manifest or registered reference.") from error


def verify_bytes(data: bytes, manifest: dict) -> None:
    if (len(data) != manifest["artifact_size_bytes"]
            or hashlib.sha256(data).hexdigest() != manifest["artifact_sha256"]):
        raise ArtifactIntegrityError("Artifact size or SHA-256 differs from the verified manifest.")


def validate_estimator(estimator, record) -> None:
    """Validate the supported fitted object itself, without inventing a training result."""
    try:
        report = record.to_dict()["training_report"]
        if record.model_type == "logistic_regression":
            if (type(estimator) is not Pipeline or list(estimator.named_steps) != ["scaler", "classifier"]
                    or type(estimator.named_steps["scaler"]) is not StandardScaler
                    or type(estimator.named_steps["classifier"]) is not LogisticRegression):
                raise ValueError
            scaler, classifier = estimator.named_steps["scaler"], estimator.named_steps["classifier"]
            check_is_fitted(scaler)
            if (scaler.get_params(deep=False) != StandardScaler().get_params(deep=False)
                    or scaler.n_samples_seen_ != report["split"]["train_rows"]
                    or classifier.n_iter_.tolist() != report["iterations"]):
                raise ValueError
            for values in [scaler.mean_, scaler.var_, scaler.scale_]:
                if np.asarray(values).shape != (len(FEATURE_COLUMNS),) or not np.isfinite(values).all():
                    raise ValueError
            if (not (scaler.scale_ > 0).all() or not (scaler.var_ >= 0).all()
                    or classifier.coef_.shape != (1, len(FEATURE_COLUMNS))
                    or classifier.intercept_.shape != (1,)
                    or not np.isfinite(classifier.coef_).all() or not np.isfinite(classifier.intercept_).all()):
                raise ValueError
        elif record.model_type == "random_forest":
            if type(estimator) is not RandomForestClassifier:
                raise ValueError
            classifier = estimator
            check_is_fitted(classifier)
            if len(classifier.estimators_) != report["hyperparameters"]["n_estimators"]:
                raise ValueError
            for tree in classifier.estimators_:
                check_is_fitted(tree)
                if tree.n_features_in_ != len(FEATURE_COLUMNS) or tree.tree_.node_count < 1:
                    raise ValueError
            if dict(zip(FEATURE_COLUMNS, classifier.feature_importances_.tolist(), strict=True)) != report["feature_importances"]:
                raise ValueError
        else:
            raise ValueError
        check_is_fitted(classifier)
        if (list(estimator.feature_names_in_) != FEATURE_COLUMNS
                or classifier.n_features_in_ != len(FEATURE_COLUMNS)
                or np.asarray(classifier.classes_).shape != (2,)
                or not np.array_equal(classifier.classes_, [0, 1])
                or canonical(classifier.get_params(deep=False)) != canonical(report["hyperparameters"])):
            raise ValueError
    except (AttributeError, TypeError, ValueError, KeyError, OverflowError, ModelVersionError) as error:
        raise ArtifactIntegrityError("Fitted estimator does not match the registered model contract.") from error
