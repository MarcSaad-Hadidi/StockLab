"""Observed-history training episodes; never change evaluation calendars or fills."""

from collections import Counter
import re

import numpy as np
import pandas as pd

from stocklab_ml import downside_research as downside, swing_research as swing
from stocklab_ml.data.validation import COLUMNS, SYMBOL_PATTERN
from stocklab_ml.features.engineering import _validate_input


def validate_identity(catalog: dict, metadata: dict) -> None:
    """Bind raw daily-response metadata to one frozen eligible catalog record."""
    if not isinstance(catalog, dict) or not isinstance(metadata, dict):
        raise ValueError("Catalog and provider identity metadata are required.")
    keys = ("symbol", "exchange", "mic_code", "currency", "type")
    if (any(not isinstance(catalog.get(key), str) or not catalog[key] for key in keys)
            or catalog["exchange"] not in ("NYSE", "NASDAQ")
            or catalog["currency"] != "USD" or catalog["type"] != "Common Stock"
            or not re.fullmatch(r"[A-Z0-9]{4}", catalog["mic_code"])
            or not re.fullmatch(SYMBOL_PATTERN, catalog["symbol"] + ":" + catalog["exchange"])
            or any(metadata.get(key) != catalog[key] for key in keys)
            or metadata.get("interval") != "1day"
            or metadata.get("exchange_timezone") != "America/New_York"):
        raise ValueError("Provider identity differs from the frozen US common-stock contract.")


def _positions(prices: pd.DataFrame, full_spy: pd.DataFrame) -> np.ndarray:
    _validate_input(prices)
    _validate_input(full_spy)
    if prices.symbol.nunique() != 1 or set(full_spy.symbol) != {"SPY"}:
        raise ValueError("One training identity and a single SPY calendar are required.")
    positions = pd.Index(full_spy.date).get_indexer(prices.date)
    if (positions < 0).any():
        raise ValueError("Training prices contain a session outside the SPY calendar.")
    return positions


def _monthly(prices: pd.DataFrame, full_spy: pd.DataFrame) -> tuple[pd.DataFrame, list[dict]]:
    expected = {period: group.date.tolist()
                for period, group in full_spy.groupby(full_spy.date.dt.to_period("M"), sort=True)}
    first_month, as_of = full_spy.date.iloc[0].to_period("M"), full_spy.date.iloc[-1]
    rows, excluded = [], []
    for period, group in prices.groupby(prices.date.dt.to_period("M"), sort=True):
        reason = None
        if period.end_time.normalize() > as_of:
            reason = "open_month"
        elif period == first_month:
            # The supplied SPY left boundary cannot certify a complete first month.
            reason = "spy_boundary_month"
        elif group.date.tolist() != expected[period]:
            reason = "partial_month"
        if reason is not None:
            excluded.append({"month": str(period), "reason": reason})
            continue
        volume = sum(int(value) for value in group.volume)
        if volume > 2**63 - 1:
            raise ValueError("Aggregated monthly volume exceeds the cleaned Int64 contract.")
        rows.append({"date": period.start_time, "symbol": group.symbol.iloc[0],
                     "open": group.open.iloc[0], "high": group.high.max(), "low": group.low.min(),
                     "close": group.close.iloc[-1], "volume": volume})
    monthly = pd.DataFrame(rows, columns=COLUMNS).astype(prices.dtypes.to_dict())
    if not monthly.empty:
        _validate_input(monthly)
    return monthly, excluded


def aggregate_monthly(prices: pd.DataFrame, full_spy: pd.DataFrame) -> pd.DataFrame:
    """Aggregate only closed months containing every full-SPY observed session."""
    _positions(prices, full_spy)
    return _monthly(prices, full_spy)[0]


def _empty_frame(prices: pd.DataFrame, features: list[str], label: str, known: str) -> pd.DataFrame:
    frame = prices.iloc[:0][["date", "symbol"]].copy()
    for column in features + [label]:
        frame[column] = pd.Series(dtype="float64")
    frame[known] = pd.Series(dtype="datetime64[ns]")
    return frame


def build_identity(prices: pd.DataFrame, full_spy: pd.DataFrame) -> tuple[pd.DataFrame, pd.DataFrame, dict]:
    """Reset feature history at every absent SPY session; retain unknown tail labels."""
    positions = _positions(prices, full_spy)
    calendar = full_spy.loc[full_spy.date.between(prices.date.iloc[0], prices.date.iloc[-1]), "date"]
    missing = calendar.loc[~calendar.isin(prices.date)]
    report = {"symbol": str(prices.symbol.iloc[0]), "monthly_provenance": "daily_derived",
              "input_rows": len(prices), "internal_missing_sessions": len(missing),
              "missing_dates": [day.date().isoformat() for day in missing], "episodes": []}
    up_frames, down_frames, reasons, month_reasons = [], [], Counter(), Counter()
    boundaries = np.flatnonzero(np.diff(positions) != 1) + 1
    for number, indices in enumerate(np.split(np.arange(len(prices)), boundaries), start=1):
        episode = prices.iloc[indices].reset_index(drop=True)
        reference = full_spy.iloc[positions[indices]].reset_index(drop=True)
        start, end = episode.date.iloc[0].date().isoformat(), episode.date.iloc[-1].date().isoformat()
        monthly, excluded = _monthly(episode, full_spy)
        month_reasons.update(row["reason"] for row in excluded)
        details = {"episode": number, "start": start, "end": end, "input_rows": len(episode),
                   "complete_months": len(monthly), "excluded_months": excluded,
                   "up_rows": 0, "down_rows": 0, "tail_missing_up": 0, "tail_missing_down": 0}
        reason = None
        if (len(episode) < 253 or len(monthly) < 19
                or episode.date.iloc[-1] <= monthly.date.iloc[18] + pd.offsets.MonthEnd(0)):
            reason = "insufficient_warmup"
        else:
            try:
                up = swing.swing_features(episode, monthly, reference)
            except ValueError as error:
                if str(error) not in ("Insufficient or nonfinite common feature history.",
                                      "Insufficient or nonfinite swing feature history."):
                    raise
                reason = "nonfinite_feature_history"
        if reason is not None:
            reasons[reason] += len(episode)
            details.update(status=reason, discarded_ranges=[{"start": start, "end": end, "reason": reason}])
        else:
            down = downside.make_frame(up, episode, reference)
            warmup = episode.loc[episode.date < up.date.iloc[0], "date"]
            unavailable = episode.loc[(episode.date >= up.date.iloc[0]) & ~episode.date.isin(up.date), "date"]
            reasons["feature_warmup"] += len(warmup)
            discarded = [{"start": start, "end": warmup.iloc[-1].date().isoformat(), "reason": "feature_warmup"}]
            if not unavailable.empty:
                reasons["feature_unavailable"] += len(unavailable)
                for _, days in unavailable.groupby(unavailable.index.to_series().diff().ne(1).cumsum()):
                    discarded.append({"start": days.iloc[0].date().isoformat(),
                                      "end": days.iloc[-1].date().isoformat(), "reason": "feature_unavailable"})
            details.update(status="usable", up_rows=len(up), down_rows=len(down), warmup_rows=len(warmup),
                           tail_missing_up=int(up.target_20.isna().sum()),
                           tail_missing_down=int(down[downside.LABEL].isna().sum()),
                           discarded_ranges=discarded)
            up_frames.append(up)
            down_frames.append(down)
        report["episodes"].append(details)
    up = (pd.concat(up_frames, ignore_index=True) if up_frames
          else _empty_frame(prices, swing.SWING_FEATURES, "target_20", "target_date_20"))
    down = (pd.concat(down_frames, ignore_index=True) if down_frames
            else _empty_frame(prices, downside.DOWNSIDE_FEATURES, downside.LABEL, downside.KNOWN_DATE))
    report.update(status="usable" if up_frames else "insufficient_history", up_rows=len(up), down_rows=len(down),
                  up_labeled_rows=int(up.target_20.notna().sum()),
                  down_labeled_rows=int(down[downside.LABEL].notna().sum()),
                  reason_counts=dict(reasons), monthly_exclusion_counts=dict(month_reasons))
    return up, down, report
