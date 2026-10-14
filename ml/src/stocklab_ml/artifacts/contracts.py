"""Artifact references and controlled persistence errors; no credentials or training."""

from dataclasses import dataclass
from pathlib import Path

from stocklab_ml.modeling.contracts import ModelingError

DEFAULT_MAX_ARTIFACT_BYTES = 100 * 1024 * 1024
MAX_MANIFEST_BYTES = 1024 * 1024


class ModelArtifactError(ModelingError):
    """Invalid artifact operation or missing explicit source trust."""


class ArtifactNotFoundError(ModelArtifactError):
    """The exact published artifact or manifest is absent."""


class ArtifactConflictError(ModelArtifactError):
    """A version already identifies different content or a concurrent write won."""


class ArtifactIntegrityError(ModelArtifactError):
    """Invalid manifest, identity, metadata, size or serialized bytes."""


class ArtifactCompatibilityError(ModelArtifactError):
    """The runtime differs from the artifact's supported environment."""


class ArtifactStorageError(ModelArtifactError):
    """Local or AWS I/O failed; no successful publication is claimed."""


@dataclass(frozen=True)
class LocalModelArtifact:
    artifact_path: Path
    manifest_path: Path


@dataclass(frozen=True)
class S3ModelArtifactReference:
    bucket: str
    region: str
    artifact_key: str
    manifest_key: str
    artifact_sha256: str
