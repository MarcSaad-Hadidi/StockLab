"""Cleaning file boundaries use synthetic CSVs exclusively in tmp_path."""

from dataclasses import asdict
import json
import os

import pandas as pd
import pytest

from stocklab_ml.data import cleaning, storage
from stocklab_ml.data.models import DataCleaningError, SchemaValidationError, StorageError

HEADER = "date,symbol,open,high,low,close,volume\n"
DIRTY = HEADER + (
    "2024-01-08,0700,10,12,9,11,9007199254740993\n"
    "2024-01-05,NA,10,12,9,11,0\n"
    "2024-01-05, na ,10.0,12,9,11,0.0\n"
    "2024-01-09,AAPL,10,12,9,,1000\n"
    "2024-01-10,AAPL,10,9,9,11,1000\n"
)


@pytest.fixture
def source(tmp_path):
    path = tmp_path / "processed" / "synthetic.csv"
    path.parent.mkdir()
    path.write_text(DIRTY, encoding="utf-8")
    return path


def run(source, **kwargs):
    return cleaning.clean_processed_dataset(source, as_of_date="2024-01-31", **kwargs)


def test_local_csv_report_lineage_and_deterministic_overwrite(source, monkeypatch):
    monkeypatch.delenv("TWELVE_DATA_ML_API_KEY")
    original = source.read_bytes()
    result = run(source)
    assert result.output_path == source.parent / "cleaned" / source.name
    assert result.report_path == result.output_path.with_suffix(".report.json")
    expected = HEADER + ("2024-01-08,0700,10.0,12.0,9.0,11.0,9007199254740993\n"
                         "2024-01-05,NA,10.0,12.0,9.0,11.0,0\n")
    assert result.output_path.read_text() == expected
    assert json.loads(result.report_path.read_text()) == asdict(result.report)
    assert (result.report.input_rows, result.report.output_rows, result.report.rows_removed) == (5, 2, 3)
    csv_before, report_before = result.output_path.read_bytes(), result.report_path.read_bytes()
    with pytest.raises(StorageError):
        run(source)
    again = run(source, overwrite=True)
    assert again.output_path.read_bytes() == csv_before
    assert again.report_path.read_bytes() == report_before
    assert source.read_bytes() == original
    pd.testing.assert_frame_equal(again.dataframe, result.dataframe)


@pytest.mark.parametrize("filename", ["synthetic.csv", "synthetic.report.json"])
def test_either_existing_destination_blocks_all_writes(source, filename):
    folder = source.parent / "cleaned"
    folder.mkdir()
    target = folder / filename
    target.write_text("existing")
    with pytest.raises(StorageError):
        run(source)
    assert target.read_text() == "existing"
    assert list(folder.iterdir()) == [target]


@pytest.mark.parametrize("overwrite", [False, True])
@pytest.mark.parametrize("slot", ["output_path", "report_path"])
def test_input_cannot_be_replaced(source, overwrite, slot):
    before = source.read_bytes()
    with pytest.raises(StorageError, match="distinct"):
        run(source, overwrite=overwrite, **{slot: source.parent / "." / source.name})
    assert source.read_bytes() == before


def test_paths_must_be_distinct_even_for_hardlinks(source, tmp_path):
    alias = tmp_path / "alias.csv"
    os.link(source, alias)
    with pytest.raises(StorageError, match="distinct"):
        run(source, output_path=alias, overwrite=True)
    same = tmp_path / "same.csv"
    with pytest.raises(StorageError, match="distinct"):
        run(source, output_path=same, report_path=same, overwrite=True)


def test_explicit_destinations(source, tmp_path):
    csv, report = tmp_path / "out.csv", tmp_path / "quality.json"
    result = run(source, output_path=csv, report_path=report)
    assert result.output_path == csv and result.report_path == report
    assert csv.exists() and report.exists()


@pytest.mark.parametrize("overwrite", [False, True])
@pytest.mark.parametrize("failure_at", ["csv", "report", "flush"])
def test_atomic_publish_failures_leave_no_partial_file(source, monkeypatch, overwrite, failure_at):
    folder = source.parent / "cleaned"
    folder.mkdir()
    csv, report = folder / source.name, folder / "synthetic.report.json"
    if overwrite:
        csv.write_text("old csv")
        report.write_text("old report")
    operation = "replace" if overwrite else "link"
    publish = getattr(os, operation)

    def fail_publish(src, dst):
        if (failure_at == "csv" and dst == csv) or (failure_at == "report" and dst == report):
            raise OSError("synthetic failure")
        publish(src, dst)

    def fail_flush(*args):
        raise OSError("synthetic flush failure")

    monkeypatch.setattr(os, operation, fail_publish)
    if failure_at == "flush":
        monkeypatch.setattr(os, "fsync", fail_flush)
    with pytest.raises(StorageError):
        run(source, overwrite=overwrite)
    assert not list(folder.glob("*.tmp"))
    if failure_at == "report":
        assert csv.read_text().startswith(HEADER)  # per-file atomicity, not a pair transaction
    elif overwrite:
        assert csv.read_text() == "old csv"
    else:
        assert not csv.exists()
    if overwrite:
        assert report.read_text() == "old report"
    else:
        assert not report.exists()
    assert source.read_text() == DIRTY


def test_concurrent_writer_is_not_clobbered(source, monkeypatch):
    publish = os.link

    def race(src, dst):
        dst.write_text("concurrent writer")
        publish(src, dst)

    monkeypatch.setattr(os, "link", race)
    with pytest.raises(StorageError):
        run(source)
    folder = source.parent / "cleaned"
    assert (folder / source.name).read_text() == "concurrent writer"
    assert not list(folder.glob("*.tmp"))
    assert not list(folder.glob("*.json"))


@pytest.mark.parametrize("content", ["", HEADER, HEADER + "2024-01-05,AAPL,10,12,9,,1000\n"])
def test_empty_or_all_invalid_input_publishes_nothing(source, content):
    source.write_text(content)
    with pytest.raises(DataCleaningError):
        run(source)
    assert not (source.parent / "cleaned").exists()


@pytest.mark.parametrize("content", [
    HEADER.replace("close", "target") + "2024-01-05,AAPL,10,12,9,11,1000\n",
    HEADER.replace("volume", "close") + "2024-01-05,AAPL,10,12,9,11,1000\n",
    HEADER + "2024-01-05,AAPL,10,12,9,11,1000,extra\n",
    HEADER + "2024-01-05,AAPL,10,12,9,11\n",
])
def test_malformed_csv_schema_fails_before_publication(source, content):
    source.write_text(content)
    with pytest.raises(SchemaValidationError):
        run(source)
    assert not (source.parent / "cleaned").exists()


def test_bounded_loader_rejects_large_file(source, monkeypatch):
    monkeypatch.setattr(storage, "MAX_CLEANING_CSV_BYTES", 16)
    with pytest.raises(StorageError, match="size limit"):
        run(source)
    assert not (source.parent / "cleaned").exists()


def test_unreadable_or_invalid_utf8_csv_is_safe_error(source):
    source.write_bytes(b"\xff\xfe\xff")
    with pytest.raises(StorageError):
        run(source)
    with pytest.raises(StorageError):
        run(source.parent / "absent.csv")


def test_ingestion_saved_csv_works_without_provider_call(tmp_path, payload):
    from stocklab_ml.data.validation import build_dataframe, validate_request
    payload["meta"]["symbol"] = "0700"
    frame = build_dataframe(payload, validate_request("0700", "2024-01-01", "2024-01-09"))
    source = tmp_path / "provider.csv"
    storage.save_processed(source, frame)
    result = run(source)
    pd.testing.assert_frame_equal(frame, result.dataframe)
    assert result.report.rows_removed == 0
