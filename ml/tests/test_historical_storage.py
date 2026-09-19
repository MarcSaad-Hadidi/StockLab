"""Persistence failure boundaries: no partial files or silent clobber."""

import os

import pytest

from stocklab_ml.data.models import StorageError
from stocklab_ml.data.storage import save_raw


@pytest.mark.parametrize("overwrite", [False, True])
def test_publish_failure_keeps_previous_file_and_removes_temp(tmp_path, monkeypatch, overwrite):
    path = tmp_path / "raw" / "sample.json"
    path.parent.mkdir()
    path.write_text("previous")

    def fail(*args):
        raise OSError("disk failure")

    monkeypatch.setattr(os, "replace" if overwrite else "link", fail)
    with pytest.raises(StorageError):
        save_raw(path, {"values": []}, overwrite=overwrite)
    assert path.read_text() == "previous"
    assert list(path.parent.iterdir()) == [path]


def test_concurrent_output_is_not_overwritten(tmp_path, monkeypatch):
    path = tmp_path / "sample.json"
    original_link = os.link

    def publish_racer(source, destination):
        path.write_text("concurrent writer")
        original_link(source, destination)

    monkeypatch.setattr(os, "link", publish_racer)
    with pytest.raises(StorageError):
        save_raw(path, {"values": []})
    assert path.read_text() == "concurrent writer"
    assert list(tmp_path.iterdir()) == [path]


def test_flush_failure_does_not_publish_partial_output(tmp_path, monkeypatch):
    path = tmp_path / "sample.json"

    def fail(*args):
        raise OSError("flush failure")

    monkeypatch.setattr(os, "fsync", fail)
    with pytest.raises(StorageError):
        save_raw(path, {"values": []})
    assert not list(tmp_path.iterdir())
