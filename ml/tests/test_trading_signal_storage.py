"""Offline signal publication reuses the existing atomic, no-clobber writer."""

from dataclasses import asdict
import json
import os

import pandas as pd
import pytest

from stocklab_ml.data import storage as shared_storage
from stocklab_ml.data.models import StorageError
from stocklab_ml.signals import TradingSignalError, generate_trading_signals, save_trading_signals
from stocklab_ml.signals import storage
from test_trading_signals import predictions


@pytest.fixture
def result():
    return generate_trading_signals(predictions(), model_name="random_forest_baseline")


def test_default_csv_json_roundtrip_and_overwrite_policy(tmp_path, monkeypatch, result):
    monkeypatch.setattr(storage, "DEFAULT_SIGNALS_DIR", tmp_path)
    saved = save_trading_signals(result)
    assert saved.signals_path == tmp_path / "signals.csv"
    assert saved.report_path == tmp_path / "report.json"
    csv = pd.read_csv(saved.signals_path)
    assert list(csv) == list(result.signals)
    assert csv.signal.tolist() == ["BUY", "HOLD", "SELL"]
    assert csv.model_name.tolist() == [result.report.model_name] * 3
    assert csv.date.tolist() == ["2024-01-01", "2024-01-02", "2024-01-03"]
    assert csv.predicted_class.tolist() == result.signals.predicted_class.tolist()
    assert csv.probability_up.tolist() == result.signals.probability_up.tolist()
    assert json.loads(saved.report_path.read_text()) == asdict(result.report)
    before = saved.signals_path.read_bytes(), saved.report_path.read_bytes()
    with pytest.raises(StorageError, match="exists"):
        save_trading_signals(result)
    save_trading_signals(result, overwrite=True)
    assert (saved.signals_path.read_bytes(), saved.report_path.read_bytes()) == before
    assert sorted(path.name for path in tmp_path.iterdir()) == ["report.json", "signals.csv"]


@pytest.mark.parametrize("existing", ["signals", "report"])
def test_either_collision_blocks_both_publications(tmp_path, result, existing):
    signals, report = tmp_path / "signals.csv", tmp_path / "report.json"
    occupied, absent = (signals, report) if existing == "signals" else (report, signals)
    occupied.write_text("original")
    with pytest.raises(StorageError):
        save_trading_signals(result, signals_path=signals, report_path=report)
    assert occupied.read_text() == "original" and not absent.exists()


@pytest.mark.parametrize("alias", ["same_outputs", "linked_outputs", "source_signals", "source_report", "linked_source"])
def test_output_and_explicit_source_aliases_are_protected(tmp_path, result, alias):
    source, signals, report = tmp_path / "input.csv", tmp_path / "signals.csv", tmp_path / "report.json"
    source.write_text("original input")
    kwargs = {}
    if alias == "same_outputs":
        report = signals
    elif alias == "linked_outputs":
        signals.write_text("old signals")
        os.link(signals, report)
    else:
        kwargs["source_path"] = source
        if alias == "source_signals":
            signals = source
        elif alias == "source_report":
            report = source
        else:
            os.link(source, signals)
    with pytest.raises(StorageError, match="distinct"):
        save_trading_signals(result, signals_path=signals, report_path=report, overwrite=True, **kwargs)
    assert source.read_text() == "original input"
    if alias == "linked_outputs":
        assert signals.read_text() == report.read_text() == "old signals"


@pytest.mark.parametrize("failure_at", ["signals", "report", "flush"])
@pytest.mark.parametrize("overwrite", [False, True])
def test_atomic_failures_and_cleanup(tmp_path, monkeypatch, result, failure_at, overwrite):
    signals, report = tmp_path / "signals.csv", tmp_path / "report.json"
    if overwrite:
        signals.write_text("old signals")
        report.write_text("old report")
    operation = "replace" if overwrite else "link"
    publish = getattr(shared_storage.os, operation)

    def fail_publish(src, dst):
        if dst == (signals if failure_at == "signals" else report):
            raise OSError("simulated publication failure")
        publish(src, dst)

    def fail_flush(*args):
        raise OSError("simulated flush failure")

    if failure_at == "flush":
        monkeypatch.setattr(shared_storage.os, "fsync", fail_flush)
    else:
        monkeypatch.setattr(shared_storage.os, operation, fail_publish)
    with pytest.raises(StorageError, match="atomically"):
        save_trading_signals(result, signals_path=signals, report_path=report, overwrite=overwrite)
    if failure_at == "report":
        assert pd.read_csv(signals).signal.tolist() == ["BUY", "HOLD", "SELL"]
    else:
        assert signals.read_text() == "old signals" if overwrite else not signals.exists()
    assert report.read_text() == "old report" if overwrite else not report.exists()
    assert not list(tmp_path.glob("*.tmp"))


def test_structured_signal_result_required(tmp_path):
    with pytest.raises(TradingSignalError):
        save_trading_signals({}, signals_path=tmp_path / "signals.csv", report_path=tmp_path / "report.json")
    assert not list(tmp_path.iterdir())
