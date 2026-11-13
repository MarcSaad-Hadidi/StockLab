"""Pinned next-open arithmetic and the separately funded trim-entry variant."""

from copy import deepcopy
from fractions import Fraction

import pandas as pd
import pytest

from stocklab_ml import dca_next_open as dca


SYMBOLS = ["AAPL", "JNJ", "JPM", "MSFT", "XOM"]
MULTIPLIERS = {"ADD": 1, "BUY": 2, "STRONG_BUY": 3, "EXTREME_BUY": 4,
    "LIGHT_TRIM": 0, "STRONG_TRIM": 0, "UNAVAILABLE": 0}


def point(month, category="BUY", **changes):
    end = str(pd.Period(month, freq="M").end_time.date())
    result = {"date": month + "-01", "periodEnd": end, "availableDate": end, "confirmed": True,
        "open": 10., "high": 11., "low": 9., "close": 10., "volume": 100,
        "category": category, "multiplier": MULTIPLIERS[category], "rsi": 50.,
        "score": None if category == "UNAVAILABLE" else 10., "profile": 24, "lookback": 12,
        "engineVersion": "range_rsi_v1", "touchCategory": None, "touchMultiplier": 0}
    return result | changes


def fixture():
    bars = [("2026-01-02", 10, 10), ("2026-01-30", 10, 10), ("2026-02-02", 20, 25),
        ("2026-02-27", 20, 20), ("2026-03-02", 30, 35), ("2026-03-03", 40, 40), ("2026-03-31", 30, 30)]
    candles = [{"date": day, "open": opening, "close": close, "high": max(opening, close) + 1,
        "low": min(opening, close) - 1, "volume": 100} for day, opening, close in bars]
    dataset = {"id": "fixture", "hash": "fixture", "candles": candles, "meta": {"symbol": "FIXTURE",
        "assetClass": "other", "currency": "USD", "exchange": "TEST", "adjustment": "raw",
        "calendarMode": "declared_sessions", "sessions": [bar[0] for bar in bars],
        "calendarThrough": "2026-03-31", "closedThrough": "2026-03-31", "synthetic": False}}
    config = {"method": "next_open", "frequency": "monthly", "startDate": "2026-01-01",
        "endDate": "2026-03-31", "monthlyAmount": 100, "strategy": "2to4", "currency": "USD"}
    return dataset, [point("2026-01"), point("2026-02", "STRONG_BUY", close=20)], config


def test_v50_exact_next_open_fixture_and_retrospective_reference():
    dataset, signals, config = fixture()
    result = dca.run_next_open(dataset, signals, config | {"initialCapital": 9999, "fee": 5, "slippageBps": 50})
    assert result["method"] == "next_open_v1" and result["executionVersion"] == "next_confirmed_session_open_v1"
    buys = [row for row in result["ledger"] if row["account"] == "signal"]
    assert [(row["date"], row["amount"], row["price"], row["quantity"]) for row in buys] == [
        ("2026-02-02", 200, 20, 10), ("2026-03-02", 300, 30, 10)]
    assert result["signal"] == {"cash": 0, "quantity": 20, "spent": 500, "fees": 0,
        "wealth": 600, "gain": 100, "average": 25}
    assert result["dca"]["quantity"] == 37.5 and result["dca"]["wealth"] == 1125
    assert result["contributions"] == 500 and result["difference"] == -525
    assert result["config"]["initialCapital"] == result["config"]["fee"] == result["config"]["slippageBps"] == 0
    assert result["config"]["quantityStep"] == .000001


def test_last_confirmed_budget_stays_cash_without_forced_fill():
    dataset, signals, config = fixture()
    result = dca.run_next_open(dataset, signals, config | {"endDate": "2026-02-28"})
    assert result["contributions"] == 500
    assert result["signal"]["spent"] == 200 and result["signal"]["cash"] == 300
    assert result["signal"]["wealth"] == 500


@pytest.mark.parametrize("defect", ["missing", "cutoff", "excluded"])
def test_first_expected_open_is_never_replaced_by_later_observed_open(defect):
    dataset, signals, config = fixture()
    if defect == "missing":
        dataset["candles"] = [bar for bar in dataset["candles"] if bar["date"] != "2026-03-02"]
    elif defect == "cutoff":
        dataset["meta"]["closedThrough"] = "2026-03-01"
    else:
        dataset["excludedDailyPeriods"] = ["2026-03-02"]
    with pytest.raises(ValueError, match="2026-03-02"):
        dca.run_next_open(dataset, signals, config)


def test_delayed_confirmations_preserve_multiple_orders_and_exact_float_fractions():
    dataset, signals, config = fixture()
    signals[0]["availableDate"] = "2026-03-01"
    result = dca.run_next_open(dataset, signals, config)
    buys = [row for row in result["ledger"] if row["account"] == "signal"]
    assert [(row["date"], row["amount"]) for row in buys] == [("2026-03-02", 300), ("2026-03-02", 200)]
    assert buys[1]["quantity"] == 200 / 30
    signals[1]["availableDate"] = "2026-03-02"
    result = dca.run_next_open(dataset, signals, config)
    assert [row["date"] for row in result["ledger"] if row["account"] == "signal"] == ["2026-03-02", "2026-03-03"]


def test_native_period_start_filter_no_one_x_no_sell_and_trim_in_reference_denominator():
    dataset, signals, config = fixture()
    signals += [point("2026-03", "STRONG_TRIM", close=30)]
    result = dca.run_next_open(dataset, signals, config)
    assert len([row for row in result["ledger"] if row["account"] == "dca"]) == 3
    assert all(row["action"] == "buy" for row in result["ledger"])
    result = dca.run_next_open(dataset, [point("2026-01", "ADD"), point("2026-02", "STRONG_TRIM")], config)
    assert result["contributions"] == result["signal"]["wealth"] == 0 and result["signal"]["average"] is None
    result = dca.run_next_open(dataset, signals, config | {"startDate": "2026-01-15"})
    assert [row["signalDate"] for row in result["ledger"] if row["account"] == "signal"] == ["2026-02-01"]


@pytest.mark.parametrize("defect", ["availability", "month", "engine", "category", "frequency", "total_return"])
def test_source_execution_rejects_incoherent_contract(defect):
    dataset, signals, config = fixture()
    if defect == "availability":
        signals[1]["availableDate"] = signals[0]["availableDate"]
    elif defect == "month":
        signals[1]["periodEnd"] = "2026-02-27"
    elif defect == "engine":
        signals[0]["engineVersion"] = "other"
    elif defect == "category":
        signals[0]["multiplier"] = 4
    elif defect == "frequency":
        config["frequency"] = "daily"
    else:
        dataset["meta"]["adjustment"] = "total_return"
    with pytest.raises(ValueError):
        dca.run_next_open(dataset, signals, config)


def test_v50_us_fallback_holiday_and_declared_calendar_coverage():
    dataset, signals, _ = fixture()
    dataset["meta"].update(assetClass="stock", exchange="NYSE", calendarMode="unknown",
        sessions=[], closedThrough="2027-01-04", calendarThrough="2027-01-04")
    dataset["candles"] = [{"date": "2027-01-04", "open": 25, "close": 25, "high": 26, "low": 24}]
    assert dca.next_signal_session(dataset, point("2026-12"), "2027-01-04") == "2027-01-04"
    dataset["candles"] = [{"date": "2025-01-10", "open": 25, "close": 25, "high": 26, "low": 24}]
    assert dca.next_signal_session(dataset, point("2024-12", availableDate="2025-01-08"), "2025-01-10") == "2025-01-10"
    dataset["meta"].update(calendarMode="declared_sessions", sessions=[], calendarThrough="2026-01-31")
    with pytest.raises(ValueError, match="calendar"):
        dca.next_signal_session(dataset, signals[0], "2026-03-31")


def funded_fixture(categories, *, start="2018-01-01", end="2018-10-31"):
    dates = pd.bdate_range("2017-11-01", "2018-11-02").astype("datetime64[ns]")
    prices = pd.DataFrame([(day, symbol, 20., 21., 19., 20., 100.) for day in dates for symbol in SYMBOLS],
        columns=["date", "symbol", "open", "high", "low", "close", "volume"])
    prices = prices.astype({"symbol": "string", "volume": "Int64"}).sort_values(["symbol", "date"]).reset_index(drop=True)
    signals = {symbol: [point("2017-11", "ADD"), point("2018-01", "ADD")] for symbol in SYMBOLS}
    signals["AAPL"] = [point(month, category) if isinstance(category, str) else point(month, **category)
        for month, category in categories]
    return prices, signals, dates.strftime("%Y-%m-%d").tolist(), start, end


def test_funded_monthly_allin_budgets_are_cash_limited_with_five_independent_buckets():
    args = funded_fixture([(f"2018-{month:02}", "EXTREME_BUY") for month in range(1, 7)])
    result = dca.simulate_funded_trim25(*args, fee_bps=0, slippage_bps=0, trim25=False)
    trades = result["trades"]
    assert trades.execution_date.min() == pd.Timestamp("2018-02-01")
    assert trades.symbol.eq("AAPL").all() and trades.action.eq("BUY").all()
    assert trades.all_in.sum() <= 20000
    assert result["buckets"].loc[lambda x: x.symbol.ne("AAPL"), "cash"].eq(20000).all()
    assert result["equity"].total_value.eq(100000).all()
    assert result["orders"].event_id.is_unique
    assert result["parameters"]["monthly_base"] == 20000 / 12


def test_trim_state_uses_warmup_valid_exits_and_survives_unavailable_and_missing_months():
    args = funded_fixture([("2017-11", "STRONG_TRIM"), ("2018-01", "STRONG_TRIM"),
        ("2018-02", "BUY"), ("2018-03", "STRONG_TRIM"), ("2018-04", "STRONG_TRIM"),
        ("2018-05", "UNAVAILABLE"), ("2018-07", "STRONG_TRIM"),
        ("2018-08", "LIGHT_TRIM"), ("2018-09", "STRONG_TRIM")])
    result = dca.simulate_funded_trim25(*args, fee_bps=0, slippage_bps=0)
    trades = result["trades"]
    sells = trades.loc[trades.action.eq("SELL")]
    assert sells.execution_date.tolist() == [pd.Timestamp("2018-04-02"), pd.Timestamp("2018-10-01")]
    assert sells.quantity.tolist() == [41.66666666, 31.25]
    assert trades.loc[trades.action.eq("BUY"), "execution_date"].tolist() == [pd.Timestamp("2018-03-01")]
    again = dca.simulate_funded_trim25(*args, fee_bps=0, slippage_bps=0)
    pd.testing.assert_frame_equal(result["trades"], again["trades"])
    assert trades.event_id.is_unique


def test_trim_first_unknown_is_not_entry_but_known_nontrim_to_trim_without_position_is_logged():
    args = funded_fixture([("2018-01", "STRONG_TRIM"), ("2018-02", "STRONG_TRIM")])
    assert dca.simulate_funded_trim25(*args)["orders"].empty
    args = funded_fixture([("2017-11", "ADD"), ("2018-01", "STRONG_TRIM")])
    result = dca.simulate_funded_trim25(*args)
    assert result["trades"].empty
    assert result["orders"].status.tolist() == ["skipped_no_position"]


def test_costs_are_inside_budget_sales_release_proportional_basis_and_proceeds_remain_internal():
    args = funded_fixture([("2017-11", "ADD"), ("2018-01", "BUY"),
        ("2018-02", "STRONG_TRIM"), ("2018-03", "BUY")])
    result = dca.simulate_funded_trim25(*args)
    buy, sell, later_buy = result["trades"].itertuples(index=False)
    unit = Fraction(20) * Fraction(10005, 10000) ** 2
    expected = Fraction((Fraction(10000, 3) / unit * 10**8) // 1, 10**8)
    assert buy.quantity == float(expected) and buy.all_in <= 20000 / 12 * 2
    assert sell.quantity == float(Fraction((expected / 4 * 10**8) // 1, 10**8))
    assert sell.released_basis == pytest.approx(buy.all_in * sell.quantity / buy.quantity)
    assert sell.realized_pnl == pytest.approx(sell.all_in - sell.released_basis)
    assert later_buy.cash_after == pytest.approx(20000 - buy.all_in + sell.all_in - later_buy.all_in)
    assert result["parameters"]["external_contributions"] == 100000


def test_same_open_executes_sell_before_buy_and_measures_quarter_of_pre_fill_holding():
    args = funded_fixture([("2017-11", "ADD"), ("2018-01", "BUY"),
        ("2018-02", {"category": "STRONG_TRIM", "availableDate": "2018-04-01"}), ("2018-03", "BUY")])
    result = dca.simulate_funded_trim25(*args, fee_bps=0, slippage_bps=0)
    same = result["trades"].loc[lambda x: x.execution_date.eq(pd.Timestamp("2018-04-02"))]
    assert same.action.tolist() == ["SELL", "BUY"] and same.quantity.iloc[0] == 41.66666666


def test_exdate_entitlement_precedes_same_open_sale_and_paydate_cash_is_delayed():
    args = funded_fixture([("2017-11", "ADD"), ("2018-01", "BUY"), ("2018-02", "STRONG_TRIM")])
    dividends = pd.DataFrame({"symbol": ["AAPL", "AAPL"],
        "ex_date": pd.to_datetime(["2018-02-01", "2018-03-01"]).astype("datetime64[ns]"),
        "pay_date": pd.to_datetime(["2018-02-05", "2018-03-01"]).astype("datetime64[ns]"), "amount": [1., 1.]})
    result = dca.simulate_funded_trim25(*args, dividends=dividends, fee_bps=0, slippage_bps=0)
    curve = result["equity"].set_index("date")
    assert curve.loc["2018-02-01", "dividends_earned"] == 0
    assert curve.loc["2018-03-01", "dividends_earned"] == 166.66666666
    assert curve.loc["2018-03-01", "dividends_paid"] == 0
    assert curve.loc["2018-03-02", "dividends_paid"] == 166.66666666
    assert curve.loc["2018-03-01", "total_value"] == pytest.approx(100166.66666666)


def test_unexecutable_end_trim_remains_pending_and_missing_expected_price_fails():
    args = funded_fixture([("2017-11", "ADD"), ("2018-01", "BUY"), ("2018-02", "STRONG_TRIM")], end="2018-02-28")
    result = dca.simulate_funded_trim25(*args, fee_bps=0, slippage_bps=0)
    assert result["orders"].status.tolist() == ["filled", "pending_after_end"]
    assert result["trades"].action.tolist() == ["BUY"]
    prices, signals, sessions, start, _ = args
    missing = prices.loc[~(prices.symbol.eq("AAPL") & prices.date.eq(pd.Timestamp("2018-02-01")))].reset_index(drop=True)
    with pytest.raises(ValueError, match="calendar|session|opening"):
        dca.simulate_funded_trim25(missing, signals, sessions, start, "2018-10-31")


def test_source_earlier_buys_stay_causal_while_its_c_over_n_reference_is_retrospective():
    dataset, signals, config = fixture()
    original = dca.run_next_open(dataset, signals, config)
    signals[1].update(category="EXTREME_BUY", multiplier=4)
    dataset["candles"][-1].update(open=1000, high=1001, low=999, close=1000)
    changed = dca.run_next_open(dataset, signals, config)
    earlier = lambda result: [row for row in result["ledger"] if row["account"] == "signal" and row["date"] < "2026-03-01"]
    assert earlier(original) == earlier(changed)
    assert original["ledger"][0]["amount"] != changed["ledger"][0]["amount"]


def test_funded_future_data_cannot_change_prior_orders_trades_or_equity_and_inputs_stay_unchanged():
    args = funded_fixture([("2017-11", "ADD"), ("2018-01", "BUY"),
        ("2018-02", "STRONG_TRIM"), ("2018-03", "BUY"), ("2018-05", "ADD")])
    prices, signals, sessions, start, end = args
    before_prices, before_signals = prices.copy(deep=True), deepcopy(signals)
    original = dca.simulate_funded_trim25(*args)
    pd.testing.assert_frame_equal(prices, before_prices)
    assert signals == before_signals
    changed_prices = prices.copy()
    changed_prices.loc[changed_prices.date.ge("2018-05-01"), ["open", "high", "low", "close"]] *= 3
    changed_signals = deepcopy(signals)
    changed_signals["AAPL"][-1].update(category="EXTREME_BUY", multiplier=4)
    changed = dca.simulate_funded_trim25(changed_prices, changed_signals, sessions, start, end)
    for name, column in (("equity", "date"), ("trades", "execution_date"), ("orders", "execution_date")):
        pd.testing.assert_frame_equal(original[name].loc[lambda x: x[column].lt("2018-05-01")].reset_index(drop=True),
            changed[name].loc[lambda x: x[column].lt("2018-05-01")].reset_index(drop=True))


def test_warmup_seeds_state_without_creating_january_buy_and_unavailable_keeps_nontrim_armed():
    args = funded_fixture([("2017-11", "ADD"), ("2017-12", "EXTREME_BUY"), ("2018-01", "ADD")])
    assert dca.simulate_funded_trim25(*args)["orders"].empty
    args = funded_fixture([("2017-11", "ADD"), ("2018-01", "UNAVAILABLE"), ("2018-02", "STRONG_TRIM")])
    result = dca.simulate_funded_trim25(*args)
    assert result["orders"].action.tolist() == ["SELL"]
    assert result["orders"].status.tolist() == ["skipped_no_position"]


@pytest.mark.parametrize("price,quantity", [(10**11, 3e-8), (10**15, 0.)])
def test_eight_decimal_floor_records_unrepresentable_buy_or_quarter_sale(price, quantity):
    prices, signals, sessions, start, end = funded_fixture([("2017-11", "ADD"),
        ("2018-01", "BUY"), ("2018-02", "STRONG_TRIM")])
    rows = prices.symbol.eq("AAPL")
    prices.loc[rows, ["open", "close"]] = float(price)
    prices.loc[rows, "high"], prices.loc[rows, "low"] = float(price + 1), float(price - 1)
    result = dca.simulate_funded_trim25(prices, signals, sessions, start, end, fee_bps=0, slippage_bps=0)
    assert result["trades"].quantity.tolist() == ([quantity] if quantity else [])
    assert result["orders"].status.tolist() == (["filled", "skipped_below_quantity"] if quantity else
        ["skipped_below_quantity", "skipped_no_position"])
    assert result["equity"].realized_pnl.eq(0).all()


@pytest.mark.parametrize("defect", ["symbol", "calendar_duplicate", "calendar_end", "profile", "trim_flag", "duplicate_signal"])
def test_funded_contract_rejects_ambiguous_population_calendar_or_signal_state(defect):
    prices, signals, sessions, start, end = funded_fixture([("2017-11", "ADD"), ("2018-01", "BUY")])
    options = {}
    if defect == "symbol":
        del signals["XOM"]
    elif defect == "calendar_duplicate":
        sessions.insert(0, sessions[0])
    elif defect == "calendar_end":
        sessions = [day for day in sessions if day < end]
    elif defect == "profile":
        signals["AAPL"][-1]["profile"] = 36
    elif defect == "trim_flag":
        options["trim25"] = 1
    else:
        signals["AAPL"].append(dict(signals["AAPL"][-1]))
    with pytest.raises(ValueError):
        dca.simulate_funded_trim25(prices, signals, sessions, start, end, **options)
