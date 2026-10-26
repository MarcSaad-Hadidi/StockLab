"""Immutable dataset/backtest transfers through the real stores, without AWS."""

from concurrent.futures import ThreadPoolExecutor
import base64
import hashlib
import io
import json
from threading import Lock

import boto3
from botocore.exceptions import ClientError, EndpointConnectionError, ReadTimeoutError
from botocore.response import StreamingBody
from botocore.stub import Stubber
import pytest

from stocklab_ml.artifacts import S3ArtifactConfig
from stocklab_ml.artifacts.contracts import (
    ArtifactConflictError, ArtifactIntegrityError, ArtifactNotFoundError, ArtifactStorageError,
)
from stocklab_ml.backtesting.contracts import SavedBacktest


class FakeS3:
    """Get/conditional Put only: no delete, list, ACL or infrastructure API."""

    def __init__(self):
        self.objects = {}
        self.lock = Lock()
        self.error = None
        self.fail_manifest = False
        self.missing_is_denied = False

    def get_object(self, *, Bucket, Key):
        if self.error:
            raise self.error
        with self.lock:
            if (Bucket, Key) not in self.objects:
                code = "AccessDenied" if self.missing_is_denied else "NoSuchKey"
                raise ClientError({"Error": {"Code": code}, "ResponseMetadata": {
                    "HTTPStatusCode": 403 if self.missing_is_denied else 404}}, "GetObject")
            data = self.objects[Bucket, Key]
        return {"Body": StreamingBody(io.BytesIO(data), len(data)), "ContentLength": len(data)}

    def put_object(self, *, Bucket, Key, Body, IfNoneMatch, ContentType, ChecksumSHA256):
        if self.error:
            raise self.error
        assert IfNoneMatch == "*"
        assert ChecksumSHA256 == base64.b64encode(hashlib.sha256(Body).digest()).decode("ascii")
        if self.fail_manifest and Key.endswith("manifest.json"):
            raise EndpointConnectionError(endpoint_url="https://private.invalid")
        with self.lock:
            if (Bucket, Key) in self.objects:
                raise ClientError({"Error": {"Code": "PreconditionFailed"},
                                   "ResponseMetadata": {"HTTPStatusCode": 412}}, "PutObject")
            self.objects[Bucket, Key] = bytes(Body)
        return {}


@pytest.fixture
def stores():
    from stocklab_ml.data.s3_storage import S3DatasetStore
    from stocklab_ml.backtesting.s3_storage import S3BacktestStore
    config = S3ArtifactConfig("stocklab-dev", "ca-central-1", "ml", trusted_private_source=True)
    fake = FakeS3()
    return S3DatasetStore(config, client=fake), S3BacktestStore(config, client=fake), fake


@pytest.fixture
def source(tmp_path):
    directory = tmp_path / "source"
    directory.mkdir()
    # These tests exercise byte persistence, not engine accounting (covered separately).
    paths = [directory / name for name in ["trades.csv", "equity_curve.csv", "positions.csv", "report.json"]]
    for path, data in zip(paths, [b"symbol,quantity\r\nNA,1\r\n", b"date,total_value\n2024-01-02,100\n",
                                  b"symbol,quantity\nNA,1\n", b'{"synthetic": true}\n'], strict=True):
        path.write_bytes(data)
    return SavedBacktest(*paths)


def upload_dataset(store, path, **kwargs):
    return store.upload_dataset(path, symbol="BRK/B", start_date="2024-01-01", end_date="2024-01-31",
                                kind="raw", dataset_id="dataset-1", **kwargs)


def download_dataset(store, directory, **kwargs):
    return store.download_dataset(symbol="BRK/B", start_date="2024-01-01", end_date="2024-01-31",
                                  kind="raw", dataset_id="dataset-1", output_dir=directory, **kwargs)


@pytest.mark.parametrize("kind", ["raw", "cleaned"])
def test_dataset_roundtrip_preserves_bytes_metadata_and_safe_identity(stores, source, tmp_path, kind):
    datasets, _, fake = stores
    metadata = {"source": "synthetic", "rows": 1, "nested": {"unicode": "é", "values": [1, True, None]}}
    reference = datasets.upload_dataset(source.trades_path, symbol="BRK/B", start_date="2024-01-01",
        end_date="2024-01-31", kind=kind, dataset_id="dataset-1", metadata=metadata)
    assert reference.manifest_key == f"ml/datasets/BRK%2FB/2024-01-01_2024-01-31/{kind}/dataset-1/manifest.json"
    result = datasets.download_dataset(symbol="BRK/B", start_date="2024-01-01", end_date="2024-01-31",
        kind=kind, dataset_id="dataset-1", output_dir=tmp_path / "download")
    assert result.files["trades.csv"].read_bytes() == b"symbol,quantity\r\nNA,1\r\n"
    assert result.metadata == metadata
    assert result.manifest_path.read_bytes() == fake.objects["stocklab-dev", reference.manifest_key]


def test_four_backtest_files_can_be_consulted_without_running_engine(stores, source, tmp_path, monkeypatch):
    _, backtests, fake = stores
    reference = backtests.upload_backtest(source, backtest_id="run-1", metadata={"synthetic": True})
    assert reference.manifest_key == "ml/backtests/run-1/manifest.json"
    def forbidden(*args, **kwargs):
        pytest.fail("Download must not run a backtest or train a model")
    monkeypatch.setattr("stocklab_ml.backtesting.engine.run_backtest", forbidden)
    monkeypatch.setattr("stocklab_ml.modeling.logistic.train_logistic_regression", forbidden)
    result = backtests.download_backtest("run-1", output_dir=tmp_path / "download")
    assert set(result.files) == {"trades.csv", "equity_curve.csv", "positions.csv", "report.json"}
    originals = {"trades.csv": source.trades_path, "equity_curve.csv": source.equity_path,
                 "positions.csv": source.positions_path, "report.json": source.report_path}
    for name, original in originals.items():
        assert result.files[name].read_bytes() == original.read_bytes()
    assert result.metadata == {"synthetic": True}
    assert len(fake.objects) == 5


def test_same_upload_is_idempotent_but_changed_file_or_metadata_conflicts(stores, source):
    datasets, _, fake = stores
    first = upload_dataset(datasets, source.trades_path, metadata={"a": 1})
    before = dict(fake.objects)
    assert upload_dataset(datasets, source.trades_path, metadata={"a": 1}) == first
    with pytest.raises(ArtifactConflictError):
        upload_dataset(datasets, source.trades_path, metadata={"a": 2})
    source.trades_path.write_bytes(b"other bytes")
    with pytest.raises(ArtifactConflictError):
        upload_dataset(datasets, source.trades_path, metadata={"a": 1})
    assert fake.objects == before


@pytest.mark.parametrize("first,second", [(1, True), (0, False), (1, 1.0)])
def test_json_metadata_type_changes_are_conflicts(stores, source, first, second):
    datasets, _, fake = stores
    upload_dataset(datasets, source.trades_path, metadata={"value": first})
    before = dict(fake.objects)
    with pytest.raises(ArtifactConflictError):
        upload_dataset(datasets, source.trades_path, metadata={"value": second})
    assert fake.objects == before


def test_backtest_ids_are_isolated(stores, source, tmp_path):
    _, store, _ = stores
    store.upload_backtest(source, backtest_id="first")
    source.report_path.write_bytes(b'{"second":true}')
    store.upload_backtest(source, backtest_id="second")
    one = store.download_backtest("first", output_dir=tmp_path / "one")
    two = store.download_backtest("second", output_dir=tmp_path / "two")
    assert one.files["report.json"].read_bytes() == b'{"synthetic": true}\n'
    assert two.files["report.json"].read_bytes() == b'{"second":true}'


@pytest.mark.parametrize("corruption", ["missing", "bytes", "size", "path", "identity", "duplicate"])
def test_corrupt_remote_never_publishes_local_success(stores, source, tmp_path, corruption):
    datasets, _, fake = stores
    ref = upload_dataset(datasets, source.trades_path)
    key = next(key for key in fake.objects if not key[1].endswith("manifest.json"))
    manifest_key = ("stocklab-dev", ref.manifest_key)
    manifest = json.loads(fake.objects[manifest_key])
    if corruption == "missing":
        del fake.objects[key]
    elif corruption == "bytes":
        fake.objects[key] = b"X" * len(fake.objects[key])
    elif corruption == "size":
        manifest["files"]["trades.csv"]["size_bytes"] += 1
    elif corruption == "path":
        manifest["files"]["../escape.csv"] = manifest["files"].pop("trades.csv")
    elif corruption == "identity":
        manifest["identity"]["symbol"] = "AAPL"
    else:
        data = fake.objects[manifest_key].decode()
        fake.objects[manifest_key] = data.replace('"schema_version":1', '"schema_version":1,"schema_version":1').encode()
    if corruption not in {"bytes", "missing", "duplicate"}:
        fake.objects[manifest_key] = json.dumps(manifest).encode()
    with pytest.raises((ArtifactIntegrityError, ArtifactNotFoundError)):
        download_dataset(datasets, tmp_path / "download")
    assert not (tmp_path / "download").exists()


@pytest.mark.parametrize("error", [
    ClientError({"Error": {"Code": "AccessDenied"}, "ResponseMetadata": {"HTTPStatusCode": 403}}, "GetObject"),
    EndpointConnectionError(endpoint_url="https://private.invalid"),
    ReadTimeoutError(endpoint_url="https://private.invalid"),
])
def test_aws_and_network_failures_are_controlled(stores, source, tmp_path, error):
    datasets, _, fake = stores
    fake.error = error
    with pytest.raises(ArtifactStorageError):
        upload_dataset(datasets, source.trades_path)
    with pytest.raises(ArtifactStorageError):
        download_dataset(datasets, tmp_path / "download")


def test_missing_manifest_is_not_found(stores, tmp_path):
    with pytest.raises(ArtifactNotFoundError):
        download_dataset(stores[0], tmp_path / "download")


def test_missing_local_input_and_existing_destination_are_not_replaced(stores, source, tmp_path):
    datasets, _, fake = stores
    with pytest.raises(ArtifactStorageError):
        upload_dataset(datasets, tmp_path / "absent.csv")
    assert not fake.objects
    upload_dataset(datasets, source.trades_path)
    dest = tmp_path / "download"
    dest.mkdir()
    (dest / "keep.txt").write_text("keep")
    with pytest.raises(ArtifactConflictError):
        download_dataset(datasets, dest)
    assert (dest / "keep.txt").read_text() == "keep"


def test_interrupted_publication_is_invisible_and_retryable_without_delete(stores, source, tmp_path):
    datasets, _, fake = stores
    fake.fail_manifest = True
    with pytest.raises(ArtifactStorageError):
        upload_dataset(datasets, source.trades_path)
    with pytest.raises(ArtifactNotFoundError):
        download_dataset(datasets, tmp_path / "incomplete")
    fake.fail_manifest = False
    upload_dataset(datasets, source.trades_path)
    result = download_dataset(datasets, tmp_path / "recovered")
    assert result.files["trades.csv"].read_bytes() == source.trades_path.read_bytes()


def test_absent_manifest_403_needs_no_list_permission(stores, source, tmp_path):
    datasets, _, fake = stores
    fake.missing_is_denied = True
    upload_dataset(datasets, source.trades_path)
    assert download_dataset(datasets, tmp_path / "download").files["trades.csv"].exists()


def test_concurrent_different_metadata_has_one_winner(stores, source, tmp_path):
    datasets, _, _ = stores
    def publish(value):
        try:
            upload_dataset(datasets, source.trades_path, metadata={"winner": value})
            return value
        except ArtifactConflictError:
            return None
    with ThreadPoolExecutor(max_workers=2) as pool:
        results = list(pool.map(publish, [1, 2]))
    winners = [value for value in results if value is not None]
    assert len(winners) == 1
    assert download_dataset(datasets, tmp_path / "winner").metadata == {"winner": winners[0]}


@pytest.mark.parametrize("identity", ["../escape", "", "a/b", "CON", "x" * 129])
def test_unsafe_backtest_identity_fails_before_s3(stores, source, identity):
    with pytest.raises(ValueError):
        stores[1].upload_backtest(source, backtest_id=identity)
    assert not stores[2].objects


def test_real_sdk_conditional_put_and_checksum_contract(source):
    from stocklab_ml.data.s3_storage import S3DatasetStore
    config = S3ArtifactConfig("stocklab-dev", "ca-central-1", "ml", trusted_private_source=True)
    client = boto3.client("s3", region_name="ca-central-1", aws_access_key_id="offline",
                          aws_secret_access_key="offline")
    data = source.trades_path.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    directory = "ml/datasets/BRK%2FB/2024-01-01_2024-01-31/raw/dataset-1"
    # Stop after verified data PUT; a denied read must never publish the manifest.
    with Stubber(client) as stub:
        stub.add_client_error("get_object", service_error_code="NoSuchKey", http_status_code=404,
                              expected_params={"Bucket": "stocklab-dev", "Key": directory + "/manifest.json"})
        stub.add_response("put_object", {}, {"Bucket": "stocklab-dev", "Key": f"{directory}/files/{digest}/trades.csv",
            "Body": data, "IfNoneMatch": "*", "ContentType": "text/csv",
            "ChecksumSHA256": base64.b64encode(hashlib.sha256(data).digest()).decode("ascii")})
        stub.add_client_error("get_object", service_error_code="AccessDenied", http_status_code=403,
            expected_params={"Bucket": "stocklab-dev", "Key": f"{directory}/files/{digest}/trades.csv"})
        with pytest.raises(ArtifactStorageError):
            upload_dataset(S3DatasetStore(config, client=client), source.trades_path)
        stub.assert_no_pending_responses()
