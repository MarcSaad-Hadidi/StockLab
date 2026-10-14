"""Private S3 version publication using conditional PUTs; manifest is the commit marker."""

import base64
from dataclasses import dataclass
import hashlib
import os
import re

import boto3
from botocore.config import Config
from botocore.exceptions import BotoCoreError, ClientError

from stocklab_ml.versioning.validation import canonical, validate_name, validate_version
from .contracts import (
    ArtifactConflictError, ArtifactIntegrityError, ArtifactNotFoundError, ArtifactStorageError,
    DEFAULT_MAX_ARTIFACT_BYTES, MAX_MANIFEST_BYTES, ModelArtifactError, S3ModelArtifactReference,
)
from .local_storage import artifact_paths, publish_local, read_local
from .validation import decode_manifest, manifest_bytes, resolve_registry, validate_limit, verify_bytes


@dataclass(frozen=True)
class S3ArtifactConfig:
    """Caller approval of this private bucket/prefix is mandatory, never inferred from a hash."""

    bucket: str
    region: str
    prefix: str
    trusted_private_source: bool = False
    max_artifact_bytes: int = DEFAULT_MAX_ARTIFACT_BYTES

    def __post_init__(self):
        if (not isinstance(self.bucket, str) or re.fullmatch(r"[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]", self.bucket) is None
                or ".." in self.bucket or ".-" in self.bucket or "-." in self.bucket
                or re.fullmatch(r"[0-9]+(?:\.[0-9]+){3}", self.bucket)
                or self.bucket.startswith(("xn--", "sthree-", "amzn-s3-demo-"))
                or self.bucket.endswith(("-s3alias", "--ol-s3", ".mrap", "--x-s3", "--table-s3"))):
            raise ModelArtifactError("An explicit valid general-purpose S3 bucket name is required.")
        if (not isinstance(self.region, str) or len(self.region) > 64
                or re.fullmatch(r"[a-z]{2}(?:-[a-z]+)+-[1-9][0-9]*", self.region) is None):
            raise ModelArtifactError("An explicit AWS region is required.")
        if (not isinstance(self.prefix, str) or not 1 <= len(self.prefix) <= 512
                or any(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}", segment) is None
                       for segment in self.prefix.split("/"))):
            raise ModelArtifactError("S3 prefix must contain safe nonempty relative path segments.")
        if self.trusted_private_source is not True:
            raise ModelArtifactError("Explicitly approve the configured private bucket and prefix.")
        validate_limit(self.max_artifact_bytes)

    @classmethod
    def from_environment(cls, *, trusted_private_source=False, max_artifact_bytes=DEFAULT_MAX_ARTIFACT_BYTES):
        return cls(os.environ.get("STOCKLAB_ML_S3_BUCKET", ""),
                   os.environ.get("STOCKLAB_ML_AWS_REGION", ""),
                   os.environ.get("STOCKLAB_ML_S3_PREFIX", ""),
                   trusted_private_source, max_artifact_bytes)


def _aws_error(error, *, conditional=False):
    if isinstance(error, ClientError):
        code = error.response.get("Error", {}).get("Code")
        status = error.response.get("ResponseMetadata", {}).get("HTTPStatusCode")
        if conditional and (code in {"PreconditionFailed", "ConditionalRequestConflict"} or status in {409, 412}):
            raise ArtifactConflictError("S3 conditional publication conflicted; verify the version before retrying.") from error
        if code in {"NoSuchKey", "NotFound"} or (status == 404 and code != "NoSuchBucket"):
            raise ArtifactNotFoundError("Exact S3 artifact or publication manifest not found.") from error
    raise ArtifactStorageError("S3 operation failed; check private configuration, permissions and connectivity.") from error


class S3ModelArtifactStore:
    """SDK credential chain, bounded retries, HTTPS; never creates resources or modifies policies."""

    def __init__(self, config: S3ArtifactConfig, *, client=None):
        if not isinstance(config, S3ArtifactConfig):
            raise ModelArtifactError("A validated S3ArtifactConfig is required.")
        self.config = config
        try:
            self.client = client if client is not None else boto3.client(
                "s3", region_name=config.region, use_ssl=True,
                config=Config(signature_version="s3v4", connect_timeout=5, read_timeout=30,
                              retries={"mode": "standard", "total_max_attempts": 3},
                              ignore_configured_endpoint_urls=True))
        except (BotoCoreError, ClientError) as error:
            _aws_error(error)

    def _keys(self, name, version, digest=None):
        validate_name(name)
        validate_version(version)
        directory = f"{self.config.prefix}/models/{name}/{version}"
        if len(directory) > 900:
            raise ModelArtifactError("S3 model/version key exceeds the supported length.")
        return f"{directory}/manifest.json", f"{directory}/artifacts/{digest}.joblib" if digest is not None else None

    def _get(self, key, limit):
        body = None
        try:
            response = self.client.get_object(Bucket=self.config.bucket, Key=key)
            body = response["Body"]
            length = response["ContentLength"]
            if type(length) is not int or not 0 < length <= limit:
                raise ArtifactIntegrityError("S3 object size is invalid or exceeds the configured limit.")
            content = body.read(limit + 1)
            if len(content) != length or len(content) > limit:
                raise ArtifactIntegrityError("S3 object download is incomplete or exceeds its size limit.")
            return content
        except (BotoCoreError, ClientError, OSError) as error:
            _aws_error(error)
        finally:
            if body is not None:
                try:
                    body.close()
                except (BotoCoreError, ClientError, OSError) as error:
                    _aws_error(error)

    def _put_if_absent(self, key, data, content_type):
        try:
            self.client.put_object(Bucket=self.config.bucket, Key=key, Body=data,
                IfNoneMatch="*", ContentType=content_type,
                ChecksumSHA256=base64.b64encode(hashlib.sha256(data).digest()).decode("ascii"))
        except ClientError as error:
            if (error.response.get("Error", {}).get("Code") == "PreconditionFailed"
                    or error.response.get("ResponseMetadata", {}).get("HTTPStatusCode") == 412):
                return  # Existing bytes are verified by GET, never assumed identical.
            _aws_error(error, conditional=True)
        except (BotoCoreError, OSError) as error:
            _aws_error(error)

    def _manifest(self, name, version, registry):
        key, _ = self._keys(name, version)
        return decode_manifest(self._get(key, MAX_MANIFEST_BYTES), model_name=name,
            model_version=version, registry=registry, max_artifact_bytes=self.config.max_artifact_bytes)

    def upload_model_artifact(self, artifact, *, registry=None) -> S3ModelArtifactReference:
        """Verify local registered content, conditionally publish binary, verify, publish manifest last."""
        registry = resolve_registry(registry, False)
        manifest, record, data = read_local(artifact, registry=registry, max_artifact_bytes=self.config.max_artifact_bytes)
        manifest_key, artifact_key = self._keys(record.model_name, record.model_version, manifest["artifact_sha256"])
        reference = S3ModelArtifactReference(self.config.bucket, self.config.region, artifact_key,
                                             manifest_key, manifest["artifact_sha256"])
        try:
            previous, _ = self._manifest(record.model_name, record.model_version, registry)
        except ArtifactNotFoundError:
            previous = None
        except ArtifactStorageError as error:
            cause = error.__cause__
            if not (isinstance(cause, ClientError)
                    and cause.response.get("Error", {}).get("Code") == "AccessDenied"
                    and cause.response.get("ResponseMetadata", {}).get("HTTPStatusCode") == 403):
                raise
            # Absent keys return 403 without ListBucket; conditional PUT still prevents replacing an unreadable version.
            previous = None
        if previous is not None:
            if canonical(previous) != canonical(manifest):
                raise ArtifactConflictError("S3 version already identifies different artifact or manifest content.")
            verify_bytes(self._get(artifact_key, self.config.max_artifact_bytes), manifest)
            return reference
        self._put_if_absent(artifact_key, data, "application/octet-stream")
        verify_bytes(self._get(artifact_key, self.config.max_artifact_bytes), manifest)
        self._put_if_absent(manifest_key, manifest_bytes(manifest), "application/json")
        published, _ = self._manifest(record.model_name, record.model_version, registry)
        if canonical(published) != canonical(manifest):
            raise ArtifactConflictError("A concurrent upload published different content for this version.")
        return reference

    def download_model_artifact(self, model_name, model_version, *, registry=None, output_dir=None,
                                allow_without_registry=False):
        """Exact approved source only; no deserialization, partial success, latest lookup or repair."""
        self._keys(model_name, model_version)
        registry = resolve_registry(registry, allow_without_registry)
        if registry is not None:
            registry.get_version(model_name, model_version)  # Fail unknown local identities before AWS I/O.
        manifest, record = self._manifest(model_name, model_version, registry)
        _, artifact_key = self._keys(model_name, model_version, manifest["artifact_sha256"])
        local = artifact_paths(model_name, model_version, output_dir)
        if os.path.lexists(local.manifest_path):
            cached, _, _ = read_local(local, registry=registry, max_artifact_bytes=self.config.max_artifact_bytes)
            if canonical(cached) != canonical(manifest):
                raise ArtifactConflictError("Local cache identifies different content for this version.")
            return local
        data = self._get(artifact_key, self.config.max_artifact_bytes)
        verify_bytes(data, manifest)
        sources = (registry.path,) if registry is not None else ()
        return publish_local(local, manifest, data, registry=registry, reuse=True, sources=sources,
                             max_artifact_bytes=self.config.max_artifact_bytes)
