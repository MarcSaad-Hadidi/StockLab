"""Synthetic mechanics check for the official evaluation API; no external calls."""

from dataclasses import asdict
import json

import numpy as np
import pandas as pd

from stocklab_ml.features import engineer_features
from stocklab_ml.modeling import evaluate_model_baselines, save_evaluation_report


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
    report = evaluate_model_baselines(features)
    for metrics in report.models:
        assert metrics.total_rows == report.test_rows > 0
        assert metrics.true_positive + metrics.true_negative + metrics.false_positive + metrics.false_negative == report.test_rows
        assert metrics.support_positive == metrics.true_positive + metrics.false_negative
        assert metrics.support_negative == metrics.true_negative + metrics.false_positive
        for name in ["accuracy", "precision", "recall", "f1"]:
            assert np.isfinite(getattr(metrics, name)) and 0 <= getattr(metrics, name) <= 1
    for name in ["accuracy", "precision", "recall", "f1"]:
        assert report.metric_deltas[name + "_delta"] == getattr(report.models[1], name) - getattr(report.models[0], name)
    output = save_evaluation_report(report, overwrite=True)
    assert json.loads(output.read_text()) == asdict(report)
    print(json.dumps(asdict(report), indent=2))
    print("Synthetic mechanics only; same holdout verified; saved ml/results/evaluation/comparison.json")


if __name__ == "__main__":
    main()
