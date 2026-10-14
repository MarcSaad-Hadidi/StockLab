"""Exact registered provenance across predictions, confidence decisions, and CSVs."""

from dataclasses import replace
import json
from pathlib import Path
import subprocess
from uuid import uuid4

import pandas as pd
import pytest

from stocklab_ml.data.models import StorageError
from stocklab_ml.signals import add_confidence_scores, generate_trading_signals
from test_model_dataset import feature_frame
from test_model_versioning import api, models, register, registry


def context(registry, result):
    record = register(registry, result)
    return registry.bind_version(result, model_name=record.model_name, model_version=record.model_version)


@pytest.mark.parametrize("index", [0, 1])
def test_historical_predictions_preserve_every_value_and_source_index(registry, models, index):
    result = replace(models[index], predictions=models[index].predictions.set_axis(range(50, 50 + len(models[index].predictions))))
    before = result.predictions.copy(deep=True)
    bound = context(registry, result)
    versioned = bound.historical_predictions()
    output = versioned.to_dataframe()
    pd.testing.assert_frame_equal(versioned.predictions, before, check_exact=True)
    pd.testing.assert_frame_equal(output[before.columns], before, check_exact=True)
    assert output.model_name.eq(result.report.model_name).all()
    assert output.model_version.eq("v000001").all()
    assert versioned.record.training_run_id == bound.record.training_run_id
    detached = versioned.predictions
    detached.loc[50, "probability_up"] = 0
    output.loc[50, "model_version"] = "latest"
    pd.testing.assert_frame_equal(versioned.predictions, before, check_exact=True)
    pd.testing.assert_frame_equal(result.predictions, before, check_exact=True)


@pytest.mark.parametrize("index", [0, 1])
def test_target_free_inference_uses_registered_estimator_without_future_labels(registry, models, index, monkeypatch):
    from sklearn.ensemble import RandomForestClassifier
    from sklearn.linear_model import LogisticRegression
    from stocklab_ml.modeling import predict_direction, predict_random_forest_direction
    result = models[index]
    bound = context(registry, result)
    features = feature_frame().tail(5).drop(columns=["close", "open", "high", "low"])
    before = features.copy(deep=True)
    def forbidden(*args, **kwargs):
        raise AssertionError("Versioned inference must not fit")
    monkeypatch.setattr(LogisticRegression, "fit", forbidden)
    monkeypatch.setattr(RandomForestClassifier, "fit", forbidden)
    expected = (predict_direction(result.pipeline, features) if index == 0
                else predict_random_forest_direction(result.model, features))
    versioned = bound.predict(features)
    pd.testing.assert_frame_equal(versioned.predictions, expected, check_exact=True)
    assert "actual_class" not in versioned.to_dataframe() and "target_date" not in versioned.to_dataframe()
    pd.testing.assert_frame_equal(features, before, check_exact=True)


def test_bind_rejects_unknown_version_wrong_family_and_different_result(registry, models):
    bound = context(registry, models[1])
    with pytest.raises(api().ModelVersionNotFoundError):
        registry.bind_version(models[0], model_name=bound.record.model_name, model_version="v999999")
    with pytest.raises(api().ModelVersionError):
        registry.bind_version(models[0], model_name=bound.record.model_name, model_version=bound.record.model_version)
    changed = models[1].predictions.copy(deep=True)
    changed.loc[0, "probability_up"] *= .99
    with pytest.raises(api().ModelVersionError):
        registry.bind_version(replace(models[1], predictions=changed), model_name=bound.record.model_name,
                              model_version=bound.record.model_version)


@pytest.mark.parametrize("historical", [True, False])
def test_versioned_decisions_preserve_original_confidence_contract(registry, models, historical):
    bound = context(registry, models[0])
    predictions = bound.historical_predictions() if historical else bound.predict(feature_frame().tail(30))
    signals = generate_trading_signals(predictions.predictions, model_name=bound.record.model_name)
    signals = replace(signals, signals=signals.signals.iloc[::-1].set_axis(range(100, 100 + len(signals.signals))))
    decisions = add_confidence_scores(signals)
    before = decisions.signals.copy(deep=True)
    versioned = api().version_confidence_decisions(decisions, predictions=predictions)
    output = versioned.to_dataframe()
    assert set(output.signal) == {"BUY", "SELL", "HOLD"}
    pd.testing.assert_frame_equal(output[before.columns], before, check_exact=True)
    pd.testing.assert_frame_equal(versioned.result.signals, before, check_exact=True)
    assert output.model_version.eq("v000001").all()
    assert list(decisions.signals) == ["date", "symbol", "model_name", "predicted_class", "probability_up", "signal", "confidence"]
    detached = versioned.result
    detached.signals.loc[100, "confidence"] = 0
    detached.report.symbols.append("MSFT")
    pd.testing.assert_frame_equal(versioned.result.signals, before, check_exact=True)
    assert versioned.result.report.symbols == decisions.report.symbols
    pd.testing.assert_frame_equal(decisions.signals, before, check_exact=True)


@pytest.mark.parametrize("bad", ["model_name", "probability", "rows", "signal", "confidence", "report"])
def test_decision_batch_must_match_bound_predictions_and_existing_validation(registry, models, bad):
    predictions = context(registry, models[0]).historical_predictions()
    frame = predictions.predictions
    if bad == "probability": frame.loc[0, "probability_up"] *= .99
    if bad == "rows": frame = frame.iloc[:-1]
    name = "different_model" if bad == "model_name" else predictions.record.model_name
    decisions = add_confidence_scores(generate_trading_signals(frame, model_name=name))
    if bad == "signal": decisions.signals.loc[0, "signal"] = "wrong"
    if bad == "confidence": decisions.signals.loc[0, "confidence"] = .12345
    if bad == "report": decisions = replace(decisions, report=replace(decisions.report, output_rows=999))
    with pytest.raises(api().ModelVersionError):
        api().version_confidence_decisions(decisions, predictions=predictions)


@pytest.mark.parametrize("kind", ["predictions", "decisions"])
def test_export_has_exact_identity_and_multiple_batches_never_overwrite(registry, models, tmp_path, kind):
    predictions = context(registry, models[1]).historical_predictions()
    value = predictions
    if kind == "decisions":
        decisions = add_confidence_scores(generate_trading_signals(predictions.predictions,
                                          model_name=predictions.record.model_name))
        value = api().version_confidence_decisions(decisions, predictions=predictions)
    save = getattr(api(), "save_versioned_" + kind)
    batch = str(uuid4())
    output_dir = tmp_path / kind
    saved = save(value, batch_id=batch, output_dir=output_dir)
    assert saved.data_path == output_dir / predictions.record.model_name / "v000001" / batch / f"{kind}.csv"
    frame = pd.read_csv(saved.data_path, keep_default_na=False, float_precision="round_trip")
    assert frame.model_name.eq(predictions.record.model_name).all()
    assert frame.model_version.eq("v000001").all()
    assert len(frame) == len(predictions.predictions)
    metadata = json.loads(saved.metadata_path.read_text())
    assert metadata["model_name"] == predictions.record.model_name
    assert metadata["model_version"] == "v000001"
    assert metadata["training_run_id"] == predictions.record.training_run_id
    assert metadata["row_count"] == len(frame)
    assert metadata["model_record"] == predictions.record.to_dict()
    before = saved.data_path.read_bytes(), saved.metadata_path.read_bytes()
    with pytest.raises(StorageError): save(value, batch_id=batch, output_dir=output_dir)
    assert (saved.data_path.read_bytes(), saved.metadata_path.read_bytes()) == before
    other = save(value, batch_id=str(uuid4()), output_dir=output_dir)
    assert other.data_path != saved.data_path and saved.data_path.exists()


def test_historical_export_keeps_v1_after_v2_becomes_active(registry, models, tmp_path):
    bound = context(registry, models[1])
    first = bound.record
    registry.set_active_version(first.model_name, first.model_version)
    predictions = bound.historical_predictions()
    decisions = add_confidence_scores(generate_trading_signals(predictions.predictions, model_name=first.model_name))
    versioned = api().version_confidence_decisions(decisions, predictions=predictions)
    saved = api().save_versioned_decisions(versioned, batch_id=str(uuid4()), output_dir=tmp_path / "exports")
    before = saved.data_path.read_bytes(), saved.metadata_path.read_bytes()
    second = register(registry, models[1])
    registry.set_active_version(second.model_name, second.model_version)
    assert registry.get_active_version(second.model_name).model_version == "v000002"
    restored = pd.read_csv(saved.data_path)
    assert restored.model_version.eq("v000001").all()
    assert json.loads(saved.metadata_path.read_text())["model_version"] == "v000001"
    assert (saved.data_path.read_bytes(), saved.metadata_path.read_bytes()) == before
    assert versioned.to_dataframe().model_version.eq("v000001").all()
    api().save_versioned_decisions(versioned, batch_id=str(uuid4()), output_dir=tmp_path / "exports")


@pytest.mark.parametrize("kind", ["predictions", "decisions"])
def test_tampered_wrapper_is_rejected_before_export(registry, models, tmp_path, kind):
    predictions = context(registry, models[0]).historical_predictions()
    value = predictions
    if kind == "decisions":
        decisions = add_confidence_scores(generate_trading_signals(predictions.predictions, model_name=predictions.record.model_name))
        value = api().version_confidence_decisions(decisions, predictions=predictions)
    value._frame.loc[value._frame.index[0], "probability_up"] *= .99
    destination = tmp_path / "exports"
    with pytest.raises(api().ModelVersionError):
        getattr(api(), "save_versioned_" + kind)(value, batch_id=str(uuid4()), output_dir=destination)
    assert not destination.exists()


@pytest.mark.parametrize("batch", ["", "../escape", "batch", None])
def test_invalid_batch_identity_rejected_before_writing(registry, models, tmp_path, batch):
    predictions = context(registry, models[0]).historical_predictions()
    destination = tmp_path / "exports"
    with pytest.raises(api().ModelVersionError):
        api().save_versioned_predictions(predictions, batch_id=batch, output_dir=destination)
    assert not destination.exists()


def test_export_revalidates_registered_record(registry, models, tmp_path):
    predictions = context(registry, models[0]).historical_predictions()
    object.__setattr__(predictions, "record", replace(predictions.record, model_version="v999999", version_number=999999))
    with pytest.raises(api().ModelVersionNotFoundError):
        api().save_versioned_predictions(predictions, batch_id=str(uuid4()), output_dir=tmp_path / "exports")
    assert not (tmp_path / "exports").exists()


def test_generated_paths_are_ignored():
    paths = ["ml/models/registry.json", "ml/models/registry.json.lock",
             "ml/results/versioned_predictions/model/v000001/batch/predictions.csv",
             "ml/results/versioned_decisions/model/v000001/batch/metadata.json"]
    result = subprocess.run(["git", "check-ignore", *paths], cwd=Path(__file__).resolve().parents[2],
                            capture_output=True, text=True, check=True)
    assert result.stdout.splitlines() == paths


@pytest.mark.parametrize("kind", ["predictions", "decisions"])
def test_failed_export_never_presents_partial_json_as_success(registry, models, tmp_path, monkeypatch, kind):
    from stocklab_ml.data import storage
    predictions = context(registry, models[0]).historical_predictions()
    value = predictions
    if kind == "decisions":
        decisions = add_confidence_scores(generate_trading_signals(predictions.predictions,
                                          model_name=predictions.record.model_name))
        value = api().version_confidence_decisions(decisions, predictions=predictions)
    original = storage.os.link
    def fail_metadata(source, destination, *args, **kwargs):
        if destination.name == "metadata.json": raise OSError("interrupted metadata publication")
        return original(source, destination, *args, **kwargs)
    monkeypatch.setattr(storage.os, "link", fail_metadata)
    batch = str(uuid4())
    with pytest.raises(StorageError):
        getattr(api(), "save_versioned_" + kind)(value, batch_id=batch, output_dir=tmp_path / "exports")
    directory = tmp_path / "exports" / value.record.model_name / value.record.model_version / batch
    assert pd.read_csv(directory / f"{kind}.csv").model_version.eq("v000001").all()
    assert not (directory / "metadata.json").exists()
    assert not list(directory.glob("*.tmp"))


def test_registry_hardlink_cannot_be_overwritten_by_export(registry, models, tmp_path):
    import os
    predictions = context(registry, models[0]).historical_predictions()
    batch = str(uuid4())
    directory = tmp_path / "exports" / predictions.record.model_name / "v000001" / batch
    directory.mkdir(parents=True)
    os.link(registry.path, directory / "predictions.csv")
    before = registry.path.read_bytes()
    with pytest.raises(api().ModelVersionError):
        api().save_versioned_predictions(predictions, batch_id=batch, output_dir=tmp_path / "exports", overwrite=True)
    assert registry.path.read_bytes() == before
