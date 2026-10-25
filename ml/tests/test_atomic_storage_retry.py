"""Shared local publication survives transient Windows replacement locks."""

import errno
import json

import pytest

from stocklab_ml.data import storage
from stocklab_ml.data.models import StorageError


@pytest.mark.parametrize("winerror", [5, 32, 33])
def test_overwrite_recovers_from_transient_windows_lock(tmp_path, monkeypatch, winerror):
    path = tmp_path / "state.json"
    path.write_text('{"old":true}')
    replace = storage.os.replace
    calls = []
    def locked_once(source, destination):
        calls.append(destination)
        if len(calls) == 1:
            error = PermissionError(errno.EACCES, "temporary Windows lock")
            error.winerror = winerror
            raise error
        return replace(source, destination)
    monkeypatch.setattr(storage.os, "replace", locked_once)
    storage.save_raw(path, {"updated": True}, overwrite=True)
    assert json.loads(path.read_text()) == {"updated": True}
    assert len(calls) == 2
    assert list(tmp_path.iterdir()) == [path]


@pytest.mark.parametrize("winerror,attempts", [(5, 4), (None, 1)])
def test_persistent_denial_is_bounded_and_preserves_existing_bytes(tmp_path, monkeypatch, winerror, attempts):
    path = tmp_path / "state.json"
    path.write_bytes(b"original bytes")
    calls = []
    def denied(source, destination):
        calls.append(destination)
        error = PermissionError(errno.EACCES, "persistent access denial")
        if winerror is not None:
            error.winerror = winerror
        raise error
    monkeypatch.setattr(storage.os, "replace", denied)
    with pytest.raises(StorageError):
        storage.save_raw(path, {"updated": True}, overwrite=True)
    assert path.read_bytes() == b"original bytes"
    assert len(calls) == attempts
    assert list(tmp_path.iterdir()) == [path]
