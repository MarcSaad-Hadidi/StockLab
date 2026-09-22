"""One offline probability policy for historical and target-free predictions."""

import pandas as pd

from .contracts import (
    BUY_THRESHOLD, SELL_THRESHOLD, TradingSignal,
    TradingSignalsReport, TradingSignalsResult,
)
from .validation import _prediction_rows, _validate_policy


def generate_trading_signals(
    predictions: pd.DataFrame, *, model_name: str,
    buy_threshold: float = BUY_THRESHOLD, sell_threshold: float = SELL_THRESHOLD,
) -> TradingSignalsResult:
    """Map raw probabilities to daily decisions available only after close[t].

    BUY >= buy_threshold, SELL <= sell_threshold, HOLD strictly between.
    Caller supplies one model identity; classes are retained only for audit.
    Return a new frame sorted by (date, symbol), without training or inference.
    """
    buy_threshold, sell_threshold = _validate_policy(model_name, buy_threshold, sell_threshold)
    frame = _prediction_rows(predictions).sort_values(["date", "symbol"]).reset_index(drop=True)
    frame.insert(2, "model_name", pd.array([model_name] * len(frame), dtype="string"))
    frame["signal"] = pd.Series(TradingSignal.HOLD.value, index=frame.index, dtype="string")
    frame.loc[frame.probability_up >= buy_threshold, "signal"] = TradingSignal.BUY.value
    frame.loc[frame.probability_up <= sell_threshold, "signal"] = TradingSignal.SELL.value
    report = TradingSignalsReport(
        model_name=model_name, input_rows=len(predictions), output_rows=len(frame),
        buy_count=int(frame.signal.eq(TradingSignal.BUY.value).sum()),
        sell_count=int(frame.signal.eq(TradingSignal.SELL.value).sum()),
        hold_count=int(frame.signal.eq(TradingSignal.HOLD.value).sum()),
        buy_threshold=float(buy_threshold), sell_threshold=float(sell_threshold),
        date_min=frame.date.min().date().isoformat(), date_max=frame.date.max().date().isoformat(),
        symbols=sorted(frame.symbol.unique().tolist()),
    )
    return TradingSignalsResult(frame, report)
