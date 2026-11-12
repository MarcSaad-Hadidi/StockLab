"""Offline price-path risk head; distinct from upside probability and live artifacts."""

import numpy as np
import pandas as pd

from stocklab_ml import swing_research as swing

LABEL = "target_price_downside20"
KNOWN_DATE = "target_date_price_downside20"
CONTEXT_FEATURES = ["spy_momentum_20", "spy_momentum_63", "spy_close_ma200", "spy_volatility_20"]
DOWNSIDE_FEATURES = swing.SWING_FEATURES + CONTEXT_FEATURES


def make_frame(features: pd.DataFrame, prices: pd.DataFrame, spy: pd.DataFrame) -> pd.DataFrame:
    """Next-open to next20closes PRICE risk; unknown paths stay unlabeled.

    Future cash events are unavailable before2018, so this label intentionally
    describes split-adjusted prices, not dividend-inclusive economic wealth.
    """
    swing._matching_calendar(prices, spy)
    labels = []
    for _, group in prices.groupby("symbol", sort=True):
        frame = group[["date", "symbol"]].copy()
        minimum = group.close.iloc[::-1].rolling(20, min_periods=20).min().iloc[::-1].shift(-1)
        frame[KNOWN_DATE] = group.date.shift(-20)
        frame["forward_price_adverse20"] = (minimum / group.open.shift(-1) - 1).where(frame[KNOWN_DATE].notna())
        frame[LABEL] = frame.forward_price_adverse20.le(-.05).astype(float).where(frame[KNOWN_DATE].notna())
        labels.append(frame)
    context = spy[["date"]].copy()
    for window in (20, 63):
        context[f"spy_momentum_{window}"] = spy.close / spy.close.shift(window) - 1
    context["spy_close_ma200"] = spy.close / spy.close.rolling(200).mean() - 1
    context["spy_volatility_20"] = spy.close.pct_change(fill_method=None).rolling(20).std(ddof=0)
    result = features.merge(pd.concat(labels, ignore_index=True), on=["date", "symbol"],
                            how="left", validate="one_to_one", indicator=True)
    if not result._merge.eq("both").all():
        raise ValueError("Downside features contain an unobserved stock session.")
    result = result.drop(columns="_merge").merge(context, on="date", how="left", validate="many_to_one")
    if result.empty or not np.isfinite(result[DOWNSIDE_FEATURES].to_numpy(dtype=float)).all():
        raise ValueError("Every downside feature needs finite, fully warmed trailing context.")
    return result.sort_values(["date", "symbol"]).reset_index(drop=True)


def predict(frame: pd.DataFrame, start: str, end: str, *,
            refit_frequency: str = "annual") -> tuple[pd.DataFrame, list[dict]]:
    """Recent RF fits purge the complete path at every annual/monthly boundary."""
    return swing._walk_forward_predict(frame, "rf_price_downside20", start, end, "rf", LABEL,
                                       KNOWN_DATE, DOWNSIDE_FEATURES, "probability_downside",
                                       refit_frequency=refit_frequency)


def orders(up: pd.DataFrame, down: pd.DataFrame, features: pd.DataFrame) -> pd.DataFrame:
    """Independent high downside wins; only low downside permits an upside BUY."""
    frame = up.merge(down, on=["date", "symbol"], how="outer", validate="one_to_one", indicator=True)
    if (not frame._merge.eq("both").all()
            or not np.isfinite(frame[["probability_up", "probability_downside"]].to_numpy(dtype=float)).all()
            or not frame.probability_up.between(0, 1).all() or not frame.probability_downside.between(0, 1).all()):
        raise ValueError("Upside and downside scores require exact finite same-date coverage in[0,1].")
    approved = swing.filtered_orders(up, features, "trend200")
    frame = frame.drop(columns="_merge").merge(approved, on=["date", "symbol"], how="left", validate="one_to_one")
    risk_sell = frame.probability_downside.ge(.65)
    model_sell = frame.action.eq("SELL")
    buy = frame.action.eq("BUY") & frame.probability_downside.le(.35)
    frame["action"] = np.select([risk_sell, model_sell, buy], ["SELL", "SELL", "BUY"], default="HOLD")
    frame["reason"] = np.where(risk_sell, "downside_model", "up_model")
    frame["allocation"] = 1.
    columns = ["date", "symbol", "action", "allocation", "reason", "probability_up", "probability_downside"]
    return frame.loc[frame.action.ne("HOLD"), columns].sort_values(["date", "symbol"]).reset_index(drop=True)
