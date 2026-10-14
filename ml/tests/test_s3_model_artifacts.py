"""S3 conditional publication, bounded retrieval and failures, entirely offline."""

from concurrent.futures import ThreadPoolExecutor
import hashlib
from importlib import import_module
import io
import json
from threading import Barrier, Lock
from uuid import uuid4

import boto3
from botocore.exceptions import (
    ClientError, EndpointConnectionError, NoCredentialsError, ReadTimeoutError,
)
from botocore.response import StreamingBody
from botocore.stub import Stubber
import joblib
import pandas as pd
import pytest

from stocklab_ml.versioning import ModelStatus
from test_model_artifacts import api, bound, load, models, never, register, registry, saved
from test_model_dataset import feature_frame


class FakeS3:
    """Only implement the SDK operations used here, including conditional writes."""

    def __init__(self):
        self.objects = {}
        self.calls = []
        self.lock = Lock()
        self.manifest_barrier = None
        self.fail_manifest = False
        self.error = None

    def get_object(self, *, Bucket, Key):
        if self.error: raise self.error
        with self.lock:
            self.calls.append(("get", Bucket, Key))
            if (Bucket, Key) not in self.objects:
                raise ClientError({"Error": {"Code": "NoSuchKey", "Message": "private response"}}, "GetObject")
            data = self.objects[Bucket, Key]
        return {"Body": StreamingBody(io.BytesIO(data), len(data)), "ContentLength": len(data)}

    def put_object(self, *, Bucket, Key, Body, IfNoneMatch, ContentType, ChecksumSHA256):
        if self.error: raise self.error
        assert IfNoneMatch == "*"
        assert Key.startswith("dev/stocklab/models/")
        assert ContentType in {"application/octet-stream", "application/json"}
        import base64
        assert ChecksumSHA256 == base64.b64encode(hashlib.sha256(Body).digest()).decode("ascii")
        if Key.endswith("manifest.json"):
            if self.manifest_barrier: self.manifest_barrier.wait(timeout=10)
            if self.fail_manifest: raise EndpointConnectionError(endpoint_url="https://private.invalid")
        with self.lock:
            self.calls.append(("put", Bucket, Key))
            if (Bucket, Key) in self.objects:
                raise ClientError({"Error": {"Code": "PreconditionFailed"},
                                   "ResponseMetadata": {"HTTPStatusCode": 412}}, "PutObject")
            self.objects[Bucket, Key] = bytes(Body)
        return {"ETag": '"not-a-sha256"'}


def store(client=None, **kwargs):
    config = api().S3ArtifactConfig("stocklab-dev", "ca-central-1", "dev/stocklab",
                                    trusted_private_source=True, **kwargs)
    return api().S3ModelArtifactStore(config, client=client if client is not None else FakeS3())


def download(storage, context, registry, tmp_path, **kwargs):
    return storage.download_model_artifact(context.record.model_name, context.record.model_version,
        registry=registry, output_dir=tmp_path / "cache", **kwargs)


def variant(artifact):
    """A different exact binary for the same metadata; it must never replace a version."""
    data = artifact.artifact_path.read_bytes() + b"extra"
    artifact.artifact_path.write_bytes(data)
    manifest = json.loads(artifact.manifest_path.read_text())
    manifest.update(artifact_sha256=hashlib.sha256(data).hexdigest(), artifact_size_bytes=len(data))
    artifact.manifest_path.write_text(json.dumps(manifest))


@pytest.mark.parametrize("index", [0, 1])
def test_s3_round_trip_has_exact_keys_publication_order_and_no_retraining(registry, models, tmp_path, monkeypatch, index):
    context, artifact = saved(registry, models[index], tmp_path)
    features = feature_frame().tail(5)
    expected = context.predict(features).to_dataframe()
    client = FakeS3()
    storage = store(client)
    reference = storage.upload_model_artifact(artifact, registry=registry)
    manifest = json.loads(artifact.manifest_path.read_text())
    prefix = f"dev/stocklab/models/{context.record.model_name}/v000001"
    assert reference.artifact_key == f"{prefix}/artifacts/{manifest['artifact_sha256']}.joblib"
    assert reference.manifest_key == f"{prefix}/manifest.json"
    writes = [key for operation, bucket, key in client.calls if operation == "put"]
    assert writes == [reference.artifact_key, reference.manifest_key]
    assert ("get", "stocklab-dev", reference.artifact_key) in client.calls[:client.calls.index(("put", "stocklab-dev", reference.manifest_key))]
    assert set(client.objects) == {("stocklab-dev", reference.artifact_key), ("stocklab-dev", reference.manifest_key)}
    before = registry.path.read_bytes()
    del context
    for module in ["stocklab_ml.modeling", "stocklab_ml.modeling.logistic", "stocklab_ml.modeling.random_forest"]:
        for name in ["train_logistic_regression", "train_random_forest"]:
            if hasattr(import_module(module), name): monkeypatch.setattr(import_module(module), name, never)
    from sklearn.pipeline import Pipeline
    from sklearn.preprocessing import StandardScaler
    from sklearn.linear_model import LogisticRegression
    from sklearn.ensemble import RandomForestClassifier
    for cls in [Pipeline, StandardScaler, LogisticRegression, RandomForestClassifier]: monkeypatch.setattr(cls, "fit", never)
    cached = storage.download_model_artifact(manifest["model_name"], "v000001", registry=registry, output_dir=tmp_path / "cache")
    loaded = load(cached, registry)
    pd.testing.assert_frame_equal(loaded.predict(features).to_dataframe(), expected, check_exact=True)
    assert loaded.record.status == ModelStatus.ARCHIVED and registry.path.read_bytes() == before
    assert storage.upload_model_artifact(artifact, registry=registry) == reference
    assert len([call for call in client.calls if call[0] == "put"]) == 2
    calls = len(client.calls)
    assert storage.download_model_artifact(manifest["model_name"], "v000001", registry=registry, output_dir=tmp_path / "cache") == cached
    assert len(client.calls) == calls + 1  # Fetch manifest, verify cache, avoid another binary download.


def test_duplicate_version_with_different_binary_conflicts_without_overwrite(registry, models, tmp_path):
    _, artifact = saved(registry, models[0], tmp_path)
    client, storage = FakeS3(), None
    storage = store(client)
    reference = storage.upload_model_artifact(artifact, registry=registry)
    before = dict(client.objects)
    variant(artifact)
    with pytest.raises(api().ArtifactConflictError): storage.upload_model_artifact(artifact, registry=registry)
    assert client.objects == before


@pytest.mark.parametrize("different", [False, True])
def test_concurrent_uploads_have_one_logical_winner(registry, models, tmp_path, different):
    context = bound(registry, models[0])
    first = api().save_model_artifact(context, output_dir=tmp_path / "a")
    second = api().save_model_artifact(context, output_dir=tmp_path / "b")
    if different: variant(second)
    client = FakeS3()
    client.manifest_barrier = Barrier(2)
    storage = store(client)
    def upload(artifact):
        try: return storage.upload_model_artifact(artifact, registry=registry)
        except api().ArtifactConflictError: return "conflict"
    with ThreadPoolExecutor(2) as pool: results = list(pool.map(upload, [first, second]))
    assert results.count("conflict") == int(different)
    if not different: assert results[0] == results[1]
    manifests = [key for bucket, key in client.objects if key.endswith("manifest.json")]
    assert len(manifests) == 1
    published = json.loads(client.objects["stocklab-dev", manifests[0]])
    assert published["artifact_sha256"] in {json.loads(item.manifest_path.read_text())["artifact_sha256"] for item in [first, second]}


def test_partial_upload_is_unpublished_and_identical_retry_finishes(registry, models, tmp_path):
    context, artifact = saved(registry, models[0], tmp_path)
    client, storage = FakeS3(), None
    storage = store(client)
    client.fail_manifest = True
    with pytest.raises(api().ArtifactStorageError): storage.upload_model_artifact(artifact, registry=registry)
    assert len(client.objects) == 1
    with pytest.raises(api().ArtifactNotFoundError): download(storage, context, registry, tmp_path)
    assert not (tmp_path / "cache").exists()
    client.fail_manifest = False
    storage.upload_model_artifact(artifact, registry=registry)
    assert len(client.objects) == 2
    assert load(download(storage, context, registry, tmp_path), registry).record == context.record


@pytest.mark.parametrize("bad", ["bytes", "truncated", "long", "missing", "invalid_manifest", "identity", "metadata"])
def test_remote_corruption_never_publishes_or_unpickles(registry, models, tmp_path, monkeypatch, bad):
    context, artifact = saved(registry, models[0], tmp_path)
    client = FakeS3()
    storage = store(client)
    reference = storage.upload_model_artifact(artifact, registry=registry)
    key = (reference.bucket, reference.artifact_key)
    if bad == "bytes": client.objects[key] = b"x" + client.objects[key][1:]
    if bad == "truncated": client.objects[key] = client.objects[key][:-1]
    if bad == "long": client.objects[key] += b"x"
    if bad == "missing": del client.objects[key]
    if bad == "invalid_manifest": client.objects[reference.bucket, reference.manifest_key] = b"{"
    if bad in {"identity", "metadata"}:
        manifest = json.loads(client.objects[reference.bucket, reference.manifest_key])
        if bad == "identity": manifest["model_version"] = "v000002"
        else: manifest["model_record"]["training_run_id"] = str(uuid4())
        client.objects[reference.bucket, reference.manifest_key] = json.dumps(manifest).encode()
    monkeypatch.setattr(joblib, "load", never)
    with pytest.raises(api().ModelArtifactError): download(storage, context, registry, tmp_path)
    assert not (tmp_path / "cache").exists()


@pytest.mark.parametrize("operation", ["upload", "download"])
@pytest.mark.parametrize("error", [
    NoCredentialsError(), EndpointConnectionError(endpoint_url="https://private.invalid"),
    ReadTimeoutError(endpoint_url="https://private.invalid"),
    *[ClientError({"Error": {"Code": code, "Message": "SECRET-CONTENT"}}, "GetObject")
      for code in ["AccessDenied", "NoSuchBucket", "NoSuchKey", "PermanentRedirect", "AuthorizationHeaderMalformed", "SlowDown"]],
])
def test_aws_errors_are_controlled_without_sensitive_response(registry, models, tmp_path, operation, error):
    context, artifact = saved(registry, models[0], tmp_path)
    client = FakeS3()
    client.error = error
    storage = store(client)
    with pytest.raises(api().ModelArtifactError) as caught:
        if operation == "upload": storage.upload_model_artifact(artifact, registry=registry)
        else: download(storage, context, registry, tmp_path)
    assert "SECRET-CONTENT" not in str(caught.value) and "private.invalid" not in str(caught.value)
    assert not client.objects and not (tmp_path / "cache").exists()


@pytest.mark.parametrize("name,version", [("../escape", "v000001"), ("/absolute", "v000001"),
    ("random_forest_baseline", "latest"), ("random_forest_baseline", "unknown"),
    ("random_forest_baseline", ""), ("random_forest_baseline", "v000002")])
def test_invalid_or_unregistered_exact_identity_makes_no_aws_calls(registry, models, tmp_path, name, version):
    bound(registry, models[1])
    client = FakeS3()
    with pytest.raises(Exception): store(client).download_model_artifact(name, version, registry=registry, output_dir=tmp_path / "cache")
    assert client.calls == []


@pytest.mark.parametrize("field,value", [("bucket", ""), ("bucket", "https://bucket"), ("bucket", "UPPER"),
    ("bucket", "192.168.1.1"), ("region", ""), ("region", "../region"),
    ("prefix", "../escape"), ("prefix", "/absolute"), ("prefix", "foo//bar"),
    ("prefix", "foo/../bar"), ("prefix", "foo\\bar"), ("prefix", "foo/"),
    ("trusted_private_source", False), ("max_artifact_bytes", 0), ("max_artifact_bytes", True)])
def test_configuration_rejects_unsafe_source_or_path(field, value):
    values = dict(bucket="stocklab-dev", region="ca-central-1", prefix="dev/stocklab", trusted_private_source=True)
    values[field] = value
    with pytest.raises(api().ModelArtifactError): api().S3ArtifactConfig(**values)


def test_configuration_from_environment_requires_explicit_trust(monkeypatch):
    monkeypatch.setenv("STOCKLAB_ML_S3_BUCKET", "stocklab-dev")
    monkeypatch.setenv("STOCKLAB_ML_AWS_REGION", "ca-central-1")
    monkeypatch.setenv("STOCKLAB_ML_S3_PREFIX", "dev/stocklab")
    config = api().S3ArtifactConfig.from_environment(trusted_private_source=True)
    assert (config.bucket, config.region, config.prefix) == ("stocklab-dev", "ca-central-1", "dev/stocklab")
    with pytest.raises(api().ModelArtifactError): api().S3ArtifactConfig.from_environment()


def test_separate_versions_and_explicit_cold_start_do_not_create_registry(registry, models, tmp_path):
    storage = store()
    first, artifact = saved(registry, models[1], tmp_path)
    storage.upload_model_artifact(artifact, registry=registry)
    second = bound(registry, models[1])
    other = api().save_model_artifact(second, output_dir=tmp_path / "artifacts")
    storage.upload_model_artifact(other, registry=registry)
    a = download(storage, first, registry, tmp_path)
    b = download(storage, second, registry, tmp_path)
    assert a.artifact_path != b.artifact_path
    cold = storage.download_model_artifact(first.record.model_name, "v000001", allow_without_registry=True,
        output_dir=tmp_path / "cold")
    assert load(cold, allow_without_registry=True).record == first.record


def test_sdk_stubber_verifies_real_conditional_request_contract(registry, models, tmp_path):
    # Dummy credentials only for the offline SDK Stubber; socket fixture blocks network.
    client = boto3.client("s3", region_name="ca-central-1", aws_access_key_id="offline-test",
                          aws_secret_access_key="offline-test")
    storage = store(client)
    context, artifact = saved(registry, models[0], tmp_path)
    manifest = json.loads(artifact.manifest_path.read_text())
    manifest_key = f"dev/stocklab/models/{context.record.model_name}/v000001/manifest.json"
    with Stubber(client) as stub:
        stub.add_client_error("get_object", service_error_code="NoSuchKey", http_status_code=404,
                              expected_params={"Bucket": "stocklab-dev", "Key": manifest_key})
        stub.add_client_error("put_object", service_error_code="ConditionalRequestConflict", http_status_code=409)
        with pytest.raises(api().ArtifactConflictError): storage.upload_model_artifact(artifact, registry=registry)
        stub.assert_no_pending_responses()


def test_bounded_partial_stream_fails_and_closes_body(registry, models, tmp_path):
    context, artifact = saved(registry, models[0], tmp_path)
    client = FakeS3()
    storage = store(client)
    reference = storage.upload_model_artifact(artifact, registry=registry)
    original = client.get_object
    stream = io.BytesIO(b"short")
    def partial(**kwargs):
        if kwargs["Key"] == reference.artifact_key:
            return {"Body": StreamingBody(stream, len(artifact.artifact_path.read_bytes())),
                    "ContentLength": len(artifact.artifact_path.read_bytes())}
        return original(**kwargs)
    client.get_object = partial
    with pytest.raises(api().ModelArtifactError): download(storage, context, registry, tmp_path)
    assert stream.closed and not (tmp_path / "cache").exists()


def test_size_limit_and_tampered_cache_fail_closed(registry, models, tmp_path):
    context, artifact = saved(registry, models[0], tmp_path)
    client = FakeS3()
    storage = store(client)
    storage.upload_model_artifact(artifact, registry=registry)
    with pytest.raises(api().ModelArtifactError): download(store(client, max_artifact_bytes=1), context, registry, tmp_path)
    cached = download(storage, context, registry, tmp_path)
    cached.artifact_path.write_bytes(b"corrupt")
    with pytest.raises(api().ArtifactIntegrityError): download(storage, context, registry, tmp_path)
    assert cached.artifact_path.read_bytes() == b"corrupt"


def test_initial_upload_works_without_listbucket_permission(registry, models, tmp_path):
    context, artifact = saved(registry, models[0], tmp_path)
    client = FakeS3()
    original = client.get_object
    def without_listing(**kwargs):
        if (kwargs["Bucket"], kwargs["Key"]) not in client.objects:
            raise ClientError({"Error": {"Code": "AccessDenied"},
                               "ResponseMetadata": {"HTTPStatusCode": 403}}, "GetObject")
        return original(**kwargs)
    client.get_object = without_listing
    storage = store(client)
    reference = storage.upload_model_artifact(artifact, registry=registry)
    assert len(client.objects) == 2
    assert load(download(storage, context, registry, tmp_path), registry).record == context.record
    assert storage.upload_model_artifact(artifact, registry=registry) == reference


def test_body_close_failure_is_controlled_and_does_not_publish_manifest(registry, models, tmp_path):
    context, artifact = saved(registry, models[0], tmp_path)
    client = FakeS3()
    original = client.get_object
    class ClosingError(io.BytesIO):
        def close(self):
            super().close()
            raise OSError("SECRET-CONTENT")
    def bad_close(**kwargs):
        response = original(**kwargs)
        if kwargs["Key"].endswith(".joblib"):
            response["Body"] = ClosingError(client.objects[kwargs["Bucket"], kwargs["Key"]])
        return response
    client.get_object = bad_close
    with pytest.raises(api().ArtifactStorageError) as caught:
        store(client).upload_model_artifact(artifact, registry=registry)
    assert "SECRET-CONTENT" not in str(caught.value)
    assert not any(key.endswith("manifest.json") for bucket, key in client.objects)
