"""Local feature CSV round trips and safe, shared atomic result publication."""

from dataclasses import asdict
import json
import os

import numpy as np
import pandas as pd
import pytest

from stocklab_ml.data import storage as shared_storage
from stocklab_ml.data.models import StorageError
from stocklab_ml.modeling import (
    ModelDatasetError, load_feature_csv, save_model_results,
    train_logistic_from_feature_file, train_logistic_regression,
)
from test_model_dataset import feature_frame


def test_feature_csv_round_trip_and_file_training(tmp_path):
    frame = feature_frame(symbol="NA")
    frame["volume"] = pd.array([9007199254740993] * len(frame), dtype="Int64")
    source = tmp_path / "features.csv"
    shared_storage.save_processed(source, frame)
    original = source.read_bytes()
    loaded = load_feature_csv(source)
    pd.testing.assert_frame_equal(frame, loaded, check_exact=False, rtol=1e-14)
    assert loaded.volume.iloc[0] == 9007199254740993
    result = train_logistic_from_feature_file(source)
    direct = train_logistic_regression(loaded)
    pd.testing.assert_frame_equal(result.predictions, direct.predictions)
    assert result.report == direct.report
    assert result.source_path == source.resolve()
    assert source.read_bytes() == original


def test_save_report_predictions_deterministic_no_index_no_model(tmp_path):
    source = tmp_path / "features.csv"
    shared_storage.save_processed(source, feature_frame())
    before = source.read_bytes()
    result = train_logistic_from_feature_file(source)
    report, predictions = tmp_path / "report.json", tmp_path / "predictions.csv"
    saved = save_model_results(result, report_path=report, predictions_path=predictions)
    assert saved.report_path == report and saved.predictions_path == predictions
    assert json.loads(report.read_text()) == asdict(result.report)
    loaded = pd.read_csv(predictions)
    assert list(loaded) == list(result.predictions)
    assert loaded.actual_class.dtype == loaded.predicted_class.dtype == np.dtype("int64")
    assert loaded.probability_up.between(0, 1).all()
    np.testing.assert_allclose(loaded.probability_up, result.predictions.probability_up)
    snapshot = report.read_bytes(), predictions.read_bytes()
    with pytest.raises(StorageError, match="exists"):
        save_model_results(result, report_path=report, predictions_path=predictions)
    save_model_results(result, report_path=report, predictions_path=predictions, overwrite=True)
    assert (report.read_bytes(), predictions.read_bytes()) == snapshot
    assert source.read_bytes() == before
    assert sorted(p.name for p in tmp_path.iterdir()) == ["features.csv", "predictions.csv", "report.json"]


@pytest.mark.parametrize("which", ["report", "predictions"])
def test_preflight_either_existing_destination_blocks_all_writes(tmp_path, which):
    report, predictions = tmp_path / "report.json", tmp_path / "predictions.csv"
    existing, absent = (report, predictions) if which == "report" else (predictions, report)
    existing.write_text("original")
    with pytest.raises(StorageError):
        save_model_results(train_logistic_regression(feature_frame()),
                           report_path=report, predictions_path=predictions)
    assert existing.read_text() == "original" and not absent.exists()


@pytest.mark.parametrize("alias", ["direct_report", "direct_predictions", "hardlink", "same_destinations"])
def test_source_and_destinations_must_be_distinct_even_when_overwriting(tmp_path, alias):
    source = tmp_path / "features.csv"
    shared_storage.save_processed(source, feature_frame())
    before = source.read_bytes()
    result = train_logistic_from_feature_file(source)
    report, predictions = tmp_path / "report.json", tmp_path / "predictions.csv"
    if alias == "direct_report":
        report = source
    elif alias == "direct_predictions":
        predictions = source
    elif alias == "hardlink":
        os.link(source, predictions)
    else:
        report = predictions
    with pytest.raises(StorageError, match="distinct"):
        save_model_results(result, report_path=report, predictions_path=predictions, overwrite=True)
    assert source.read_bytes() == before


@pytest.mark.parametrize("overwrite", [False, True])
def test_atomic_publication_failure_leaves_no_partial_file(tmp_path, monkeypatch, overwrite):
    result = train_logistic_regression(feature_frame())
    report, predictions = tmp_path / "report.json", tmp_path / "predictions.csv"
    if overwrite:
        predictions.write_text("original")

    def fail(*args):
        raise OSError("simulated publication failure")

    monkeypatch.setattr(shared_storage.os, "replace" if overwrite else "link", fail)
    with pytest.raises(StorageError, match="atomically"):
        save_model_results(result, report_path=report, predictions_path=predictions, overwrite=overwrite)
    assert not report.exists()
    assert predictions.read_text() == "original" if overwrite else not predictions.exists()
    assert not list(tmp_path.glob("*.tmp"))


def test_second_file_failure_is_visible_with_atomic_first_file(tmp_path, monkeypatch):
    result = train_logistic_regression(feature_frame())
    report, predictions = tmp_path / "report.json", tmp_path / "predictions.csv"
    real_link = shared_storage.os.link

    def fail_report(src, dst):
        if dst == report:
            raise OSError("report publication failure")
        return real_link(src, dst)

    monkeypatch.setattr(shared_storage.os, "link", fail_report)
    with pytest.raises(StorageError):
        save_model_results(result, report_path=report, predictions_path=predictions)
    assert len(pd.read_csv(predictions)) == len(result.predictions)
    assert not report.exists() and not list(tmp_path.glob("*.tmp"))


@pytest.mark.parametrize("bad", ["raw", "duplicate", "date", "nan", "inf", "width", "missing_file"])
def test_feature_csv_rejects_invalid_input(tmp_path, bad):
    frame = feature_frame()
    source = tmp_path / "features.csv"
    if bad == "missing_file":
        with pytest.raises(StorageError):
            load_feature_csv(source)
        return
    if bad == "raw":
        frame = frame.iloc[:, :7]
    if bad == "date":
        frame["date"] = "01/02/2024"
    if bad in {"nan", "inf"}:
        frame.loc[0, "ma_5"] = np.nan if bad == "nan" else np.inf
    content = frame.to_csv(index=False, date_format="%Y-%m-%d")
    if bad == "duplicate":
        content = content.replace("ma_5", "ma_20", 1)
    if bad == "width":
        content += "extra,short,row\n"
    source.write_text(content)
    with pytest.raises((ModelDatasetError, StorageError)):
        load_feature_csv(source)
