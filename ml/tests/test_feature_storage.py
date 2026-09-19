"""Feature CSV parsing, lineage, collisions, and real atomic-write boundaries."""

from dataclasses import asdict
import json
import os

import numpy as np
import pandas as pd
import pytest

from stocklab_ml.data.cleaning import clean_processed_dataset
from stocklab_ml.data.models import StorageError
from stocklab_ml.data.storage import save_processed
from stocklab_ml.features import FeatureEngineeringError, engineer_cleaned_dataset, engineer_features
from stocklab_ml.features import storage
from test_feature_engineering import clean_frame


@pytest.fixture
def source(tmp_path, monkeypatch):
    monkeypatch.setattr(storage, "DEFAULT_FEATURE_DIR", tmp_path / "processed" / "features")
    path = tmp_path / "cleaned" / "synthetic.csv"
    save_processed(path, clean_frame(100 + np.sin(np.arange(40)), "0700"))
    return path


def test_roundtrip_deterministic_report_no_key_and_source_unchanged(source, monkeypatch):
    monkeypatch.delenv("TWELVE_DATA_ML_API_KEY")
    before = source.read_bytes()
    result = engineer_cleaned_dataset(source)
    assert result.output_path == storage.DEFAULT_FEATURE_DIR / source.name
    assert result.report_path == result.output_path.with_suffix(".features.json")
    report = json.loads(result.report_path.read_text())
    assert report == asdict(result.report)
    assert (report["input_rows"], report["output_rows"], report["warmup_rows_removed"]) == (40, 20, 20)
    loaded = pd.read_csv(result.output_path, dtype={
        "symbol": "string", "volume": "Int64", **{c: "float64" for c in result.dataframe.columns[2:] if c != "volume"},
    }, parse_dates=["date"], float_precision="round_trip")
    loaded["date"] = loaded.date.astype("datetime64[ns]")
    pd.testing.assert_frame_equal(loaded, result.dataframe, check_exact=True)
    assert result.dataframe.symbol.unique().tolist() == ["0700"]
    assert (result.dataframe.volume == 9007199254740993).all()
    csv_bytes, report_bytes = result.output_path.read_bytes(), result.report_path.read_bytes()
    assert result.output_path.read_text().splitlines()[1].startswith("2024-01-29,0700,")
    with pytest.raises(StorageError):
        engineer_cleaned_dataset(source)
    again = engineer_cleaned_dataset(source, overwrite=True)
    assert again.output_path.read_bytes() == csv_bytes
    assert again.report_path.read_bytes() == report_bytes
    assert source.read_bytes() == before


def test_cleaner_output_is_directly_compatible(source, tmp_path):
    cleaned = clean_processed_dataset(source, as_of_date="2024-12-31")
    expected = engineer_features(cleaned.dataframe)
    actual = engineer_cleaned_dataset(cleaned.output_path, output_path=tmp_path / "features.csv")
    pd.testing.assert_frame_equal(actual.dataframe, expected.dataframe)


@pytest.mark.parametrize("slot", ["output_path", "report_path"])
@pytest.mark.parametrize("overwrite", [False, True])
def test_source_path_cannot_be_overwritten(source, slot, overwrite):
    before = source.read_bytes()
    with pytest.raises(StorageError, match="distinct"):
        engineer_cleaned_dataset(source, **{slot: source}, overwrite=overwrite)
    assert source.read_bytes() == before


def test_alias_and_shared_destinations_rejected(source, tmp_path):
    alias = tmp_path / "alias.csv"
    os.link(source, alias)
    with pytest.raises(StorageError, match="distinct"):
        engineer_cleaned_dataset(source, output_path=alias, overwrite=True)
    same = tmp_path / "same.csv"
    with pytest.raises(StorageError, match="distinct"):
        engineer_cleaned_dataset(source, output_path=same, report_path=same)


@pytest.mark.parametrize("slot", ["csv", "report"])
def test_either_existing_output_prevents_all_writes(source, tmp_path, slot):
    output, report = tmp_path / "features.csv", tmp_path / "features.json"
    existing = output if slot == "csv" else report
    absent = report if slot == "csv" else output
    existing.write_text("unchanged")
    with pytest.raises(StorageError):
        engineer_cleaned_dataset(source, output_path=output, report_path=report)
    assert existing.read_text() == "unchanged"
    assert not absent.exists()


@pytest.mark.parametrize("overwrite", [False, True])
@pytest.mark.parametrize("failure_at", ["csv", "report", "flush"])
def test_atomic_failures_leave_no_partial_files(source, monkeypatch, tmp_path, overwrite, failure_at):
    output, report = tmp_path / "features.csv", tmp_path / "features.json"
    if overwrite:
        output.write_text("old csv")
        report.write_text("old report")
    operation = "replace" if overwrite else "link"
    publish = getattr(os, operation)
    def fail_publish(src, dst):
        if dst == (output if failure_at == "csv" else report):
            raise OSError("synthetic failure")
        publish(src, dst)
    def fail_flush(*args):
        raise OSError("synthetic failure")
    monkeypatch.setattr(os, operation, fail_publish)
    if failure_at == "flush":
        monkeypatch.setattr(os, "fsync", fail_flush)
    with pytest.raises(StorageError):
        engineer_cleaned_dataset(source, output_path=output, report_path=report, overwrite=overwrite)
    assert not list(tmp_path.glob("*.tmp"))
    if failure_at == "report":
        assert output.read_text().startswith("date,symbol,")
    elif overwrite:
        assert output.read_text() == "old csv"
    else:
        assert not output.exists()
    if overwrite:
        assert report.read_text() == "old report"
    else:
        assert not report.exists()


def test_concurrent_writer_cannot_be_clobbered(source, monkeypatch):
    publish = os.link
    def race(src, dst):
        dst.write_text("concurrent data")
        publish(src, dst)
    monkeypatch.setattr(os, "link", race)
    with pytest.raises(StorageError):
        engineer_cleaned_dataset(source)
    output = storage.DEFAULT_FEATURE_DIR / source.name
    assert output.read_text() == "concurrent data"
    assert not output.with_suffix(".features.json").exists()
    assert not list(output.parent.glob("*.tmp"))


@pytest.mark.parametrize("old,new", [
    ("date,symbol", "target,symbol"), ("volume", "close"),
    ("2024-01-01", "2024-01-01T01:00:00"), ("2024-01-01", " 2024-01-01 "),
    ("2024-01-01", "2024-01-01T00:00:00Z"), ("2024-01-01", "01/01/2024"),
    ("0700", "na"), ("0700", " 0700 "),
    ("9007199254740993", "1.5"), ("9007199254740993", "9223372036854775808"),
    ("9007199254740993", "-1"), ("9007199254740993", ""),
    ("100.0", "NaN"), ("100.0", "inf"), ("100.0", "0"),
])
def test_csv_invalid_values_fail_without_cleaning_or_publishing(source, old, new):
    source.write_text(source.read_text().replace(old, new, 1))
    with pytest.raises(FeatureEngineeringError):
        engineer_cleaned_dataset(source)
    assert not storage.DEFAULT_FEATURE_DIR.exists()


def test_na_symbol_and_max_integer_volume_survive_csv(source):
    frame = clean_frame(np.ones(30), "NA")
    frame["volume"] = pd.array([2**63 - 1] * len(frame), dtype="Int64")
    save_processed(source, frame, overwrite=True)
    result = engineer_cleaned_dataset(source)
    assert result.dataframe.symbol.unique().tolist() == ["NA"]
    assert (result.dataframe.volume == 2**63 - 1).all()


@pytest.mark.parametrize("content", ["", "date,symbol,open,high,low,close,volume\n", "date,symbol\n2024-01-01,AAPL,1\n"])
def test_empty_and_malformed_csv_fail_without_publication(source, content):
    source.write_text(content)
    with pytest.raises(FeatureEngineeringError):
        engineer_cleaned_dataset(source)
    assert not storage.DEFAULT_FEATURE_DIR.exists()
