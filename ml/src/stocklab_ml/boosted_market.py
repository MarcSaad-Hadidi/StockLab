"""Stock-only universe discovery and daily scoring for the directional boost."""

from dataclasses import dataclass
import hashlib
import json
import re

import joblib
import numpy as np
import pandas as pd

from stocklab_ml import research
from stocklab_ml.data.validation import SYMBOL_PATTERN


SWING_FEATURES = ["momentum_20", "momentum_63", "momentum_126", "momentum_252", "close_ma50",
                  "close_ma200", "volatility_63", "drawdown_252"]
FEATURES = research.RESEARCH_FEATURES + SWING_FEATURES
BAR_COLUMNS = ["date", "symbol", "open", "high", "low", "close", "volume"]
KEY = ["date", "symbol"]


@dataclass(frozen=True, order=True)
class Listing:
    symbol: str
    mic: str
    exchange: str
    figi: str


def common_stock_listings(payload: dict) -> tuple[Listing, ...]:
    """Use provider instrument type and venue, independent of score and history."""
    rows = payload.get("data")
    if payload.get("status") != "ok" or not isinstance(rows, list) or payload.get("count") != len(rows):
        raise ValueError("Invalid or partial stock catalog response.")
    result = {}
    for row in rows:
        if (row.get("country") != "United States" or row.get("currency") != "USD"
                or row.get("type") != "Common Stock" or row.get("symbol") == "SPY"):
            continue
        symbol, mic = row.get("symbol"), row.get("mic_code")
        if (not isinstance(symbol, str) or not isinstance(mic, str)
                or not re.fullmatch(SYMBOL_PATTERN, symbol) or ":" in symbol or not re.fullmatch(r"[A-Z0-9]{4}", mic)):
            # Transition aliases such as !otc/TICKER are not orderable symbols.
            continue
        listing = Listing(symbol, mic, row.get("exchange", ""), row.get("figi_code") or "")
        key = (listing.symbol, listing.mic)
        if key in result and result[key] != listing:
            raise ValueError(f"Conflicting stock listing for {listing.symbol} on {listing.mic}.")
        result[key] = listing
    return tuple(sorted(result.values()))


def _indicators(bars: pd.DataFrame, monthly: pd.DataFrame) -> pd.DataFrame:
    base = research.research_features(bars, monthly, labeled_only=False)
    swing = bars[KEY].copy()
    close = bars.close
    for window in (20, 63, 126, 252):
        swing[f"momentum_{window}"] = close.pct_change(window, fill_method=None)
    for window in (50, 200):
        swing[f"close_ma{window}"] = close / close.rolling(window).mean() - 1.
    swing["volatility_63"] = close.pct_change(fill_method=None).rolling(63).std(ddof=0)
    swing["drawdown_252"] = close / close.rolling(252).max() - 1.
    future_open = bars.open.shift(-21) / bars.open.shift(-1) - 1.
    swing["target_date_20"] = bars.date.shift(-21)
    swing["target_20"] = future_open.gt(.002).astype(float).where(future_open.notna())
    return base.merge(swing, on=KEY, validate="one_to_one").dropna(subset=FEATURES)


def stock_features(bars: pd.DataFrame, calendar: pd.DatetimeIndex) -> pd.DataFrame:
    """Reset feature history across absent sessions and exclude partial months."""
    if (not calendar.is_unique or not calendar.is_monotonic_increasing or len(calendar) == 0
            or bars.symbol.nunique() != 1 or bars.symbol.iloc[0].split(":", 1)[0] == "SPY"):
        raise ValueError("An ordered exchange calendar and one ordinary stock are required.")
    locations = calendar.get_indexer(bars.date)
    if (locations < 0).any() or (np.diff(locations) <= 0).any():
        raise ValueError("Stock dates must increase within the exchange calendar.")
    expected = calendar.to_series().groupby(calendar.to_period("M")).size()
    episodes = np.r_[0, (np.diff(locations) != 1).cumsum()]
    output = []
    for _, sequence in bars.groupby(episodes, sort=False):
        if len(sequence) <= 252:
            continue
        groups = sequence.groupby(sequence.date.dt.to_period("M"), sort=True)
        monthly = groups.agg(symbol=("symbol", "first"), open=("open", "first"), high=("high", "max"),
                             low=("low", "min"), close=("close", "last"), volume=("volume", "sum"))
        counts = groups.size()
        complete = counts.eq(expected.reindex(counts.index)) & (counts.index < calendar[-1].to_period("M"))
        monthly = monthly.loc[complete].copy()
        if len(monthly) < 20:
            continue
        monthly["date"] = monthly.index.to_timestamp().as_unit("ns")
        monthly["volume"] = monthly.volume.astype("Int64")
        try:
            stock = _indicators(sequence[BAR_COLUMNS].reset_index(drop=True), monthly[BAR_COLUMNS].reset_index(drop=True))
        except ValueError as error:
            if str(error) != "Insufficient or nonfinite common feature history.":
                raise
            continue
        if not stock.empty:
            output.append(stock)
    if not output:
        return pd.DataFrame(columns=KEY + FEATURES + ["target_date_20", "target_20"])
    frame = pd.concat(output, ignore_index=True)
    if not np.isfinite(frame[FEATURES].to_numpy(dtype=float)).all():
        raise ValueError("Nonfinite directional model features.")
    return frame


def scored_candidates(model, features: pd.DataFrame) -> pd.DataFrame:
    """Score every eligible stock row, then identify decisions from its own price history."""
    if list(model.feature_names_in_) != FEATURES or list(model.classes_) != [0., 1.]:
        raise ValueError("The directional estimator does not match the stock-only schema.")
    result = features[KEY + ["momentum_252", "monthly_multiplier"]].copy()
    if result.duplicated(KEY).any() or result.symbol.str.split(":").str[0].eq("SPY").any():
        raise ValueError("Each dated stock identity must occur once and exclude the benchmark.")
    probability = model.predict_proba(features[FEATURES])[:, 1]
    if not np.isfinite(probability).all() or ((probability < 0.) | (probability > 1.)).any():
        raise ValueError("Invalid directional scores.")
    buys = (probability >= .65) & features.close_ma50.gt(0)
    sells = (probability <= .35) & features.close_ma200.lt(0) & features.momentum_20.lt(0)
    result["probability_up"] = probability
    result["action"] = np.select([buys, sells], ["BUY", "SELL"], default="HOLD")
    return result


def load_directional_model(directory, first_session: pd.Timestamp):
    """Load the checksum-pinned local fold whose outcomes matured before use."""
    summary = json.loads((directory / "summary.json").read_bytes())
    year = str(first_session.year)
    trained = summary["annual_fits"][year]["directional"]
    if not pd.Timestamp(trained["last_target_date"]) < pd.Timestamp(trained["first_forecast"]) <= first_session:
        raise ValueError("The model was not available on the requested decision date.")
    relative = f"estimators/{year}-direction.joblib"
    path = directory / relative
    with path.open("rb") as artifact:
        digest = hashlib.file_digest(artifact, "sha256").hexdigest()
    if digest != summary["files"][relative]:
        raise ValueError(f"Directional artifact checksum differs for {year}.")
    return joblib.load(path)


def latest_candidates(model, bars: pd.DataFrame, calendar: pd.DatetimeIndex, listings: tuple[Listing, ...]):
    """Evaluate today's confirmed bar for every catalog identity with sufficient observations."""
    as_of = calendar[-1]
    accepted = {(item.symbol, item.mic) for item in listings}
    panels = []
    for identity, history in bars.groupby(["symbol", "mic"], sort=True):
        if identity not in accepted:
            continue
        observed = history.loc[history.date.le(as_of)].sort_values("date", ignore_index=True)
        if observed.empty or observed.date.iloc[-1] != as_of or observed.volume.iloc[-1] <= 0:
            continue
        inputs = observed[BAR_COLUMNS].copy()
        inputs["symbol"] = pd.Series(":".join(identity), dtype="string", index=inputs.index)
        features = stock_features(inputs, calendar)
        panels.append(features.loc[features.date.eq(as_of), KEY + FEATURES])
    if not panels or all(panel.empty for panel in panels):
        return pd.DataFrame(columns=KEY + ["probability_up", "action"])
    scores = scored_candidates(model, pd.concat([panel for panel in panels if not panel.empty], ignore_index=True))
    return scores.sort_values(["action", "monthly_multiplier", "probability_up", "symbol"],
                              ascending=[True, False, False, True], ignore_index=True)
