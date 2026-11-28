"""Pinned v50 next-open parity and a separately named funded comparison."""

from calendar import monthrange
from datetime import date, timedelta
from fractions import Fraction
import math
from numbers import Real

import pandas as pd

from stocklab_ml.dca_signals import ENGINE_VERSION
from stocklab_ml.features.engineering import _validate_input
from stocklab_ml.research import _dividend_events, _execution_costs

NEXT_OPEN_EXECUTION_VERSION = "next_confirmed_session_open_v1"
MULTIPLIERS = {"ADD": 1, "BUY": 2, "STRONG_BUY": 3, "EXTREME_BUY": 4,
    "LIGHT_TRIM": 0, "STRONG_TRIM": 0, "UNAVAILABLE": 0}
US_EXCHANGES = {"NASDAQ", "NYSE", "AMEX", "NYSE AMERICAN", "NYSE ARCA", "NYSEARCA", "ARCA",
    "BATS", "CBOE", "XNAS", "XNYS", "XASE", "ARCX"}


def _iso(value):
    if not isinstance(value, str):
        raise ValueError("Dates must be canonical ISO daily strings.")
    try:
        parsed = date.fromisoformat(value)
    except ValueError as error:
        raise ValueError("Dates must be canonical ISO daily strings.") from error
    if parsed.isoformat() != value:
        raise ValueError("Dates must be canonical ISO daily strings.")
    return value


def _positive(value):
    return isinstance(value, Real) and not isinstance(value, bool) and math.isfinite(value) and value > 0


def _next_month(month):
    year, number = map(int, month.split("-"))
    return f"{year + (number == 12):04}-{number % 12 + 1:02}"


def _month_end(month):
    year, number = map(int, month.split("-"))
    return date(year, number, monthrange(year, number)[1]).isoformat()


def _declared_sessions(meta):
    sessions = meta.get("sessions")
    through = _iso(meta.get("calendarThrough"))
    if not isinstance(sessions, list) or any(_iso(day) != day for day in sessions) or sessions != sorted(set(sessions)):
        raise ValueError("Declared calendar sessions must be unique and chronological.")
    return sessions, through


def _planned_month_sessions(dataset, month, start):
    meta, last = dataset["meta"], _month_end(month)
    if meta.get("calendarMode") == "declared_sessions":
        sessions, through = _declared_sessions(meta)
        if through < last:
            raise ValueError("The declared calendar must cover the whole month.")
        result = [day for day in sessions if max(start, month + "-01") <= day <= last]
        if not result:
            raise ValueError("No declared calendar session in the requested month.")
        return result
    year, number = map(int, month.split("-"))
    if (meta.get("assetClass") not in ("stock", "etf") or meta.get("currency") != "USD"
            or str(meta.get("exchange", "")).strip().upper() not in US_EXCHANGES or not 1971 <= year <= 2099):
        raise ValueError("The v50 US fallback calendar requires a supported USD listing in1971–2099.")

    def weekday(month_number, wanted, nth):
        first = date(year, month_number, 1)
        return first + timedelta(days=(wanted - first.weekday()) % 7 + (nth - 1) * 7)

    def observed(month_number, day):
        value = date(year, month_number, day)
        return value + timedelta(days=-1 if value.weekday() == 5 else 1 if value.weekday() == 6 else 0)

    a, b, c = year % 19, year // 100, year % 100
    d, e, f = b // 4, b % 4, (b + 8) // 25
    g = (b - f + 1) // 3
    h = (19 * a + b - d - g + 15) % 30
    i, k = c // 4, c % 4
    l = (32 + 2 * e + 2 * i - h - k) % 7
    m = (a + 11 * h + 22 * l) // 451
    value = h + l - 7 * m + 114
    easter = date(year, value // 31, value % 31 + 1)
    memorial = date(year, 5, 31)
    memorial -= timedelta(days=memorial.weekday())
    holidays = {weekday(2, 0, 3), easter - timedelta(days=2), memorial, observed(7, 4),
        weekday(9, 0, 1), weekday(11, 3, 4), observed(12, 25)}
    new_year = date(year, 1, 1)
    if new_year.weekday() != 5:
        holidays.add(new_year + timedelta(days=1 if new_year.weekday() == 6 else 0))
    if year >= 1998:
        holidays.add(weekday(1, 0, 3))
    if year >= 2022:
        holidays.add(observed(6, 19))
    if year == 2025:
        holidays.add(date(2025, 1, 9))
    # This is the pinned v50 fallback, including its exceptional-closure limits.
    return [date(year, number, day).isoformat() for day in range(1, monthrange(year, number)[1] + 1)
        if date(year, number, day).weekday() < 5 and date(year, number, day) not in holidays
        and date(year, number, day).isoformat() >= start]


def next_signal_session(dataset, point, end_date):
    known = max(_iso(point["availableDate"]), _iso(point["periodEnd"]))
    start = (date.fromisoformat(known) + timedelta(days=1)).isoformat()
    end_date = _iso(end_date)
    if start > end_date:
        return None
    meta, following = dataset["meta"], None
    if meta.get("calendarMode") == "declared_sessions":
        sessions, through = _declared_sessions(meta)
        following = next((day for day in sessions if start <= day <= end_date and day <= through), None)
        if following is None and through < end_date:
            raise ValueError("The declared calendar does not cover the next session.")
    else:
        month = start[:7]
        while month <= end_date[:7]:
            following = next((day for day in _planned_month_sessions(dataset, month, start) if day <= end_date), None)
            if following:
                break
            month = _next_month(month)
    if following:
        candle = next((bar for bar in dataset["candles"] if bar["date"] == following), None)
        if (following > _iso(meta.get("closedThrough")) or candle is None
                or following in dataset.get("excludedDailyPeriods", []) or not _positive(candle.get("open"))):
            raise ValueError("The first expected session opening is missing or excluded: " + following)
    return following


def reference_periods(signals, config):
    start, end = _iso(config["startDate"]), _iso(config["endDate"])
    if start > end:
        raise ValueError("The requested date window is inverted.")
    periods = [point for point in signals if point.get("confirmed") is True
        and point.get("category") != "UNAVAILABLE" and point.get("score") is not None
        and _iso(point.get("date")) >= start and _iso(point.get("availableDate")) <= end]
    periods.sort(key=lambda point: point["availableDate"])
    available, months = set(), set()
    for point in periods:
        month = _iso(point.get("periodEnd"))[:7]
        if (point["date"] > point["availableDate"] or point["availableDate"] in available or month in months
                or point["periodEnd"] != _month_end(month) or point["date"][:7] != month
                or point.get("engineVersion") != ENGINE_VERSION or not _positive(point.get("close"))
                or point.get("category") not in MULTIPLIERS
                or isinstance(point.get("multiplier"), bool)
                or point.get("multiplier") != MULTIPLIERS[point["category"]]
                or not isinstance(point["score"], Real) or not math.isfinite(point["score"])):
            raise ValueError("Incoherent or duplicate confirmed monthly signal.")
        available.add(point["availableDate"])
        months.add(month)
    return periods


def _daily_window(dataset, start, end):
    meta = dataset["meta"]
    closed = _iso(meta.get("closedThrough"))
    history = sorted((bar for bar in dataset["candles"] if _iso(bar["date"]) <= closed), key=lambda bar: bar["date"])
    if not history:
        raise ValueError("No closed daily execution prices.")
    first, last = history[0]["date"], history[-1]["date"]
    if start < first:
        missing = start[:7] < first[:7]
        if not missing:
            if meta.get("calendarMode") == "declared_sessions":
                missing = any(start <= day < first for day in _declared_sessions(meta)[0])
            else:
                try:
                    missing = _planned_month_sessions(dataset, first[:7], start)[0] < first
                except (ValueError, IndexError):
                    pass
        if missing:
            raise ValueError("The requested start is outside daily execution coverage.")
    if dataset.get("monthly", {}).get("closedThrough", "") > last and min(end, closed)[:7] > last[:7]:
        raise ValueError("The requested end is outside daily execution coverage.")
    rows = [bar for bar in history if start <= bar["date"] <= end]
    if not rows or len({bar["date"] for bar in rows}) != len(rows):
        raise ValueError("Daily execution sessions must be nonempty and unique.")
    for bar in rows:
        if (not all(_positive(bar.get(key)) for key in ("open", "high", "low", "close"))
                or bar["low"] > min(bar["open"], bar["close"]) or bar["high"] < max(bar["open"], bar["close"])):
            raise ValueError("Invalid daily OHLC execution price.")
    months = sorted({bar["date"][:7] for bar in rows})
    if any(_next_month(left) < right for left, right in zip(months, months[1:])):
        raise ValueError("A daily execution month is missing.")
    return rows


def run_next_open(dataset, signals, config):
    """v50 buy-only parity: signal-budget inflows and its retrospective C/N comparator."""
    if (config.get("method") != "next_open" or config.get("frequency", "monthly") != "monthly"
            or config.get("strategy") != "2to4" or not _positive(config.get("monthlyAmount"))
            or config.get("currency") != dataset["meta"].get("currency") or dataset["meta"].get("synthetic")
            or dataset["meta"].get("adjustment") == "total_return"):
        raise ValueError("Source parity requires monthly next_open/2to4, positive base and un-reinvested prices.")
    config = dict(config, initialCapital=0, funding="periodic", fee=0, slippageBps=0, quantityStep=.000001,
        frequency="monthly")
    periods = reference_periods(signals, config)
    if not periods:
        raise ValueError("No calculable confirmed periods in the requested window.")
    rows = _daily_window(dataset, config["startDate"], config["endDate"])
    sessions = {bar["date"]: bar for bar in rows}
    invested = 0.
    for point in periods:
        if point["multiplier"] >= 2:
            invested += config["monthlyAmount"] * point["multiplier"]
    if not math.isfinite(invested):
        raise ValueError("The contribution budget is nonfinite.")
    even, dca_quantity = invested / len(periods), 0.
    dca_rows, original_values, dca_quantities = [], {}, {}
    for point in periods:
        if even > 0:
            units = even / point["close"]
            dca_quantity += units
            dca_rows.append({"date": point["availableDate"], "account": "dca", "action": "buy",
                "amount": even, "quantity": units, "price": point["close"], "fee": 0, "cash": 0})
            dca_quantities[point["availableDate"]] = units
        original_values[point["availableDate"]] = dca_quantity * point["close"]
    last = periods[-1]
    quotes = [bar for bar in rows if bar["date"] >= last["availableDate"]]
    valuation_close = quotes[-1]["close"] if quotes else last["close"]
    if quotes and quotes[-1]["date"] > last["availableDate"]:
        original_values[quotes[-1]["date"]] = dca_quantity * valuation_close
    dca = {"cash": 0, "quantity": dca_quantity, "spent": invested, "fees": 0,
        "wealth": dca_quantity * valuation_close, "gain": dca_quantity * valuation_close - invested,
        "average": invested / dca_quantity if dca_quantity > 0 else None}
    scheduled = {}
    for point in periods:
        if point["multiplier"] >= 2:
            following = next_signal_session(dataset, point, config["endDate"])
            if following:
                scheduled.setdefault(following, []).append((point, config["monthlyAmount"] * point["multiplier"]))
    closes = {point["availableDate"]: point for point in periods}
    quantity = cash = spent = allocated = held_dca = 0.
    close = rows[0]["open"]
    ledger, equity = [], []
    for day in sorted(set(sessions) | set(closes) | set(original_values)):
        candle = sessions.get(day)
        for point, amount in scheduled.get(day, []):
            if candle is None or point["availableDate"] >= day or amount > cash + 1e-7:
                raise ValueError("The signal budget was not available before its first opening.")
            units = amount / candle["open"]
            quantity += units
            spent += amount
            cash -= amount
            if abs(cash) < 1e-8:
                cash = 0.
            ledger.append({"date": day, "account": "signal", "action": "buy", "amount": amount,
                "quantity": units, "price": candle["open"], "fee": 0, "cash": cash,
                "category": point["category"], "signalDate": point["date"]})
        point = closes.get(day)
        if point and point["multiplier"] >= 2:
            amount = config["monthlyAmount"] * point["multiplier"]
            cash += amount
            allocated += amount
        held_dca += dca_quantities.get(day, 0.)
        if candle:
            close = candle["close"]
        elif point:
            close = point["close"]
        equity.append({"date": day, "dca": original_values.get(day, held_dca * close),
            "signal": quantity * close + cash, "contributions": allocated})
    wealth = quantity * close + cash
    if not all(math.isfinite(value) for value in (wealth, quantity, spent, cash, dca["wealth"])):
        raise ValueError("The result exceeds finite precision.")
    return {"method": "next_open_v1", "executionVersion": NEXT_OPEN_EXECUTION_VERSION,
        "config": config, "datasetHash": dataset["hash"], "engineVersion": ENGINE_VERSION,
        "dca": dca, "signal": {"cash": cash, "quantity": quantity, "spent": spent, "fees": 0,
            "wealth": wealth, "gain": wealth - invested, "average": spent / quantity if quantity > 0 else None},
        "contributions": invested, "difference": wealth - dca["wealth"],
        "ledger": sorted(dca_rows + ledger, key=lambda row: row["date"]), "equity": equity,
        "limitations": ["Pinned v50 buy-only execution; zero costs/dividends and exact float quantities.",
            "The equal-capital C/N close comparator is retrospective, not a causal contribution policy."]}


def _floor8(quantity):
    return Fraction((quantity * 10**8) // 1, 10**8)


def simulate_funded_trim25(prices, signals_by_symbol, sessions, start, end, dividends=None, *,
                          fee_bps=5, slippage_bps=5, trim25=True):
    """Named funded adaptation: five independent20k buckets, B20k/12 and entry-only quarter sales.

    The complete warmup signals seed state; only retained test-period signals create
    orders. Money, eight-decimal quantities and proportional basis use Fractions.
    """
    start, end = _iso(start), _iso(end)
    if start >= end or type(trim25) is not bool:
        raise ValueError("A chronological window and boolean trim25 policy are required.")
    fee_rate, slippage = _execution_costs(fee_bps, slippage_bps)
    _validate_input(prices)
    symbols = sorted(prices.symbol.unique())
    if (len(symbols) != 5 or not isinstance(signals_by_symbol, dict) or set(signals_by_symbol) != set(symbols)
            or not isinstance(sessions, list) or not sessions or any(_iso(day) != day for day in sessions)
            or sessions != sorted(set(sessions)) or sessions[0] > start or sessions[-1] < end):
        raise ValueError("Five signal/price buckets and a complete declared session calendar are required.")
    expected = [day for day in sessions if start < day <= end]
    if not expected:
        raise ValueError("No declared session in the account window.")
    market = prices.loc[prices.date.gt(pd.Timestamp(start)) & prices.date.le(pd.Timestamp(end))]
    if any(group.date.dt.strftime("%Y-%m-%d").tolist() != expected
           for _, group in market.groupby("symbol", sort=True)) or market.symbol.nunique() != 5:
        raise ValueError("Every stock must cover the exact declared session calendar; missing openings cannot be skipped.")
    cash_events = _dividend_events(dividends, prices, start, end)
    base, events = Fraction(20000, 12), []
    event_columns = ["event_id", "signal_date", "available_date", "known_date", "execution_date", "symbol",
        "action", "category", "multiplier", "requested_budget"]
    for symbol, group in prices.groupby("symbol", sort=True):
        history = signals_by_symbol[symbol]
        if not isinstance(history, list) or not history:
            raise ValueError("Each stock needs its full warmup signal history.")
        periods = reference_periods(history, {"startDate": min(_iso(point["date"]) for point in history), "endDate": end})
        records = group[["date", "open", "high", "low", "close"]].copy()
        records["date"] = records.date.dt.strftime("%Y-%m-%d")
        dataset = {"candles": records.to_dict("records"), "meta": {"calendarMode": "declared_sessions",
            "sessions": sessions, "calendarThrough": sessions[-1], "closedThrough": str(group.date.max().date())}}
        previous = None
        for point in periods:
            if point.get("profile") != 24:
                raise ValueError("The funded comparison requires the fixed monthly Standard24 profile.")
            known = max(point["availableDate"], point["periodEnd"])
            category = point["category"]
            if point["date"] < start:
                if known < start:
                    previous = category
                continue
            action = ("BUY" if point["multiplier"] >= 2 else "SELL" if trim25 and category == "STRONG_TRIM"
                and previous is not None and previous != "STRONG_TRIM" else None)
            previous = category
            if action is None:
                continue
            following = next_signal_session(dataset, point, end)
            events.append({"event_id": f"{symbol}|{point['periodEnd']}|{action}", "signal_date": point["date"],
                "available_date": point["availableDate"], "known_date": known, "execution_date": following,
                "symbol": symbol, "action": action, "category": category, "multiplier": point["multiplier"],
                "requested_budget": float(base * point["multiplier"]) if action == "BUY" else 0.,
                "status": "scheduled" if following else "pending_after_end"})
    if len({event["event_id"] for event in events}) != len(events):
        raise ValueError("Every monthly execution event must have a unique identity.")
    events.sort(key=lambda event: (event["execution_date"] or "9999-12-31", event["action"] != "SELL",
        event["available_date"], event["signal_date"], event["symbol"], event["event_id"]))
    scheduled = {}
    for event in events:
        if event["execution_date"]:
            scheduled.setdefault(pd.Timestamp(event["execution_date"]), []).append(event)
    states = {symbol: {"cash": Fraction(20000), "quantity": Fraction(0), "cost_basis": Fraction(0),
        "dividends_earned": Fraction(0), "dividends_paid": Fraction(0), "realized_pnl": Fraction(0),
        "fees": Fraction(0), "pending": []} for symbol in symbols}
    curve, buckets, trades, processed = [], [], [], set()

    def record(day, closes):
        totals = {name: Fraction(0) for name in ("cash", "total_value", "dividends_receivable",
            "dividends_earned", "dividends_paid", "cost_basis", "realized_pnl", "unrealized_pnl", "fees")}
        for symbol, state in states.items():
            value = state["quantity"] * closes.get(symbol, Fraction(0))
            receivable = state["dividends_earned"] - state["dividends_paid"]
            if receivable != sum((amount for _, amount in state["pending"]), Fraction(0)):
                raise ValueError("Dividend receivables do not reconcile.")
            total = state["cash"] + receivable + value
            if state["cash"] < 0 or state["quantity"] < 0 or total <= 0 or not math.isfinite(float(total)):
                raise ValueError("Invalid funded cash, position or equity.")
            row = {"date": day, "symbol": symbol, "cash": state["cash"], "quantity": state["quantity"],
                "cost_basis": state["cost_basis"], "market_value": value, "dividends_receivable": receivable,
                "dividends_earned": state["dividends_earned"], "dividends_paid": state["dividends_paid"],
                "realized_pnl": state["realized_pnl"], "unrealized_pnl": value - state["cost_basis"],
                "fees": state["fees"], "total_value": total}
            buckets.append({key: float(value) if isinstance(value, Fraction) else value for key, value in row.items()})
            for name in totals:
                totals[name] += row[name]
        curve.append({"date": day} | {name: float(value) for name, value in totals.items()})

    record(pd.Timestamp(start), {})
    for day, bars in market.groupby("date", sort=True):
        for state in states.values():
            pending = []
            for pay_date, amount in state["pending"]:
                if pay_date < day:
                    state["cash"] += amount
                    state["dividends_paid"] += amount
                else:
                    pending.append((pay_date, amount))
            state["pending"] = pending
        # Dividend entitlement precedes every purchase and sale at today's open.
        for dividend in cash_events.get(day, []):
            state = states[dividend.symbol]
            amount = state["quantity"] * Fraction(str(dividend.amount))
            if amount:
                state["pending"].append((dividend.pay_date, amount))
                state["dividends_earned"] += amount
        opens = {bar.symbol: Fraction(str(bar.open)) for bar in bars.itertuples(index=False)}
        for event in scheduled.get(day, []):
            if event["event_id"] in processed or pd.Timestamp(event["known_date"]) >= day:
                raise ValueError("An execution event is duplicate or not yet known.")
            processed.add(event["event_id"])
            state, opening = states[event["symbol"]], opens[event["symbol"]]
            old, basis_before = state["quantity"], state["cost_basis"]
            released = realized = Fraction(0)
            if event["action"] == "SELL":
                quantity = _floor8(old / 4)
                if not quantity:
                    event["status"] = "skipped_no_position" if not old else "skipped_below_quantity"
                    continue
                fill = opening * (1 - slippage)
                fee = quantity * fill * fee_rate
                all_in = quantity * fill - fee
                released = basis_before * quantity / old
                realized = all_in - released
                state["quantity"] -= quantity
                state["cost_basis"] -= released
                state["cash"] += all_in
                state["realized_pnl"] += realized
            else:
                fill = opening * (1 + slippage)
                unit = fill * (1 + fee_rate)
                budget = min(base * event["multiplier"], state["cash"])
                quantity = _floor8(budget / unit)
                if not quantity:
                    event["status"] = "skipped_no_cash" if not state["cash"] else "skipped_below_quantity"
                    continue
                fee = quantity * fill * fee_rate
                all_in = quantity * unit
                state["quantity"] += quantity
                state["cost_basis"] += all_in
                state["cash"] -= all_in
            state["fees"] += fee
            event["status"] = "filled"
            numeric = {"quantity": quantity, "price": fill, "fee": fee, "all_in": all_in,
                "cash_after": state["cash"], "quantity_before": old, "quantity_after": state["quantity"],
                "cost_basis_before": basis_before, "cost_basis_after": state["cost_basis"],
                "released_basis": released, "realized_pnl": realized}
            trades.append({name: event[name] for name in event_columns}
                | {name: float(value) for name, value in numeric.items()})
        closes = {bar.symbol: Fraction(str(bar.close)) for bar in bars.itertuples(index=False)}
        record(day, closes)
    trade_columns = event_columns + ["quantity", "price", "fee", "all_in", "cash_after", "quantity_before",
        "quantity_after", "cost_basis_before", "cost_basis_after", "released_basis", "realized_pnl"]
    orders, executions = pd.DataFrame(events, columns=event_columns + ["status"]), pd.DataFrame(trades, columns=trade_columns)
    for table in (orders, executions):
        for name in ("signal_date", "available_date", "known_date", "execution_date"):
            table[name] = pd.to_datetime(table[name]).astype("datetime64[ns]")
    bucket_frame = pd.DataFrame(buckets)
    return {"parameters": {"method": "funded_next_open_trim25_entry_v1" if trim25 else "funded_next_open_buy_only_v1",
            "execution_version": NEXT_OPEN_EXECUTION_VERSION, "engine_version": ENGINE_VERSION,
            "initial_capital": 100000, "bucket_capital": 20000, "monthly_base": float(base),
            "external_contributions": 100000, "fee_bps": fee_bps, "slippage_bps": slippage_bps,
            "quantity_decimals": 8, "trim25": trim25, "profile": 24, "strategy": "2to4"},
        "equity": pd.DataFrame(curve), "trades": executions, "orders": orders,
        "positions": bucket_frame[["date", "symbol", "quantity", "cost_basis", "market_value"]].copy(),
        "buckets": bucket_frame}
