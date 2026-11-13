"""Synthetic directory-publication denials; no provider, data repair or real permission changes."""

from pathlib import Path

import pytest

from stocklab_ml import operational_data as data


def denied(winerror=5):
    error = PermissionError("synthetic publication denial")
    if winerror is not None:
        error.winerror = winerror
    return error


@pytest.fixture
def publication(tmp_path, monkeypatch):
    source, target = tmp_path / ".partial-synthetic", tmp_path / "2026-10-26"
    source.mkdir()
    (source / "manifest.json").write_bytes(b"synthetic immutable evidence\n")
    waits = []
    monkeypatch.setattr(data, "_WINDOWS", True, raising=False)
    monkeypatch.setattr(data, "_publication_sleep", waits.append, raising=False)
    return source, target, waits


@pytest.mark.parametrize("winerror", [5, 32])
def test_windows_denial_then_success_preserves_exact_source_bytes(publication, monkeypatch, winerror):
    source, target, waits = publication
    original = Path.rename
    calls = []

    def rename(path, destination):
        calls.append((path, destination))
        if len(calls) == 1:
            raise denied(winerror)
        return original(path, destination)

    monkeypatch.setattr(Path, "rename", rename)
    data._publish_snapshot(source, target)
    assert len(calls) == 2 and waits == [.02]
    assert not source.exists()
    assert (target / "manifest.json").read_bytes() == b"synthetic immutable evidence\n"


@pytest.mark.parametrize("winerror", [5, 32])
def test_permanent_windows_denial_is_bounded_and_preserves_original_error_and_partial(publication, monkeypatch, winerror):
    source, target, waits = publication
    error = denied(winerror)
    calls = []

    def rename(*args):
        calls.append(args)
        raise error

    monkeypatch.setattr(Path, "rename", rename)
    with pytest.raises(PermissionError) as raised:
        data._publish_snapshot(source, target)
    assert raised.value is error
    assert len(calls) == 4 and waits == [.02, .04, .08]
    assert not target.exists() and source.is_dir()
    assert (source / "manifest.json").read_bytes() == b"synthetic immutable evidence\n"


@pytest.mark.parametrize("kind", ["no_windows", "other_winerror", "no_winerror", "other_error"])
def test_unrelated_or_nonwindows_errors_are_not_retried(publication, monkeypatch, kind):
    source, target, waits = publication
    monkeypatch.setattr(data, "_WINDOWS", kind != "no_windows")
    error = OSError("different failure") if kind == "other_error" else denied(None if kind == "no_winerror" else 87 if kind == "other_winerror" else 5)
    calls = []

    def rename(*args):
        calls.append(args)
        raise error

    monkeypatch.setattr(Path, "rename", rename)
    with pytest.raises(type(error)) as raised:
        data._publish_snapshot(source, target)
    assert raised.value is error and len(calls) == 1 and waits == []
    assert source.is_dir() and not target.exists()


@pytest.mark.parametrize("when", ["before", "after_denial", "during_backoff"])
def test_destination_appearance_never_overwrites_merges_or_deletes(publication, monkeypatch, when):
    source, target, waits = publication
    calls = []

    def competitor():
        target.mkdir()
        (target / "keep.txt").write_bytes(b"competing immutable evidence")

    def rename(*args):
        calls.append(args)
        if when == "after_denial":
            competitor()
        raise denied()

    def wait(delay):
        waits.append(delay)
        competitor()

    monkeypatch.setattr(Path, "rename", rename)
    if when == "before": competitor()
    if when == "during_backoff": monkeypatch.setattr(data, "_publication_sleep", wait)
    with pytest.raises((FileExistsError, PermissionError)):
        data._publish_snapshot(source, target)
    assert len(calls) == (0 if when == "before" else 1)
    assert (target / "keep.txt").read_bytes() == b"competing immutable evidence"
    assert (source / "manifest.json").read_bytes() == b"synthetic immutable evidence\n"
    assert not (target / "manifest.json").exists()


def test_missing_source_and_cross_parent_publication_are_refused(publication, tmp_path):
    source, target, waits = publication
    with pytest.raises((ValueError, FileNotFoundError)):
        data._publish_snapshot(source / "absent", target)
    other = tmp_path / "other"
    other.mkdir()
    with pytest.raises(ValueError):
        data._publish_snapshot(source, other / target.name)
    assert source.is_dir() and not target.exists() and waits == []


def test_distinct_persistent_denials_preserve_the_first_original_error(publication, monkeypatch):
    source, target, waits = publication
    errors = [denied(5), denied(32), denied(5), denied(32)]
    remaining = iter(errors)
    monkeypatch.setattr(Path, "rename", lambda *args: (_ for _ in ()).throw(next(remaining)))
    with pytest.raises(PermissionError) as raised:
        data._publish_snapshot(source, target)
    assert raised.value is errors[0]
    assert waits == [.02, .04, .08] and source.is_dir() and not target.exists()


def test_source_disappearance_after_denial_is_not_repaired_or_retried(publication, monkeypatch):
    source, target, waits = publication
    error = denied()
    calls = []

    def rename(*args):
        calls.append(args)
        (source / "manifest.json").unlink()
        source.rmdir()
        raise error

    monkeypatch.setattr(Path, "rename", rename)
    with pytest.raises(PermissionError) as raised:
        data._publish_snapshot(source, target)
    assert raised.value is error and len(calls) == 1 and waits == []
    assert not source.exists() and not target.exists()
