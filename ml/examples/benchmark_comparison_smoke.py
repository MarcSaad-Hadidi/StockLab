"""Deterministic two-symbol offline mechanics; no profitability evidence."""

from dataclasses import asdict
import json
import socket

import pandas as pd

from stocklab_ml.backtesting import compare_to_buy_and_hold, run_backtest, save_benchmark_comparison
from stocklab_ml.data.cleaning import clean_historical_dataset
from stocklab_ml.signals import add_confidence_scores, generate_trading_signals


def main() -> None:
    frame = pd.DataFrame([
        ("2024-01-05", "AAPL", 50, 90), ("2024-01-08", "AAPL", 100, 120),
        ("2024-01-09", "AAPL", 120, 90), ("2024-01-10", "AAPL", 110, 110),
        ("2024-01-05", "MSFT", 200, 200), ("2024-01-08", "MSFT", 200, 200),
        ("2024-01-09", "MSFT", 200, 200), ("2024-01-10", "MSFT", 200, 200),
    ], columns=["date", "symbol", "open", "close"])
    frame["high"] = frame[["open", "close"]].max(axis=1)
    frame["low"] = frame[["open", "close"]].min(axis=1)
    frame["volume"] = 100
    prices = clean_historical_dataset(frame, as_of_date="2024-01-10")
    predictions = pd.DataFrame({
        "date": pd.to_datetime(["2024-01-05", "2024-01-05", "2024-01-09"]).as_unit("ns"),
        "symbol": ["AAPL", "MSFT", "AAPL"], "predicted_class": [1, 1, 0],
        "probability_up": [.8, .5, .2],
    })
    decisions = add_confidence_scores(generate_trading_signals(predictions, model_name="synthetic_example"))
    before = prices.dataframe.copy(deep=True)
    backtest = run_backtest(decisions, prices)
    result = compare_to_buy_and_hold(backtest, prices)
    repeated = compare_to_buy_and_hold(backtest, prices)
    for name in ["benchmark_allocations", "benchmark_equity_curve", "benchmark_positions_history"]:
        pd.testing.assert_frame_equal(getattr(result, name), getattr(repeated, name), check_exact=True)
    pd.testing.assert_frame_equal(prices.dataframe, before, check_exact=True)
    assert result.comparison_report == repeated.comparison_report
    assert result.benchmark_report == repeated.benchmark_report
    assert result.benchmark_report.initial_capital == backtest.report.initial_capital == 100000
    assert result.benchmark_allocations.quantity.tolist() == [500, 250]
    assert result.benchmark_equity_curve.total_value.tolist() == [100000, 110000, 95000, 105000]
    assert backtest.report.final_value == 101000
    assert result.benchmark_report.final_value == 105000
    assert abs(result.comparison_report.return_delta + .04) < 1e-14
    saved = save_benchmark_comparison(result, overwrite=True)
    assert json.loads(saved.comparison_path.read_text())["benchmark"]["final_value"] == 105000
    print(result.benchmark_allocations.to_string(index=False))
    print(json.dumps(asdict(result.comparison_report), indent=2, allow_nan=False))
    print("Synthetic mechanics only. External market/API calls: 0; API credits: 0.")


if __name__ == "__main__":
    def forbidden(*args, **kwargs):
        raise AssertionError("Network is forbidden during this smoke.")
    socket.socket.connect = forbidden
    socket.socket.connect_ex = forbidden
    main()
