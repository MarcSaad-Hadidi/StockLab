"""Reproducible local smoke: synthetic OHLCV -> #60 features -> #61 baseline."""

from dataclasses import asdict
import json
from pathlib import Path

import numpy as np
import pandas as pd

from stocklab_ml.data.storage import save_processed
from stocklab_ml.features import FEATURE_COLUMNS, engineer_features
from stocklab_ml.modeling import predict_direction, save_model_results, train_logistic_from_feature_file


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
    directory = Path(__file__).resolve().parents[1] / "results" / "logistic_regression"
    source = directory / "synthetic_features.csv"
    save_processed(source, features, overwrite=True)
    result = train_logistic_from_feature_file(source)
    report = result.report
    assert report.dataset.supervised_rows > 0
    assert report.split.train_rows > 0 and report.split.test_rows > 0
    assert result.pipeline.named_steps["classifier"].classes_.tolist() == [0, 1]
    assert len(result.predictions) == report.split.test_rows
    assert np.isfinite(result.predictions.probability_up).all()
    assert result.predictions.probability_up.between(0, 1).all()
    assert np.isfinite(report.test_accuracy)
    latest = features.groupby("symbol", sort=True).tail(1)[["date", "symbol"] + FEATURE_COLUMNS]
    assert len(predict_direction(result.pipeline, latest)) == 2
    saved = save_model_results(result, overwrite=True)
    assert saved.report_path.exists() and saved.predictions_path.exists()
    print(json.dumps(asdict(report), indent=2))
    print("Saved report.json and predictions.csv in ml/results/logistic_regression/")


if __name__ == "__main__":
    main()
