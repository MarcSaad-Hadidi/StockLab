"""Comparison JSON uses existing atomic storage, without model or prediction data."""

from dataclasses import asdict
import json

import pytest

from stocklab_ml.data import storage as shared_storage
from stocklab_ml.data.models import StorageError
from stocklab_ml.modeling import (
    ModelEvaluationError, compare_model_results, save_evaluation_report,
)
from stocklab_ml.modeling import storage
from test_model_evaluation import baseline_pair


def test_comparison_json_is_complete_deterministic_and_separate(tmp_path, monkeypatch, baseline_pair):
    monkeypatch.setattr(storage, "DEFAULT_EVALUATION_DIR", tmp_path / "evaluation")
    report = compare_model_results(*baseline_pair)
    saved = save_evaluation_report(report)
    assert saved == tmp_path / "evaluation" / "comparison.json"
    payload = json.loads(saved.read_text())
    assert payload == asdict(report)
    assert [model["model_name"] for model in payload["models"]] == ["logistic_regression_baseline", "random_forest_baseline"]
    assert all(model["total_rows"] == payload["test_rows"] for model in payload["models"])
    assert payload["metric_deltas"] == report.metric_deltas
    assert all("predictions" not in model and "model" not in model for model in payload["models"])
    before = saved.read_bytes()
    with pytest.raises(StorageError):
        save_evaluation_report(report)
    assert saved.read_bytes() == before
    save_evaluation_report(report, overwrite=True)
    assert saved.read_bytes() == before
    assert sorted(path.name for path in saved.parent.iterdir()) == ["comparison.json"]


@pytest.mark.parametrize("overwrite", [False, True])
@pytest.mark.parametrize("failure", ["publication", "flush"])
def test_atomic_failure_never_publishes_partial_json(tmp_path, monkeypatch, baseline_pair, overwrite, failure):
    report = compare_model_results(*baseline_pair)
    destination = tmp_path / "comparison.json"
    if overwrite:
        destination.write_text("original report")

    def fail(*args):
        raise OSError("synthetic failure")

    operation = "fsync" if failure == "flush" else "replace" if overwrite else "link"
    monkeypatch.setattr(shared_storage.os, operation, fail)
    with pytest.raises(StorageError, match="atomically"):
        save_evaluation_report(report, output_path=destination, overwrite=overwrite)
    assert destination.read_text() == "original report" if overwrite else not destination.exists()
    assert not list(tmp_path.glob("*.tmp"))


def test_concurrent_writer_cannot_be_clobbered(tmp_path, monkeypatch, baseline_pair):
    report = compare_model_results(*baseline_pair)
    destination = tmp_path / "comparison.json"
    real_link = shared_storage.os.link

    def race(src, dst):
        dst.write_text("concurrent report")
        real_link(src, dst)

    monkeypatch.setattr(shared_storage.os, "link", race)
    with pytest.raises(StorageError):
        save_evaluation_report(report, output_path=destination)
    assert destination.read_text() == "concurrent report"
    assert not list(tmp_path.glob("*.tmp"))


def test_storage_requires_structured_report(tmp_path):
    with pytest.raises(ModelEvaluationError):
        save_evaluation_report({}, output_path=tmp_path / "comparison.json")
    assert not list(tmp_path.iterdir())
