"""Offline DCA research only; never imported by live inference or promotion."""

from fractions import Fraction
import math

import numpy as np
import pandas as pd
from sklearn.ensemble import HistGradientBoostingClassifier, RandomForestClassifier
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import StandardScaler

from stocklab_ml.backtesting.accounting import _size_buy
from stocklab_ml.features import FEATURE_COLUMNS, engineer_features
from stocklab_ml.features.engineering import _compute_rsi_wilder, _validate_input
from stocklab_ml.modeling.contracts import TARGET_NAME
from stocklab_ml.modeling.dataset import build_supervised_dataset
from stocklab_ml.modeling.logistic import HYPERPARAMETERS as LOGISTIC_PARAMETERS
from stocklab_ml.modeling.random_forest import HYPERPARAMETERS as FOREST_PARAMETERS

INITIAL_CAPITAL = 100000.0
FEE = Fraction(5, 10000)
SLIPPAGE = Fraction(5, 10000)
MONTHLY_COLUMNS = ["monthly_range_9", "monthly_range_12", "monthly_range_19",
                   "monthly_rsi", "monthly_multiplier"]
RESEARCH_FEATURES = ["return_1d", "rsi_14", "momentum_10", "volatility_20",
                     "close_ma5", "close_ma20", "ma_ratio", "relative_volume"] + MONTHLY_COLUMNS
CANDIDATES = ("rf_base", "rf_dca", "logistic_dca", "boosted_dca")


def monthly_context(monthly: pd.DataFrame, cutoff: pd.Timestamp) -> pd.DataFrame:
    """Original DCA range_rsi_v1; native complete months, never low-touch fills."""
    _validate_input(monthly)
    frames = []
    for symbol, group in monthly.groupby("symbol", sort=True):
        group = group.copy().reset_index(drop=True)
        periods = group.date.dt.to_period("M")
        if (not group.date.dt.is_month_start.all()
                or not np.array_equal(periods.astype("int64").diff().dropna().to_numpy(),
                                      np.ones(len(group) - 1))):
            raise ValueError("Native monthly candles must be consecutive month starts.")
        group["date"] = (group.date + pd.offsets.MonthEnd(0)).astype("datetime64[ns]")
        group = group.loc[group.date < cutoff].copy()
        group["monthly_rsi"] = _compute_rsi_wilder(group.close)
        for window in (9, 12, 19):
            low = group.low.rolling(window).min()
            high = group.high.rolling(window).max()
            group[f"monthly_range_{window}"] = (group.close - low) / (high - low).replace(0, np.nan) * 100
        score = group.monthly_range_12
        group["monthly_multiplier"] = np.select(
            [group.monthly_rsi >= 80, score <= 3, score <= 10, score <= 25], [0., 4., 3., 2.], default=1.)
        group.loc[group.monthly_rsi.isna() | score.isna(), "monthly_multiplier"] = np.nan
        frames.append(group[["date", "symbol"] + MONTHLY_COLUMNS])
    return pd.concat(frames, ignore_index=True)


def research_features(daily: pd.DataFrame, monthly: pd.DataFrame) -> pd.DataFrame:
    """Common rows for all candidates; no original artifact feature is redefined."""
    base = engineer_features(daily).dataframe
    frame = build_supervised_dataset(base).dataframe
    context = monthly_context(monthly, daily.date.max())
    if set(context.symbol) != set(daily.symbol):
        raise ValueError("Daily and monthly symbols must match exactly.")
    joined = []
    for symbol, group in frame.groupby("symbol", sort=True):
        group = pd.merge_asof(group.sort_values("date"),
            context.loc[context.symbol.eq(symbol)].drop(columns="symbol").rename(
                columns={"date": "monthly_available_date"}),
            left_on="date", right_on="monthly_available_date", direction="backward", allow_exact_matches=False)
        if (group.monthly_available_date.notna()
                & group.monthly_available_date.ne(group.date - pd.offsets.MonthEnd(1))).any():
            raise ValueError("A required confirmed monthly candle is missing; stale context is forbidden.")
        group["close_ma5"] = group.close / group.ma_5 - 1
        group["close_ma20"] = group.close / group.ma_20 - 1
        group["ma_ratio"] = group.ma_5 / group.ma_20 - 1
        group["relative_volume"] = group.volume.astype(float) / group.volume.rolling(20).mean().replace(0, np.nan)
        joined.append(group)
    result = pd.concat(joined, ignore_index=True).dropna(subset=RESEARCH_FEATURES)
    if result.empty or not np.isfinite(result[RESEARCH_FEATURES].to_numpy(dtype=float)).all():
        raise ValueError("Insufficient or nonfinite common feature history.")
    return result.sort_values(["date", "symbol"]).reset_index(drop=True)


def training_rows(frame: pd.DataFrame, boundary: pd.Timestamp) -> pd.DataFrame:
    return frame.loc[(frame.date >= boundary - pd.DateOffset(years=4))
                     & (frame.date < boundary) & (frame.target_date < boundary)].copy()


def fit_predict(frame: pd.DataFrame, candidate: str, start: str, end: str) -> tuple[pd.DataFrame, list[dict]]:
    """Annual rolling fits; future test labels never enter a fit or selection."""
    if candidate not in CANDIDATES:
        raise ValueError("Unknown fixed research candidate.")
    predictions, audits = [], []
    features = FEATURE_COLUMNS if candidate == "rf_base" else RESEARCH_FEATURES
    test = frame.loc[frame.date.between(start, end)]
    if test.empty:
        raise ValueError("No evaluation rows.")
    for _, rows in test.groupby(test.date.dt.year, sort=True):
        first = rows.date.min()
        train = training_rows(frame, first)
        if train.empty or set(train[TARGET_NAME]) != {0, 1}:
            raise ValueError("A purged training set with both classes is required.")
        if candidate.startswith("rf_"):
            model = RandomForestClassifier(**FOREST_PARAMETERS)
        elif candidate == "logistic_dca":
            model = make_pipeline(StandardScaler(), LogisticRegression(**LOGISTIC_PARAMETERS))
        else:
            model = HistGradientBoostingClassifier(max_iter=200, learning_rate=.05,
                max_leaf_nodes=15, l2_regularization=1., early_stopping=False, random_state=42)
        model.fit(train[features], train[TARGET_NAME])
        probability = model.predict_proba(rows[features])[:, list(model.classes_).index(1)]
        if not np.isfinite(probability).all() or not ((probability >= 0) & (probability <= 1)).all():
            raise ValueError("Invalid model probabilities.")
        output = rows[["date", "symbol", TARGET_NAME]].copy()
        output["probability_up"] = probability
        predictions.append(output)
        audits.append({"candidate": candidate, "first_fit": train.date.min().date().isoformat(),
                       "last_fit": train.date.max().date().isoformat(),
                       "last_target": train.target_date.max().date().isoformat(),
                       "first_prediction": first.date().isoformat(), "train_rows": len(train)})
    return pd.concat(predictions, ignore_index=True), audits


def select_candidate(reports: dict[str, dict]) -> str:
    """Selection-period evidence only; improvement and the accepted .2pp tolerance."""
    baseline = reports["rf_base"]
    eligible = [name for name in CANDIDATES if name == "rf_base" or (
        reports[name]["max_drawdown"] <= baseline["max_drawdown"] + .002
        and reports[name]["annualized_return"] > baseline["annualized_return"])]
    return max(eligible, key=lambda name: reports[name]["annualized_return"])


def _daily_market(prices: pd.DataFrame, start: str, end: str) -> pd.DataFrame:
    _validate_input(prices)
    market = prices.loc[(prices.date > pd.Timestamp(start)) & (prices.date <= pd.Timestamp(end))]
    calendars = [group.date.tolist() for _, group in market.groupby("symbol", sort=True)]
    if (len(calendars) != prices.symbol.nunique() or not calendars
            or any(calendar != calendars[0] for calendar in calendars[1:])):
        raise ValueError("Research requires matching, nonempty observed symbol calendars.")
    return market


def simulate(prices: pd.DataFrame, decisions: pd.DataFrame, start: str, end: str) -> tuple[pd.DataFrame, pd.DataFrame]:
    """Shared cash, next-open orders, 5bps fee +5bps slippage, unchanged risk caps.

    `allocation` is requested initial-capital fraction, bounded again at execution.
    Rule baselines have no artificial ML confidence. No deposits or dividend cash.
    """
    market = _daily_market(prices, start, end)
    if (list(decisions.columns) != ["date", "symbol", "action", "allocation"]
            or decisions.duplicated(["date", "symbol"]).any()
            or not decisions.action.isin(["BUY", "SELL"]).all()
            or not np.isfinite(decisions.allocation.to_numpy(dtype=float)).all()
            or not decisions.allocation.gt(0).all()
            or not set(decisions.symbol).issubset(set(prices.symbol))):
        raise ValueError("Invalid or duplicate research decisions.")
    observed = set(zip(prices.date, prices.symbol))
    scheduled = {}
    sessions = {symbol: group.date.tolist() for symbol, group in prices.groupby("symbol")}
    following = {(day, symbol): dates[i + 1] for symbol, dates in sessions.items()
                 for i, day in enumerate(dates[:-1])}
    for row in decisions.itertuples(index=False):
        if (row.date, row.symbol) not in observed or not pd.Timestamp(start) <= row.date <= pd.Timestamp(end):
            raise ValueError("Decision is outside the observed evaluation period.")
        next_day = following.get((row.date, row.symbol))
        if next_day is not None and next_day <= pd.Timestamp(end):
            scheduled.setdefault(next_day, []).append(row)
    cash, holdings = Fraction(str(INITIAL_CAPITAL)), {}
    curve = [(pd.Timestamp(start), INITIAL_CAPITAL, INITIAL_CAPITAL)]
    trades = []
    for day, bars in market.groupby("date", sort=True):
        opens = {row.symbol: Fraction(str(row.open)) for row in bars.itertuples(index=False)}
        for row in sorted(scheduled.get(day, []), key=lambda row: row.symbol):
            old = holdings.get(row.symbol, Fraction(0))
            total = cash + sum((q * opens[s] for s, q in holdings.items()), Fraction(0))
            opening = opens[row.symbol]
            if row.action == "SELL":
                if not old:
                    continue
                quantity = old
                fill = opening * (1 - SLIPPAGE)
                fee = quantity * fill * FEE
                all_in = quantity * fill - fee
                cash += all_in
                del holdings[row.symbol]
            else:
                if not old and len(holdings) >= 10:
                    continue
                fill = opening * (1 + SLIPPAGE)
                unit_cost = fill * (1 + FEE)
                friction = unit_cost - opening
                # Exposure uses equity AFTER fee/slippage, so solve for its size.
                quantity_cap = (total / 5 - old * opening) / (opening + friction / 5)
                budget = min(cash, total / 10, Fraction(str(INITIAL_CAPITAL)) * Fraction(str(row.allocation)),
                             max(Fraction(0), quantity_cap) * unit_cost)
                quantity_float, _ = _size_buy(budget, float(unit_cost), 8)
                quantity = Fraction(str(quantity_float))
                if quantity <= 0:
                    continue
                all_in = quantity * unit_cost
                if all_in > budget:
                    quantity -= Fraction(1, 10**8)
                    all_in = quantity * unit_cost
                if quantity <= 0:
                    continue
                fee = quantity * fill * FEE
                cash -= all_in
                holdings[row.symbol] = old + quantity
            if cash < 0:
                raise ValueError("Negative research cash.")
            trades.append((row.date, day, row.symbol, row.action, float(quantity), float(fill),
                           float(fee), float(all_in), float(total)))
        closes = {row.symbol: Fraction(str(row.close)) for row in bars.itertuples(index=False)}
        total = cash + sum((q * closes[s] for s, q in holdings.items()), Fraction(0))
        if not math.isfinite(float(total)) or total <= 0:
            raise ValueError("Invalid research equity.")
        curve.append((day, float(cash), float(total)))
    return (pd.DataFrame(curve, columns=["date", "cash", "total_value"]),
            pd.DataFrame(trades, columns=["decision_date", "execution_date", "symbol", "action",
                                         "quantity", "price", "fee", "all_in", "equity_before"]))


def spy_curve(prices: pd.DataFrame, dates: pd.DatetimeIndex | pd.Series) -> pd.DataFrame:
    """SPY buy-and-hold total-return PROXY, not executable adjusted trade prices."""
    _validate_input(prices)
    if set(prices.symbol) != {"SPY"}:
        raise ValueError("A single SPY source is required.")
    dates = pd.DatetimeIndex(dates)
    rows = prices.loc[prices.date.isin(dates[1:])].sort_values("date")
    if rows.date.tolist() != dates[1:].tolist():
        raise ValueError("SPY and strategy observed sessions must match exactly.")
    fill = Fraction(str(rows.open.iloc[0])) * (1 + SLIPPAGE)
    cost = fill * (1 + FEE)
    quantity, _ = _size_buy(Fraction(str(INITIAL_CAPITAL)), float(cost), 8)
    shares = Fraction(str(quantity))
    residual = Fraction(str(INITIAL_CAPITAL)) - shares * cost
    values = [INITIAL_CAPITAL] + [float(residual + shares * Fraction(str(close))) for close in rows.close]
    return pd.DataFrame({"date": dates, "total_value": values})


def assess(curve: pd.DataFrame, benchmark: pd.DataFrame) -> dict:
    """Fixed-capital, already-net curves; no inferred or interpolated observations."""
    for data in (curve, benchmark):
        if (len(data) < 2 or not pd.api.types.is_datetime64_dtype(data.date.dtype)
                or data.date.isna().any() or data.date.duplicated().any()
                or not data.date.is_monotonic_increasing
                or not data.date.eq(data.date.dt.normalize()).all()
                or not np.isfinite(data.total_value.to_numpy(dtype=float)).all()
                or not data.total_value.gt(0).all()):
            raise ValueError("Invalid positive, strictly chronological daily equity curve.")
    if (curve.date.tolist() != benchmark.date.tolist()
            or curve.total_value.iloc[0] != benchmark.total_value.iloc[0]):
        raise ValueError("Curve dates and initial capital must match exactly.")
    days = (curve.date.iloc[-1] - curve.date.iloc[0]).days
    returns = float(curve.total_value.iloc[-1] / curve.total_value.iloc[0] - 1)
    reference = float(benchmark.total_value.iloc[-1] / benchmark.total_value.iloc[0] - 1)
    annualized = math.expm1(math.log1p(returns) * 365.2425 / days)
    benchmark_annualized = math.expm1(math.log1p(reference) * 365.2425 / days)
    return {"total_return": returns, "annualized_return": annualized,
            "benchmark_return": reference, "benchmark_annualized_return": benchmark_annualized,
            "max_drawdown": float((1 - curve.total_value / curve.total_value.cummax()).max()),
            "days": days, "target_met": bool(days >= 365.2425 and annualized >= .20 and returns > reference)}
