"""Offline synthetic #75 workflow; metrics demonstrate mechanics, not market quality."""

from datetime import UTC, datetime
import json
from pathlib import Path
import socket
from uuid import uuid4

import numpy as np
import pandas as pd

from stocklab_ml.features import engineer_features
from stocklab_ml.modeling import train_random_forest
from stocklab_ml.signals import add_confidence_scores, generate_trading_signals
from stocklab_ml.versioning import (
    ModelStatus, ModelVersionRegistry, save_versioned_decisions,
    save_versioned_predictions, version_confidence_decisions,
)


def main() -> None:
    def forbidden(*args, **kwargs):
        raise AssertionError("Network forbidden in the model versioning smoke")
    socket.socket.connect = forbidden
    socket.socket.connect_ex = forbidden
    positions = np.arange(120)
    prices = 100 + positions * .07 + 3 * np.sin(positions * .7)
    raw = pd.DataFrame({
        "date": pd.date_range("2024-01-01", periods=len(prices), freq="B").as_unit("ns"),
        "symbol": pd.array(["AAPL"] * len(prices), dtype="string"),
        "open": prices, "high": prices + 1, "low": prices - 1, "close": prices,
        "volume": pd.array(1000 + positions, dtype="Int64"),
    })
    features = engineer_features(raw).dataframe
    forest = train_random_forest(features)
    trained_at = datetime.now(UTC)
    # Each smoke execution is a distinct training run; keep all earlier registry history.
    run_id = str(uuid4())
    directory = Path(__file__).resolve().parents[1] / "results" / "model_versioning_smoke" / run_id
    registry = ModelVersionRegistry(directory / "registry.json")
    logistic = registry.register_version(forest.baseline_result, training_run_id=str(uuid4()), trained_at_utc=trained_at)
    record = registry.register_version(forest, training_run_id=run_id, trained_at_utc=trained_at)
    assert logistic.model_version == record.model_version == "v000001"
    assert record.status == ModelStatus.ARCHIVED
    assert registry.register_version(forest, training_run_id=run_id, trained_at_utc=trained_at) == record
    context = registry.bind_version(forest, model_name=record.model_name, model_version=record.model_version)
    predictions = context.historical_predictions()
    original = predictions.predictions
    confidence = add_confidence_scores(generate_trading_signals(original, model_name=record.model_name))
    decisions = version_confidence_decisions(confidence, predictions=predictions)
    pd.testing.assert_frame_equal(decisions.to_dataframe()[confidence.signals.columns], confidence.signals, check_exact=True)
    saved_predictions = save_versioned_predictions(predictions, batch_id=run_id, output_dir=directory / "predictions")
    saved_decisions = save_versioned_decisions(decisions, batch_id=run_id, output_dir=directory / "decisions")
    inference = context.predict(features.tail(2))
    assert "actual_class" not in inference.predictions and "target_date" not in inference.predictions
    second = registry.register_version(forest, training_run_id=str(uuid4()), trained_at_utc=trained_at)
    registry.set_active_version(second.model_name, second.model_version)
    restored = ModelVersionRegistry(registry.path)
    assert restored.get_active_version(second.model_name).model_version == "v000002"
    assert restored.get_version(record.model_name, record.model_version).metrics == record.metrics
    assert pd.read_csv(saved_decisions.data_path).model_version.eq("v000001").all()
    assert json.loads(saved_predictions.metadata_path.read_text())["model_version"] == "v000001"
    pd.testing.assert_frame_equal(predictions.predictions, original, check_exact=True)
    print(json.dumps({"model_name": record.model_name, "historical_version": record.model_version,
                      "active_version": second.model_version, "synthetic_metrics": record.to_dict()["metrics"],
                      "decision_rows": len(confidence.signals), "network_calls": 0, "api_credits": 0}, indent=2))
    print(saved_decisions.data_path)


if __name__ == "__main__":
    main()
