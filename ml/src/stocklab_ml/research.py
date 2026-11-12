"""Offline DCA research only; never imported by live inference or promotion."""

from fractions import Fraction
import math
from numbers import Integral

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


def research_features(daily: pd.DataFrame, monthly: pd.DataFrame, *, labeled_only: bool = True) -> pd.DataFrame:
    """Common rows for all candidates; no original artifact feature is redefined."""
    base = engineer_features(daily).dataframe
    frame = build_supervised_dataset(base).dataframe if labeled_only else base
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


def _execution_costs(fee_bps: int, slippage_bps: int) -> tuple[Fraction, Fraction]:
    if any(isinstance(value, (bool, np.bool_)) or not isinstance(value, Integral) or not 0 <= value < 10000
           for value in (fee_bps, slippage_bps)):
        raise ValueError("Fee/slippage assumptions must be integer basis points in0..9999.")
    return Fraction(int(fee_bps), 10000), Fraction(int(slippage_bps), 10000)


def _dividend_events(dividends: pd.DataFrame | None, prices: pd.DataFrame, start: str, end: str) -> dict:
    if dividends is None:
        return {}
    if (not isinstance(dividends, pd.DataFrame) or list(dividends.columns) != ["symbol", "ex_date", "pay_date", "amount"]
            or dividends.isna().any().any() or dividends.duplicated(["symbol", "ex_date"]).any()
            or dividends.duplicated(["symbol", "pay_date"]).any()
            or not set(dividends.symbol).issubset(set(prices.symbol))
            or not pd.api.types.is_numeric_dtype(dividends.amount.dtype)
            or pd.api.types.is_bool_dtype(dividends.amount.dtype)
            or not np.isfinite(dividends.amount.to_numpy(dtype=float)).all() or not dividends.amount.gt(0).all()):
        raise ValueError("Invalid or duplicate split-adjusted cash dividend events.")
    for column in ("ex_date", "pay_date"):
        if (str(dividends[column].dtype) != "datetime64[ns]"
                or not dividends[column].eq(dividends[column].dt.normalize()).all()):
            raise ValueError("Dividend dates must be timezone-naive daily dates.")
    if not dividends.pay_date.ge(dividends.ex_date).all():
        raise ValueError("Unsupported dividend payment before its ex-date.")
    observed = set(zip(prices.date, prices.symbol))
    events = {}
    for row in dividends.itertuples(index=False):
        if pd.Timestamp(start) < row.ex_date <= pd.Timestamp(end):
            if (row.ex_date, row.symbol) not in observed:
                raise ValueError("A dividend ex-date is absent from the observed stock calendar.")
            events.setdefault(row.ex_date, []).append(row)
    return events


def _spy_risk_states(spy: pd.DataFrame | None, prices: pd.DataFrame, start: str, end: str) -> dict:
    """Confirmed regime known BEFORE each open; no same-close execution."""
    if spy is None:
        raise ValueError("SPY history is required for the regime policy.")
    _validate_input(spy)
    rows = spy.loc[spy.date <= pd.Timestamp(end)].sort_values("date")
    calendar = prices.loc[prices.date <= pd.Timestamp(end), "date"].drop_duplicates().sort_values()
    if (set(spy.symbol) != {"SPY"} or len(calendar.loc[calendar <= pd.Timestamp(start)]) < 200
            or rows.loc[rows.date >= calendar.min(), "date"].tolist() != calendar.tolist()):
        raise ValueError("SPY needs matching observed sessions and 200-session pre-start context.")
    average = rows.close.rolling(200).mean()
    bearish, below, above, states = False, 0, 0, {}
    for day, close, mean in zip(rows.date, rows.close, average):
        states[day] = bearish
        below = below + 1 if close < mean else 0
        above = above + 1 if close > mean else 0
        if below >= 3:
            bearish = True
        elif above >= 3:
            bearish = False
    return states


def simulate(prices: pd.DataFrame, decisions: pd.DataFrame, start: str, end: str, *,
             fee_bps: int = 5, slippage_bps: int = 5,
             dividends: pd.DataFrame | None = None, risk_policy: str | None = None,
             spy: pd.DataFrame | None = None) -> tuple[pd.DataFrame, pd.DataFrame]:
    """Shared cash, next-open orders, default5bps fee +5bps slippage, unchanged caps.

    `allocation` is requested initial-capital fraction, bounded again at execution.
    Rule baselines have no artificial ML confidence. No deposits. Optional cash
    dividends use current split-adjusted amounts, prior holdings at ex-date and
    spendable cash strictly after the actual pay date; receivables are equity.
    Optional protective exits are next-open risk orders, not ML forecasts. Stops
    use dividend-aware held-close returns; every risk sale blocks 20 later opens.
    """
    fee_rate, slippage = _execution_costs(fee_bps, slippage_bps)
    market = _daily_market(prices, start, end)
    cash_events = _dividend_events(dividends, prices, start, end)
    if risk_policy not in (None, "trailing15", "trailing15_spy200"):
        raise ValueError("Unknown fixed research risk policy.")
    bearish = _spy_risk_states(spy, prices, start, end) if risk_policy == "trailing15_spy200" else {}
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
    pending, earned, paid = [], Fraction(0), Fraction(0)
    curve = [(pd.Timestamp(start), INITIAL_CAPITAL, INITIAL_CAPITAL, 0., 0., 0.)]
    trades = []
    stops, peaks, signal, previous_close, blocked_until = {}, {}, {}, {}, {}
    for session, (day, bars) in enumerate(market.groupby("date", sort=True)):
        remaining = []
        for pay_date, amount in pending:
            if pay_date < day:
                cash += amount
                paid += amount
            else:
                remaining.append((pay_date, amount))
        pending = remaining
        # A buyer at today's open has no right to today's detached dividend.
        for event in cash_events.get(day, []):
            amount = holdings.get(event.symbol, Fraction(0)) * Fraction(str(event.amount))
            if amount:
                pending.append((event.pay_date, amount))
                earned += amount
        receivable = earned - paid
        if receivable != sum((amount for _, amount in pending), Fraction(0)):
            raise ValueError("Dividend receivable accounting does not reconcile.")
        opens = {row.symbol: Fraction(str(row.open)) for row in bars.itertuples(index=False)}
        risk_orders = dict(stops)
        if bearish.get(day, False):
            # The confirmed regime takes priority over any simultaneous stop.
            prior = prices.loc[prices.date < day, "date"].max()
            risk_orders.update({symbol: (prior, "spy_regime") for symbol in holdings})
        normal = [(row.date, row.symbol, row.action, row.allocation, "decision")
                  for row in sorted(scheduled.get(day, []), key=lambda row: row.symbol)]
        executions = [(decision_day, symbol, "SELL", 1., reason)
                      for symbol, (decision_day, reason) in sorted(risk_orders.items())] + normal
        for decision_day, symbol, action, allocation, reason in executions:
            old = holdings.get(symbol, Fraction(0))
            total = cash + receivable + sum((q * opens[s] for s, q in holdings.items()), Fraction(0))
            opening = opens[symbol]
            if action == "SELL":
                if not old:
                    continue
                quantity = old
                fill = opening * (1 - slippage)
                fee = quantity * fill * fee_rate
                all_in = quantity * fill - fee
                cash += all_in
                del holdings[symbol]
                if risk_policy is not None:
                    for state in (peaks, signal, previous_close):
                        state.pop(symbol, None)
                    if reason != "decision":
                        blocked_until[symbol] = session + 20
            else:
                if (symbol in risk_orders or bearish.get(day, False)
                        or session <= blocked_until.get(symbol, -1)):
                    continue
                if not old and len(holdings) >= 10:
                    continue
                fill = opening * (1 + slippage)
                unit_cost = fill * (1 + fee_rate)
                friction = unit_cost - opening
                # Exposure uses equity AFTER fee/slippage, so solve for its size.
                quantity_cap = (total / 5 - old * opening) / (opening + friction / 5)
                budget = min(cash, total / 10, Fraction(str(INITIAL_CAPITAL)) * Fraction(str(allocation)),
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
                fee = quantity * fill * fee_rate
                cash -= all_in
                holdings[symbol] = old + quantity
                if risk_policy is not None and not old:
                    signal[symbol] = peaks[symbol] = opening
            if cash < 0:
                raise ValueError("Negative research cash.")
            trade = (decision_day, day, symbol, action, float(quantity), float(fill),
                     float(fee), float(all_in), float(total))
            trades.append(trade + (reason,) if risk_policy is not None else trade)
        closes = {row.symbol: Fraction(str(row.close)) for row in bars.itertuples(index=False)}
        stops = {}
        if risk_policy is not None:
            detached = {event.symbol: Fraction(str(event.amount)) for event in cash_events.get(day, [])}
            for symbol in holdings:
                if symbol not in previous_close:
                    signal[symbol] = closes[symbol]
                else:
                    signal[symbol] *= (closes[symbol] + detached.get(symbol, Fraction(0))) / previous_close[symbol]
                peaks[symbol] = max(peaks[symbol], signal[symbol])
                if signal[symbol] <= peaks[symbol] * Fraction(85, 100):
                    stops[symbol] = (day, "trailing_stop")
                previous_close[symbol] = closes[symbol]
        total = cash + receivable + sum((q * closes[s] for s, q in holdings.items()), Fraction(0))
        if not math.isfinite(float(total)) or total <= 0:
            raise ValueError("Invalid research equity.")
        curve.append((day, float(cash), float(total), float(receivable), float(earned), float(paid)))
    equity = pd.DataFrame(curve, columns=["date", "cash", "total_value", "dividends_receivable", "dividends_earned", "dividends_paid"])
    if dividends is None:
        equity = equity[["date", "cash", "total_value"]]
    return (equity,
            pd.DataFrame(trades, columns=["decision_date", "execution_date", "symbol", "action",
                                         "quantity", "price", "fee", "all_in", "equity_before"]
                         + (["reason"] if risk_policy is not None else [])))


def spy_curve(prices: pd.DataFrame, dates: pd.DatetimeIndex | pd.Series, *,
              fee_bps: int = 5, slippage_bps: int = 5) -> pd.DataFrame:
    """SPY buy-and-hold total-return PROXY, not executable adjusted trade prices."""
    fee_rate, slippage = _execution_costs(fee_bps, slippage_bps)
    _validate_input(prices)
    if set(prices.symbol) != {"SPY"}:
        raise ValueError("A single SPY source is required.")
    dates = pd.DatetimeIndex(dates)
    rows = prices.loc[prices.date.isin(dates[1:])].sort_values("date")
    if rows.date.tolist() != dates[1:].tolist():
        raise ValueError("SPY and strategy observed sessions must match exactly.")
    fill = Fraction(str(rows.open.iloc[0])) * (1 + slippage)
    cost = fill * (1 + fee_rate)
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
