"""Bounded local artifact reads and atomic no-clobber publication of each file."""

import os
from pathlib import Path
import tempfile

from stocklab_ml.data.models import StorageError
from stocklab_ml.data.storage import cleaning_paths
from stocklab_ml.versioning.validation import canonical, validate_name, validate_version
from .contracts import (
    ArtifactConflictError, ArtifactIntegrityError, ArtifactNotFoundError,
    ArtifactStorageError, DEFAULT_MAX_ARTIFACT_BYTES, LocalModelArtifact, MAX_MANIFEST_BYTES,
)
from .validation import decode_manifest, manifest_bytes, verify_bytes

DEFAULT_ARTIFACTS_DIR = Path(__file__).resolve().parents[3] / "models" / "artifacts"


def artifact_paths(model_name: str, model_version: str, output_dir=None) -> LocalModelArtifact:
    validate_name(model_name)
    validate_version(model_version)
    root = Path(output_dir if output_dir is not None else DEFAULT_ARTIFACTS_DIR).resolve()
    directory = root / model_name / model_version
    if not directory.resolve().is_relative_to(root):
        raise ArtifactStorageError("Artifact paths must stay inside the selected local directory.")
    return LocalModelArtifact(directory / "model.joblib", directory / "manifest.json")


def read_bounded(path: Path, limit: int) -> bytes:
    try:
        with path.open("rb") as stream:
            if os.fstat(stream.fileno()).st_size > limit:
                raise ArtifactIntegrityError("Artifact file exceeds the configured size limit.")
            data = stream.read(limit + 1)
        if len(data) > limit:
            raise ArtifactIntegrityError("Artifact file exceeds the configured size limit.")
        return data
    except FileNotFoundError as error:
        raise ArtifactNotFoundError("Exact local artifact or manifest not found.") from error
    except OSError as error:
        raise ArtifactStorageError("Could not read local artifact files.") from error


def read_local(artifact: LocalModelArtifact, *, registry, max_artifact_bytes=DEFAULT_MAX_ARTIFACT_BYTES):
    if (not isinstance(artifact, LocalModelArtifact) or artifact.artifact_path.name != "model.joblib"
            or artifact.manifest_path.name != "manifest.json"
            or artifact.artifact_path.parent != artifact.manifest_path.parent):
        raise ArtifactIntegrityError("An exact local model.joblib and manifest.json pair is required.")
    directory = artifact.artifact_path.parent
    manifest, record = decode_manifest(read_bounded(artifact.manifest_path, MAX_MANIFEST_BYTES),
        model_name=directory.parent.name, model_version=directory.name,
        registry=registry, max_artifact_bytes=max_artifact_bytes)
    data = read_bounded(artifact.artifact_path, max_artifact_bytes)
    verify_bytes(data, manifest)
    return manifest, record, data


def _atomic_bytes(path: Path, data: bytes) -> None:
    temporary = None
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(mode="wb", dir=path.parent, prefix=".artifact-",
                                         suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.link(temporary, path)  # Same atomic no-clobber semantics as the existing text writer.
    except FileExistsError as error:
        raise ArtifactConflictError("Local artifact file already exists; verify before retrying.") from error
    except OSError as error:
        raise ArtifactStorageError("Atomic artifact publication failed; inspect files before retrying.") from error
    finally:
        if temporary is not None:
            try:
                temporary.unlink(missing_ok=True)
            except OSError as error:
                raise ArtifactStorageError("Artifact temporary-file cleanup failed.") from error


def publish_local(artifact, manifest, data, *, registry, reuse=False, sources=(),
                  max_artifact_bytes=DEFAULT_MAX_ARTIFACT_BYTES):
    """Manifest last; identical orphan bytes can be resumed, never replaced."""
    encoded_manifest = manifest_bytes(manifest)
    if len(encoded_manifest) > MAX_MANIFEST_BYTES:
        raise ArtifactIntegrityError("Manifest exceeds the size limit; no files were published.")
    verify_bytes(data, manifest)
    try:
        for source in sources:
            cleaning_paths(Path(source), artifact.artifact_path, artifact.manifest_path)
        exists = [os.path.lexists(path) for path in [artifact.artifact_path, artifact.manifest_path]]
        if any(exists) and not reuse:
            raise ArtifactConflictError("Local version already exists; explicit identical retry required.")
        if exists[1]:
            previous, _, _ = read_local(artifact, registry=registry, max_artifact_bytes=max_artifact_bytes)
            if canonical(previous) != canonical(manifest):
                raise ArtifactConflictError("Local version already identifies different artifact content.")
            return artifact
        if exists[0]:
            verify_bytes(read_bounded(artifact.artifact_path, max_artifact_bytes), manifest)
        else:
            _atomic_bytes(artifact.artifact_path, data)
        _atomic_bytes(artifact.manifest_path, encoded_manifest)
        return artifact
    except StorageError as error:
        raise ArtifactStorageError("Artifact outputs must not alias registry or training sources.") from error
