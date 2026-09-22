"""Three offline, synthetic ML decisions; no inference or risk evaluation."""

from dataclasses import asdict
import json
import socket

import numpy as np
import pandas as pd

from stocklab_ml.signals import add_confidence_scores, generate_trading_signals, save_confidence_signals


def main() -> None:
    predictions = pd.DataFrame({
        "date": pd.to_datetime(["2026-09-22"] * 3).as_unit("ns"),
        "symbol": pd.array(["AAPL", "MSFT", "NVDA"], dtype="string"),
        "predicted_class": np.array([1, 1, 0], dtype="int64"),
        "probability_up": [0.75, 0.50, 0.20],
    })
    source = generate_trading_signals(predictions, model_name="synthetic_example")
    before = source.signals.copy(deep=True)
    report_before = asdict(source.report)
    result = add_confidence_scores(source)
    assert result.signals.signal.tolist() == ["BUY", "HOLD", "SELL"]
    assert result.signals.confidence.tolist() == [0.75, 1.0, 0.80]
    assert len(result.signals) == 3
    assert np.isfinite(result.signals.confidence).all()
    assert result.signals.confidence.between(0, 1).all()
    pd.testing.assert_frame_equal(result.signals.drop(columns="confidence"), before, check_exact=True)
    pd.testing.assert_frame_equal(source.signals, before, check_exact=True)
    assert asdict(source.report) == report_before
    saved = save_confidence_signals(result, overwrite=True)
    assert json.loads(saved.report_path.read_text()) == asdict(result.report)
    assert pd.read_csv(saved.signals_path).confidence.tolist() == [0.75, 1.0, 0.80]
    print(result.signals.to_string(index=False))
    print(json.dumps(asdict(result.report), indent=2))
    print("Saved ml/results/confidence/decisions.csv and report.json. External calls: 0; API credits: 0.")


if __name__ == "__main__":
    def forbidden(*args, **kwargs):
        raise AssertionError("Network is forbidden during this smoke.")

    socket.socket.connect = forbidden
    socket.socket.connect_ex = forbidden
    main()
