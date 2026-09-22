"""Offline, reproducible comparison of both real estimators on synthetic features."""

from dataclasses import asdict
import json
from pathlib import Path

import numpy as np
import pandas as pd

from stocklab_ml.data.storage import save_processed
from stocklab_ml.features import FEATURE_COLUMNS, engineer_features
from stocklab_ml.modeling import (
    load_feature_csv, predict_random_forest_direction, save_model_results,
    train_logistic_regression, train_random_forest_from_feature_file,
)


def main() -> None:
    frames = []
    for symbol, offset in [("AAPL", 0), ("MSFT", 70)]:
        positions = np.arange(260)
        prices = 100 + offset + positions * 0.06 + 4 * np.sin(positions * 0.73)
        frames.append(pd.DataFrame({
            "date": pd.date_range("2024-01-01", periods=len(prices), freq="B").as_unit("ns"),
            "symbol": pd.array([symbol] * len(prices), dtype="string"),
            "open": prices, "high": prices + 1, "low": prices - 1, "close": prices,
            "volume": pd.array(1000 + positions * 13, dtype="Int64"),
        }))
    features = engineer_features(pd.concat(frames, ignore_index=True)).dataframe
    directory = Path(__file__).resolve().parents[1] / "results" / "random_forest"
    source = directory / "synthetic_features.csv"
    save_processed(source, features, overwrite=True)
    forest = train_random_forest_from_feature_file(source)
    logistic = train_logistic_regression(load_feature_csv(source))
    identifiers = ["date", "target_date", "symbol", "actual_class"]
    pd.testing.assert_frame_equal(forest.predictions[identifiers], logistic.predictions[identifiers])
    assert forest.report.split == logistic.report.split
    assert forest.report.baseline_accuracy == logistic.report.test_accuracy
    assert forest.report.accuracy_delta == forest.report.test_accuracy - logistic.report.test_accuracy
    for result in [forest, logistic]:
        assert len(result.predictions) == result.report.split.test_rows > 0
        assert np.isfinite(result.predictions.probability_up).all()
        assert result.predictions.probability_up.between(0, 1).all()
        assert np.isfinite(result.report.test_accuracy)
    importance = forest.report.feature_importances
    assert list(importance) == FEATURE_COLUMNS
    assert all(np.isfinite(value) and value >= 0 for value in importance.values())
    assert np.isclose(sum(importance.values()), 1.0)
    latest = features.groupby("symbol", sort=True).tail(1)[["date", "symbol"] + FEATURE_COLUMNS]
    assert len(predict_random_forest_direction(forest.model, latest)) == 2
    saved = save_model_results(forest, overwrite=True)
    assert saved.report_path.exists() and saved.predictions_path.exists()
    print(json.dumps(asdict(forest.report), indent=2))
    print("Same holdout verified; saved report.json and predictions.csv in ml/results/random_forest/")


if __name__ == "__main__":
    main()
