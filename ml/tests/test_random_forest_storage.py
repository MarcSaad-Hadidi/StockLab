"""RF results share safe storage, with separate defaults from Logistic outputs."""

from dataclasses import asdict, replace
import json
import os

import numpy as np
import pandas as pd
import pytest

from stocklab_ml.data import storage as shared_storage
from stocklab_ml.data.models import StorageError
from stocklab_ml.modeling import (
    load_feature_csv, save_model_results, train_logistic_regression,
    train_random_forest, train_random_forest_from_feature_file,
)
from stocklab_ml.modeling import storage
from test_model_dataset import feature_frame


@pytest.fixture(scope="module")
def forest():
    return train_random_forest(feature_frame())


def test_file_training_matches_dataframe_and_remembers_source(tmp_path):
    source = tmp_path / "features.csv"
    shared_storage.save_processed(source, feature_frame(symbol="NA"))
    before = source.read_bytes()
    actual = train_random_forest_from_feature_file(source, test_fraction=0.3)
    expected = train_random_forest(load_feature_csv(source), test_fraction=0.3)
    pd.testing.assert_frame_equal(actual.predictions, expected.predictions, check_exact=True)
    assert actual.report == expected.report
    assert actual.source_path == source.resolve()
    assert source.read_bytes() == before


def test_default_destinations_keep_both_models_separate(tmp_path, monkeypatch, forest):
    logistic_dir, forest_dir = tmp_path / "logistic_regression", tmp_path / "random_forest"
    monkeypatch.setattr(storage, "DEFAULT_RESULTS_DIR", logistic_dir)
    monkeypatch.setattr(storage, "DEFAULT_RANDOM_FOREST_RESULTS_DIR", forest_dir)
    logistic = save_model_results(train_logistic_regression(feature_frame()))
    before = logistic.report_path.read_bytes(), logistic.predictions_path.read_bytes()
    saved = save_model_results(forest)
    assert saved.report_path == forest_dir / "report.json"
    assert saved.predictions_path == forest_dir / "predictions.csv"
    assert json.loads(saved.report_path.read_text()) == asdict(forest.report)
    csv = pd.read_csv(saved.predictions_path)
    assert list(csv) == ["date", "target_date", "symbol", "actual_class", "predicted_class", "probability_up"]
    assert csv.actual_class.dtype == csv.predicted_class.dtype == np.dtype("int64")
    np.testing.assert_allclose(csv.probability_up, forest.predictions.probability_up)
    assert csv.probability_up.between(0, 1).all()
    assert len(csv) == forest.report.split.test_rows
    assert (logistic.report_path.read_bytes(), logistic.predictions_path.read_bytes()) == before
    assert sorted(path.name for path in forest_dir.iterdir()) == ["predictions.csv", "report.json"]


def test_overwrite_requires_opt_in_and_writes_deterministically(tmp_path, forest):
    report, predictions = tmp_path / "report.json", tmp_path / "predictions.csv"
    save_model_results(forest, report_path=report, predictions_path=predictions)
    before = report.read_bytes(), predictions.read_bytes()
    with pytest.raises(StorageError, match="exists"):
        save_model_results(forest, report_path=report, predictions_path=predictions)
    assert (report.read_bytes(), predictions.read_bytes()) == before
    save_model_results(forest, report_path=report, predictions_path=predictions, overwrite=True)
    assert (report.read_bytes(), predictions.read_bytes()) == before


@pytest.mark.parametrize("alias", ["report", "predictions", "hardlink", "explicit_source", "outputs"])
def test_source_and_destination_aliases_are_protected(tmp_path, forest, alias):
    source = tmp_path / "features.csv"
    shared_storage.save_processed(source, feature_frame())
    before = source.read_bytes()
    result = replace(forest, source_path=source)
    report, predictions = tmp_path / "report.json", tmp_path / "predictions.csv"
    kwargs = {}
    if alias == "report":
        report = source
    elif alias == "predictions":
        predictions = source
    elif alias == "hardlink":
        os.link(source, predictions)
    elif alias == "explicit_source":
        result = forest
        predictions = source
        kwargs["source_path"] = source
    else:
        report = predictions
    with pytest.raises(StorageError, match="distinct"):
        save_model_results(result, report_path=report, predictions_path=predictions, overwrite=True, **kwargs)
    assert source.read_bytes() == before


@pytest.mark.parametrize("failure_at", ["predictions", "report"])
@pytest.mark.parametrize("overwrite", [False, True])
def test_atomic_failures_keep_whole_files_and_clean_up_temporary_files(tmp_path, monkeypatch, forest, failure_at, overwrite):
    report, predictions = tmp_path / "report.json", tmp_path / "predictions.csv"
    if overwrite:
        report.write_text("old report")
        predictions.write_text("old predictions")
    operation = "replace" if overwrite else "link"
    publish = getattr(shared_storage.os, operation)

    def fail(src, dst):
        if dst == (report if failure_at == "report" else predictions):
            raise OSError("simulated publication failure")
        publish(src, dst)

    monkeypatch.setattr(shared_storage.os, operation, fail)
    with pytest.raises(StorageError, match="atomically"):
        save_model_results(forest, report_path=report, predictions_path=predictions, overwrite=overwrite)
    if failure_at == "report":
        assert len(pd.read_csv(predictions)) == forest.report.split.test_rows
    elif overwrite:
        assert predictions.read_text() == "old predictions"
    else:
        assert not predictions.exists()
    assert report.read_text() == "old report" if overwrite else not report.exists()
    assert not list(tmp_path.glob("*.tmp"))


def test_either_collision_is_checked_before_any_publication(tmp_path, forest):
    report, predictions = tmp_path / "report.json", tmp_path / "predictions.csv"
    report.write_text("existing report")
    with pytest.raises(StorageError):
        save_model_results(forest, report_path=report, predictions_path=predictions)
    assert not predictions.exists() and report.read_text() == "existing report"
