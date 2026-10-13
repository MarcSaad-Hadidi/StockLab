"""Assert-based, offline BUY/HOLD/SELL mechanics; no profitability evidence."""

from dataclasses import asdict
import json
import socket

import pandas as pd

from stocklab_ml.backtesting import run_backtest, save_backtest_result
from stocklab_ml.data.cleaning import clean_historical_dataset
from stocklab_ml.signals import add_confidence_scores, generate_trading_signals


def main() -> None:
    frame = pd.DataFrame({
        "date": ["2024-01-05", "2024-01-08", "2024-01-09", "2024-01-10"],
        "symbol": ["AAPL"] * 4, "open": [90, 100, 110, 120],
        "high": [90, 110, 115, 120], "low": [90, 100, 110, 120],
        "close": [90, 110, 115, 120], "volume": [100] * 4,
    })
    prices = clean_historical_dataset(frame, as_of_date="2024-01-10")
    predictions = pd.DataFrame({
        "date": pd.to_datetime(frame.date.iloc[:3]).astype("datetime64[ns]"),
        "symbol": ["AAPL"] * 3, "predicted_class": [1, 1, 0], "probability_up": [.8, .5, .2],
    })
    decisions = add_confidence_scores(generate_trading_signals(predictions, model_name="synthetic_example"))
    before = prices.dataframe.copy(deep=True), decisions.signals.copy(deep=True)
    result = run_backtest(decisions, prices)
    assert result.trades.signal.tolist() == ["BUY", "SELL"]
    assert result.trades.execution_price.tolist() == [100, 120]
    assert result.trades.quantity.tolist() == [100, 100]
    assert (result.trades.execution_date > result.trades.decision_date).all()
    assert result.report.initial_capital == 100000
    assert result.report.final_cash == result.report.final_value == 102000
    assert result.report.final_positions_value == 0
    assert abs(result.report.total_return - .02) < 1e-14
    assert result.report.win_rate == 1 and result.report.max_drawdown == 0
    pd.testing.assert_frame_equal(prices.dataframe, before[0], check_exact=True)
    pd.testing.assert_frame_equal(decisions.signals, before[1], check_exact=True)
    repeated = run_backtest(decisions, prices)
    for name in ["trades", "equity_curve", "positions_history"]:
        pd.testing.assert_frame_equal(getattr(result, name), getattr(repeated, name), check_exact=True)
    assert result.report == repeated.report
    saved = save_backtest_result(result, overwrite=True)
    assert json.loads(saved.report_path.read_text())["final_value"] == 102000
    print(result.trades.to_string(index=False))
    print(json.dumps(asdict(result.report), indent=2, allow_nan=False))
    print("Synthetic mechanics only. External market/API calls: 0; API credits: 0.")


if __name__ == "__main__":
    def forbidden(*args, **kwargs):
        raise AssertionError("Network is forbidden during this smoke.")

    socket.socket.connect = forbidden
    socket.socket.connect_ex = forbidden
    main()
