"""Confidence CSV/JSON contracts and safe publication through the shared writer."""

from dataclasses import asdict, replace
from fractions import Fraction
import json
import os

import numpy as np
import pandas as pd
import pytest

from stocklab_ml import signals as api
from stocklab_ml.data import storage as shared_storage
from stocklab_ml.data.models import StorageError
from stocklab_ml.signals import storage
from test_confidence_scores import signal_result


@pytest.fixture
def result():
    return api.add_confidence_scores(signal_result())


def test_default_storage_roundtrip_preserves_decimal_contract(tmp_path, monkeypatch, result):
    monkeypatch.setattr(storage, "DEFAULT_CONFIDENCE_DIR", tmp_path, raising=False)
    saved = api.save_confidence_signals(result)
    assert saved.signals_path == tmp_path / "decisions.csv"
    assert saved.report_path == tmp_path / "report.json"
    restored = pd.read_csv(saved.signals_path, parse_dates=["date"], dtype={
        "symbol": "string", "model_name": "string", "predicted_class": "int64",
        "probability_up": "float64", "signal": "string", "confidence": "float64",
    })
    restored["date"] = restored.date.dt.as_unit("ns")
    pd.testing.assert_frame_equal(restored, result.signals, check_exact=True)
    assert json.loads(saved.report_path.read_text()) == asdict(result.report)
    before = saved.signals_path.read_bytes(), saved.report_path.read_bytes()
    with pytest.raises(StorageError, match="exists"):
        api.save_confidence_signals(result)
    api.save_confidence_signals(result, overwrite=True)
    assert (saved.signals_path.read_bytes(), saved.report_path.read_bytes()) == before
    assert sorted(path.name for path in tmp_path.iterdir()) == ["decisions.csv", "report.json"]


def test_valid_numeric_report_scalars_are_normalized_for_json(tmp_path):
    source = signal_result()
    source = replace(source, report=replace(
        source.report, input_rows=np.int64(3), output_rows=np.int64(3),
        buy_count=np.int64(1), sell_count=np.int64(1), hold_count=np.int64(1),
        buy_threshold=Fraction(3, 5), sell_threshold=np.float64(0.4),
    ))
    result = api.add_confidence_scores(source)
    saved = api.save_confidence_signals(result, signals_path=tmp_path / "decisions.csv",
                                        report_path=tmp_path / "report.json")
    assert json.loads(saved.report_path.read_text()) == {
        "model_name": "future_model", "input_rows": 3, "output_rows": 3,
        "buy_count": 1, "sell_count": 1, "hold_count": 1,
        "buy_threshold": 0.6, "sell_threshold": 0.4,
        "date_min": "2024-01-01", "date_max": "2024-01-03", "symbols": ["AAPL"],
        "confidence_min": 0.75, "confidence_max": 1.0, "confidence_mean": 0.85,
    }
    assert type(source.report.input_rows) is np.int64
    assert isinstance(source.report.buy_threshold, Fraction)


def test_manually_constructed_confidence_report_supports_real_numeric_scalars(tmp_path, result):
    result = replace(result, report=replace(result.report, input_rows=np.int64(3),
                     buy_threshold=Fraction(3, 5), confidence_max=np.int64(1)))
    saved = api.save_confidence_signals(result, signals_path=tmp_path / "decisions.csv",
                                        report_path=tmp_path / "report.json")
    report = json.loads(saved.report_path.read_text())
    assert report["input_rows"] == 3 and report["buy_threshold"] == 0.6 and report["confidence_max"] == 1.0


@pytest.mark.parametrize("existing", ["csv", "report"])
def test_collision_checks_both_paths_before_writing(tmp_path, result, existing):
    csv, report = tmp_path / "decisions.csv", tmp_path / "report.json"
    occupied, absent = (csv, report) if existing == "csv" else (report, csv)
    occupied.write_text("original")
    with pytest.raises(StorageError):
        api.save_confidence_signals(result, signals_path=csv, report_path=report)
    assert occupied.read_text() == "original" and not absent.exists()


@pytest.mark.parametrize("alias", ["same_outputs", "linked_outputs", "source_csv", "source_report", "linked_source"])
def test_aliases_and_explicit_source_are_protected(tmp_path, result, alias):
    source, csv, report = tmp_path / "input.csv", tmp_path / "decisions.csv", tmp_path / "report.json"
    source.write_text("original input")
    kwargs = {}
    if alias == "same_outputs":
        report = csv
    elif alias == "linked_outputs":
        csv.write_text("old decisions")
        os.link(csv, report)
    else:
        kwargs["source_path"] = source
        if alias == "source_csv":
            csv = source
        elif alias == "source_report":
            report = source
        else:
            os.link(source, csv)
    with pytest.raises(StorageError, match="distinct"):
        api.save_confidence_signals(result, signals_path=csv, report_path=report, overwrite=True, **kwargs)
    assert source.read_text() == "original input"
    if alias == "linked_outputs":
        assert csv.read_text() == report.read_text() == "old decisions"


@pytest.mark.parametrize("failure_at", ["csv", "report", "flush"])
@pytest.mark.parametrize("overwrite", [False, True])
def test_atomic_failures_leave_complete_files_and_clean_temporaries(tmp_path, monkeypatch, result, failure_at, overwrite):
    csv, report = tmp_path / "decisions.csv", tmp_path / "report.json"
    if overwrite:
        csv.write_text("old decisions")
        report.write_text("old report")
    operation = "replace" if overwrite else "link"
    publish = getattr(shared_storage.os, operation)

    def fail_publish(src, dst):
        if dst == (csv if failure_at == "csv" else report):
            raise OSError("simulated publication failure")
        publish(src, dst)

    def fail_flush(*args):
        raise OSError("simulated flush failure")

    if failure_at == "flush":
        monkeypatch.setattr(shared_storage.os, "fsync", fail_flush)
    else:
        monkeypatch.setattr(shared_storage.os, operation, fail_publish)
    with pytest.raises(StorageError, match="atomically"):
        api.save_confidence_signals(result, signals_path=csv, report_path=report, overwrite=overwrite)
    if failure_at == "report":
        assert pd.read_csv(csv).confidence.tolist() == [0.75, 1.0, 0.80]
    else:
        assert csv.read_text() == "old decisions" if overwrite else not csv.exists()
    assert report.read_text() == "old report" if overwrite else not report.exists()
    assert not list(tmp_path.glob("*.tmp"))


@pytest.mark.parametrize("value", [-0.01, 1.01, np.nan, np.inf, -np.inf, True, "0.8", None, 0.5 + 0j, 0.3])
def test_invalid_or_incorrect_confidence_is_not_saved(tmp_path, result, value):
    result.signals["confidence"] = value
    with pytest.raises(api.ConfidenceScoreError):
        api.save_confidence_signals(result, signals_path=tmp_path / "decisions.csv", report_path=tmp_path / "report.json")
    assert not list(tmp_path.iterdir())


@pytest.mark.parametrize("field,value", [
    ("confidence_min", -1), ("confidence_max", 2), ("confidence_mean", 0.2),
    ("confidence_min", np.nan), ("confidence_max", np.inf), ("confidence_mean", True),
    ("confidence_mean", "0.85"), ("confidence_max", 10**100), ("buy_count", 0),
])
def test_inconsistent_reports_are_not_saved(tmp_path, result, field, value):
    malformed = replace(result, report=replace(result.report, **{field: value}))
    with pytest.raises(api.ConfidenceScoreError):
        api.save_confidence_signals(malformed, signals_path=tmp_path / "decisions.csv", report_path=tmp_path / "report.json")
    assert not list(tmp_path.iterdir())


@pytest.mark.parametrize("bad", ["result", "report", "frame", "missing", "extra", "signal", "empty"])
def test_structured_validated_result_required_before_storage(tmp_path, result, bad):
    if bad == "result":
        result = {}
    elif bad == "report":
        result = replace(result, report={})
    elif bad == "frame":
        result = replace(result, signals=[])
    elif bad == "missing":
        result = replace(result, signals=result.signals.drop(columns="confidence"))
    elif bad == "extra":
        result = replace(result, signals=result.signals.assign(actual_class=1))
    elif bad == "signal":
        result.signals.loc[0, "signal"] = "HOLD"
    else:
        result = replace(result, signals=result.signals.iloc[:0])
    with pytest.raises(api.ConfidenceScoreError):
        api.save_confidence_signals(result, signals_path=tmp_path / "decisions.csv", report_path=tmp_path / "report.json")
    assert not list(tmp_path.iterdir())
