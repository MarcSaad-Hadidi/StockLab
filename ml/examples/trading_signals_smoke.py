"""Offline signal mechanics: three decisions, then real fitted synthetic models."""

from dataclasses import asdict
import json

import numpy as np
import pandas as pd

from stocklab_ml.features import engineer_features
from stocklab_ml.modeling import predict_random_forest_direction, train_random_forest
from stocklab_ml.signals import generate_trading_signals, save_trading_signals


def main() -> None:
    predictions = pd.DataFrame({
        "date": pd.to_datetime(["2026-09-22"] * 3).as_unit("ns"),
        "symbol": pd.array(["AAPL", "MSFT", "NVDA"], dtype="string"),
        "predicted_class": np.array([1, 1, 0], dtype="int64"),
        "probability_up": [0.75, 0.50, 0.20],
    })
    result = generate_trading_signals(predictions, model_name="synthetic_example")
    assert result.signals.signal.tolist() == ["BUY", "HOLD", "SELL"]
    assert result.report.buy_count == result.report.hold_count == result.report.sell_count == 1
    saved = save_trading_signals(result, overwrite=True)
    assert json.loads(saved.report_path.read_text()) == asdict(result.report)
    assert pd.read_csv(saved.signals_path).signal.tolist() == ["BUY", "HOLD", "SELL"]
    print(result.signals.to_string(index=False))
    print(json.dumps(asdict(result.report), indent=2))

    positions = np.arange(100)
    prices = 100 + positions * 0.07 + 3 * np.sin(positions * 0.7)
    features = engineer_features(pd.DataFrame({
        "date": pd.date_range("2024-01-01", periods=100, freq="B").as_unit("ns"),
        "symbol": pd.array(["AAPL"] * 100, dtype="string"),
        "open": prices, "high": prices + 1, "low": prices - 1, "close": prices,
        "volume": pd.array(100 + positions, dtype="Int64"),
    })).dataframe
    forest = train_random_forest(features)
    for model in [forest.baseline_result, forest]:
        historical = generate_trading_signals(model.predictions, model_name=model.report.model_name)
        target_free = generate_trading_signals(model.predictions.drop(columns=["target_date", "actual_class"]),
                                               model_name=model.report.model_name)
        pd.testing.assert_frame_equal(historical.signals, target_free.signals, check_exact=True)
        assert historical.report == target_free.report
        print(json.dumps(asdict(historical.report), indent=2))
    future = predict_random_forest_direction(forest.model, features.tail(1))
    assert generate_trading_signals(future, model_name=forest.report.model_name).report.output_rows == 1
    print("Synthetic mechanics only; saved ml/results/signals/signals.csv and report.json; no external calls.")


if __name__ == "__main__":
    main()
