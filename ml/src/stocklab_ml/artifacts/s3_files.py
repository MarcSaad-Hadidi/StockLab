"""Bounded, immutable file bundles; the verified manifest is the commit marker."""

from dataclasses import dataclass
import hashlib
import json
import os
from pathlib import Path
import re

from botocore.exceptions import ClientError

from stocklab_ml.data.storage import _atomic_write
from stocklab_ml.data.models import StorageError
from .contracts import (
    ArtifactConflictError, ArtifactIntegrityError, ArtifactNotFoundError, ArtifactStorageError,
    MAX_MANIFEST_BYTES,
)
from .s3_storage import S3ObjectStore

_RESERVED = {"CON", "PRN", "AUX", "NUL", *(f"COM{i}" for i in range(10)), *(f"LPT{i}" for i in range(10))}


def safe_segment(value):
    """Safe on both Windows and POSIX; never accept paths from a manifest."""
    if (not isinstance(value, str) or re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}", value) is None
            or value.endswith(".") or value.split(".")[0].upper() in _RESERVED):
        raise ValueError("An explicit safe identifier/filename of 1..128 characters is required.")
    return value


def _json_bytes(value):
    try:
        return json.dumps(value, ensure_ascii=False, sort_keys=True, allow_nan=False,
                          separators=(",", ":")).encode("utf-8")
    except (TypeError, ValueError, OverflowError, RecursionError):
        raise ArtifactIntegrityError("Bundle metadata must be finite JSON-safe data.") from None


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ArtifactIntegrityError("Duplicate manifest fields are forbidden.")
        result[key] = value
    return result


@dataclass(frozen=True)
class S3BundleReference:
    bucket: str
    region: str
    manifest_key: str


@dataclass(frozen=True)
class DownloadedBundle:
    directory: Path
    manifest_path: Path
    files: dict[str, Path]
    metadata: dict


class S3FileBundleStore(S3ObjectStore):
    """Exact-key Get/conditional Put only; no List, Delete or ACL permission."""

    def _decode(self, data, identity, expected_files=None):
        try:
            manifest = json.loads(data, object_pairs_hook=_unique_object)
            if (not isinstance(manifest, dict)
                    or set(manifest) != {"schema_version", "identity", "metadata", "files"}
                    or type(manifest["schema_version"]) is not int or manifest["schema_version"] != 1
                    or manifest["identity"] != identity or not isinstance(manifest["metadata"], dict)
                    or not isinstance(manifest["files"], dict) or not 1 <= len(manifest["files"]) <= 4):
                raise ValueError
            if expected_files is not None and set(manifest["files"]) != set(expected_files):
                raise ValueError
            folded = set()
            for name, entry in manifest["files"].items():
                safe_segment(name)
                if name.lower() == "manifest.json" or name.casefold() in folded:
                    raise ValueError
                folded.add(name.casefold())
                if (not isinstance(entry, dict) or set(entry) != {"sha256", "size_bytes"}
                        or type(entry["size_bytes"]) is not int
                        or not 0 < entry["size_bytes"] <= self.config.max_artifact_bytes
                        or not isinstance(entry["sha256"], str)
                        or re.fullmatch(r"[a-f0-9]{64}", entry["sha256"]) is None):
                    raise ValueError
            _json_bytes(manifest)  # Also reject nonfinite JSON constants.
            return manifest
        except (ValueError, TypeError, UnicodeError, RecursionError):
            raise ArtifactIntegrityError("Invalid file bundle manifest, identity, size or filename.") from None

    def _verify(self, data, entry):
        if len(data) != entry["size_bytes"] or hashlib.sha256(data).hexdigest() != entry["sha256"]:
            raise ArtifactIntegrityError("Bundle file size or SHA-256 differs from the publication manifest.")

    def _directory(self, relative):
        directory = f"{self.config.prefix}/{relative}"
        if len(directory.encode("utf-8")) > 750:
            raise ValueError("S3 bundle identity exceeds the supported key length.")
        return directory

    def _upload_bundle(self, relative, identity, paths, metadata=None):
        directory = self._directory(relative)
        if metadata is None:
            metadata = {}
        if not isinstance(metadata, dict):
            raise ArtifactIntegrityError("Bundle metadata must be a JSON object.")
        content = {}
        try:
            for name, path in paths.items():
                with Path(path).open("rb") as stream:
                    if not 0 < os.fstat(stream.fileno()).st_size <= self.config.max_artifact_bytes:
                        raise ArtifactIntegrityError("Local bundle file is empty or exceeds its size limit.")
                    content[name] = stream.read(self.config.max_artifact_bytes + 1)
                if not 0 < len(content[name]) <= self.config.max_artifact_bytes:
                    raise ArtifactIntegrityError("Local bundle file exceeds its size limit.")
        except OSError:
            raise ArtifactStorageError("Could not read a local bundle file.") from None
        manifest = {"schema_version": 1, "identity": identity, "metadata": metadata,
                    "files": {name: {"sha256": hashlib.sha256(data).hexdigest(), "size_bytes": len(data)}
                              for name, data in content.items()}}
        encoded = _json_bytes(manifest)
        if len(encoded) > MAX_MANIFEST_BYTES:
            raise ArtifactIntegrityError("Bundle manifest exceeds its size limit.")
        manifest = self._decode(encoded, identity, paths)
        manifest_key = f"{directory}/manifest.json"
        try:
            previous = self._decode(self._get(manifest_key, MAX_MANIFEST_BYTES), identity, paths)
        except ArtifactNotFoundError:
            previous = None
        except ArtifactStorageError as error:
            cause = error.__cause__
            if not (isinstance(cause, ClientError) and cause.response.get("Error", {}).get("Code") == "AccessDenied"
                    and cause.response.get("ResponseMetadata", {}).get("HTTPStatusCode") == 403):
                raise
            # Like the model store, permit first publication without ListBucket.
            # Conditional PUT still prevents replacing an unreadable publication.
            previous = None
        if previous is not None and _json_bytes(previous) != encoded:
            raise ArtifactConflictError("This bundle identity already identifies different content or metadata.")
        for name, data in content.items():
            key = f"{directory}/files/{manifest['files'][name]['sha256']}/{name}"
            if previous is None:
                content_type = "application/json" if name.endswith(".json") else (
                    "text/csv" if name.endswith(".csv") else "application/octet-stream")
                self._put_if_absent(key, data, content_type)
            self._verify(self._get(key, self.config.max_artifact_bytes), manifest["files"][name])
        if previous is None:
            self._put_if_absent(manifest_key, encoded, "application/json")
        published = self._decode(self._get(manifest_key, MAX_MANIFEST_BYTES), identity, paths)
        if _json_bytes(published) != encoded:
            raise ArtifactConflictError("A concurrent writer published a different bundle for this identity.")
        return S3BundleReference(self.config.bucket, self.config.region, manifest_key)

    def _download_bundle(self, relative, identity, output_dir, expected_files=None):
        directory = self._directory(relative)
        destination = Path(output_dir)
        if os.path.lexists(destination):
            raise ArtifactConflictError("Download destination already exists; select a new directory.")
        encoded = self._get(f"{directory}/manifest.json", MAX_MANIFEST_BYTES)
        manifest = self._decode(encoded, identity, expected_files)
        content = {}
        for name, entry in manifest["files"].items():
            data = self._get(f"{directory}/files/{entry['sha256']}/{name}", self.config.max_artifact_bytes)
            self._verify(data, entry)
            content[name] = data
        # Reserve a new directory atomically, never replace even an empty directory.
        # If local I/O fails, no manifest marks this partial directory as complete.
        try:
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.mkdir()
        except FileExistsError:
            raise ArtifactConflictError("Another writer reserved the download destination.") from None
        except OSError:
            raise ArtifactStorageError("Could not create a local bundle directory.") from None
        try:
            for name, data in content.items():
                # Exclusive binary publication preserves CRLF, numeric text and all bytes.
                with (destination / name).open("xb") as stream:
                    stream.write(data)
                    stream.flush()
                    os.fsync(stream.fileno())
                self._verify((destination / name).read_bytes(), manifest["files"][name])
            _atomic_write(destination / "manifest.json", encoded.decode("utf-8"), False)
        except (OSError, StorageError):
            raise ArtifactStorageError("Local download publication failed; no completed bundle is claimed.") from None
        return DownloadedBundle(destination, destination / "manifest.json",
                                {name: destination / name for name in content}, manifest["metadata"])
