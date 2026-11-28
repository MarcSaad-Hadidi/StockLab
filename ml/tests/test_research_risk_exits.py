"""Protective exits use observed closes, actual next opens and real cash events."""

import pandas as pd
import pytest

from stocklab_ml import research


def prices(count=40):
    return pd.DataFrame({"date": pd.bdate_range("2025-01-01", periods=count).astype("datetime64[ns]"),
        "symbol": pd.Series(["AAPL"] * count, dtype="string"), "open": 100.,
        "high": 101., "low": 99., "close": 100., "volume": pd.Series([1000] * count, dtype="Int64")})


def orders(market, days=None, actions=None):
    days = market.date if days is None else days
    return pd.DataFrame({"date": days, "symbol": "AAPL", "action": "BUY" if actions is None else actions,
                         "allocation": 1.}).reset_index(drop=True)


def run(market, decisions, **kwargs):
    return research.simulate(market, decisions, str(market.date.iloc[0].date()), str(market.date.iloc[-1].date()),
        fee_bps=0, slippage_bps=0, risk_policy="trailing15", **kwargs)


def set_price(market, row, opening, close):
    market.loc[row, ["open", "high", "low", "close"]] = [opening, max(opening, close) + 1, min(opening, close) - 1, close]


def test_stop_is_close_confirmed_and_fills_at_actual_next_open_before_same_symbol_buy():
    market = prices(6)
    market.loc[1, "low"] = 70.  # Intraday breach alone must not sell.
    set_price(market, 2, 99., 84.)
    set_price(market, 3, 70., 72.)
    _, trades = run(market, orders(market, market.date.iloc[[0, 2]]))
    assert trades.action.tolist() == ["BUY", "SELL"]
    assert trades.reason.tolist() == ["decision", "trailing_stop"]
    assert trades.execution_date.tolist() == market.date.iloc[[1, 3]].tolist()
    assert trades.decision_date.iloc[-1] == market.date.iloc[2]
    assert trades.price.iloc[-1] == 70.


def test_entry_day_loss_is_protected_and_final_close_is_not_filled():
    market = prices(3)
    set_price(market, 1, 100., 84.)
    _, trades = run(market, orders(market, market.date.iloc[:1]))
    assert trades.action.tolist() == ["BUY", "SELL"]
    assert trades.execution_date.iloc[-1] == market.date.iloc[2]
    market = prices(2)
    set_price(market, 1, 100., 84.)
    _, trades = run(market, orders(market, market.date.iloc[:1]))
    assert trades.action.tolist() == ["BUY"]


def test_twenty_observed_open_cooldown_and_fresh_entry_resets_peak():
    market = prices()
    set_price(market, 2, 100., 84.)
    for i in range(3, len(market)):
        set_price(market, i, 60., 60.)
    curve, trades = run(market, orders(market))
    sold = trades.loc[trades.action.eq("SELL")]
    assert sold.execution_date.tolist() == [market.date.iloc[3]]
    later = trades.loc[trades.action.eq("BUY") & trades.execution_date.gt(market.date.iloc[3])]
    assert later.execution_date.iloc[0] == market.date.iloc[24]
    assert curve.cash.ge(0).all()


def test_model_sale_does_not_start_risk_cooldown():
    market = prices(5)
    _, trades = run(market, orders(market, market.date.iloc[:3], ["BUY", "SELL", "BUY"]))
    assert trades.action.tolist() == ["BUY", "SELL", "BUY"]
    assert trades.reason.eq("decision").all()


def test_adding_to_a_position_does_not_reset_its_trailing_peak():
    market = prices(5)
    set_price(market, 2, 90., 90.)
    set_price(market, 3, 90., 84.)
    _, trades = run(market, orders(market, market.date.iloc[[0, 1]]))
    assert trades.action.tolist() == ["BUY", "BUY", "SELL"]
    assert trades.execution_date.iloc[-1] == market.date.iloc[4]


def test_risk_exit_keeps_execution_fees_and_adverse_slippage():
    market = prices(4)
    set_price(market, 2, 100., 84.)
    curve, trades = research.simulate(market, orders(market, market.date.iloc[:1]),
        str(market.date.iloc[0].date()), str(market.date.iloc[-1].date()),
        fee_bps=25, slippage_bps=25, risk_policy="trailing15")
    sale = trades.iloc[-1]
    assert sale.reason == "trailing_stop"
    assert sale.price == 99.75
    assert sale.fee == pytest.approx(sale.quantity * sale.price * .0025)
    assert curve.cash.iloc[-1] == pytest.approx(100000 - trades.all_in.iloc[0] + sale.all_in)


def test_dividend_detachment_is_not_a_trailing_loss():
    market = prices(6)
    for i in range(2, len(market)):
        set_price(market, i, 80., 80.)
    events = pd.DataFrame({"symbol": pd.Series(["AAPL"], dtype="string"),
        "ex_date": market.date.iloc[[2]].reset_index(drop=True),
        "pay_date": market.date.iloc[[4]].reset_index(drop=True), "amount": [20.]})
    curve, trades = run(market, orders(market, market.date.iloc[:1]), dividends=events)
    assert trades.action.tolist() == ["BUY"]
    assert curve.total_value.eq(100000).all()


def test_risk_seller_on_ex_date_keeps_dividend_receivable():
    market = prices(7)
    set_price(market, 2, 100., 84.)
    set_price(market, 3, 70., 70.)
    events = pd.DataFrame({"symbol": pd.Series(["AAPL"], dtype="string"),
        "ex_date": market.date.iloc[[3]].reset_index(drop=True),
        "pay_date": market.date.iloc[[5]].reset_index(drop=True), "amount": [1.]})
    curve, trades = run(market, orders(market, market.date.iloc[:1]), dividends=events)
    curve = curve.set_index("date")
    assert trades.reason.tolist() == ["decision", "trailing_stop"]
    assert curve.loc[market.date.iloc[3], "dividends_receivable"] == 100
    assert curve.loc[market.date.iloc[5], "dividends_paid"] == 0
    assert curve.loc[market.date.iloc[6], "dividends_paid"] == 100


def regime_context():
    spy = prices(270)
    spy["symbol"] = pd.Series(["SPY"] * len(spy), dtype="string")
    market = prices(270)
    for i in range(222, 226):
        set_price(spy, i, 90., 90.)
    for i in range(226, len(spy)):
        set_price(spy, i, 110., 110.)
    return market, spy


def test_three_confirmed_spy_closes_apply_at_next_open_and_future_data_cannot_change_past():
    market, spy = regime_context()
    active = market.iloc[220:].reset_index(drop=True)
    kwargs = dict(fee_bps=0, slippage_bps=0, risk_policy="trailing15_spy200", spy=spy)
    curve, trades = research.simulate(market, orders(active), str(active.date.iloc[0].date()),
                                     str(active.date.iloc[-1].date()), **kwargs)
    sales = trades.loc[trades.action.eq("SELL")]
    assert sales.reason.tolist() == ["spy_regime"]
    assert sales.execution_date.tolist() == [active.date.iloc[5]]
    later = trades.loc[trades.action.eq("BUY") & trades.execution_date.gt(active.date.iloc[5])]
    assert later.execution_date.iloc[0] == active.date.iloc[26]
    changed = spy.copy()
    for i in range(250, len(changed)):
        set_price(changed, i, 10., 10.)
    kwargs["spy"] = changed
    other_curve, other_trades = research.simulate(market, orders(active), str(active.date.iloc[0].date()),
                                                str(active.date.iloc[-1].date()), **kwargs)
    pd.testing.assert_frame_equal(curve.loc[curve.date < spy.date.iloc[250]],
                                  other_curve.loc[other_curve.date < spy.date.iloc[250]])
    pd.testing.assert_frame_equal(trades.loc[trades.execution_date <= spy.date.iloc[250]],
                                  other_trades.loc[other_trades.execution_date <= spy.date.iloc[250]])


def test_regime_exit_has_priority_over_same_open_stop_and_model_sale():
    market, spy = regime_context()
    active = market.iloc[220:].reset_index(drop=True)
    set_price(market, 224, 100., 84.)
    decisions = orders(active, active.date.iloc[[0, 4]], ["BUY", "SELL"])
    _, trades = research.simulate(market, decisions, str(active.date.iloc[0].date()),
        str(active.date.iloc[-1].date()), risk_policy="trailing15_spy200", spy=spy)
    assert trades.reason.tolist() == ["decision", "spy_regime"]
    assert trades.execution_date.iloc[-1] == active.date.iloc[5]


def test_equal_spy_close_resets_confirmation_streak():
    market, spy = regime_context()
    for i in range(220, len(spy)):
        set_price(spy, i, 100., 100.)
    # Two down closes, an exact rolling-mean close, then three down closes.
    for i in (222, 223, 225, 226, 227):
        set_price(spy, i, 90., 90.)
    mean = (198 * 100 + 2 * 90) / 199
    set_price(spy, 224, mean, mean)
    states = research._spy_risk_states(spy, market, str(market.date.iloc[220].date()),
                                      str(market.date.iloc[-1].date()))
    assert not any(states[spy.date.iloc[i]] for i in range(224, 228))
    assert states[spy.date.iloc[228]]


@pytest.mark.parametrize("bad", ["policy", "missing", "short", "calendar", "symbols"])
def test_risk_context_rejects_unknown_policy_or_incomplete_spy(bad):
    market, spy = regime_context()
    active = market.iloc[220:].reset_index(drop=True)
    kwargs = {"risk_policy": "trailing15_spy200", "spy": spy}
    if bad == "policy":
        kwargs["risk_policy"] = "best_returns"
    elif bad == "missing":
        kwargs["spy"] = None
    elif bad == "short":
        kwargs["spy"] = spy.iloc[220:].reset_index(drop=True)
    elif bad == "calendar":
        kwargs["spy"] = spy.drop(index=225).reset_index(drop=True)
    else:
        kwargs["spy"] = spy.assign(symbol=pd.Series(["QQQ"] * len(spy), dtype="string"))
    with pytest.raises(ValueError):
        research.simulate(market, orders(active), str(active.date.iloc[0].date()),
                          str(active.date.iloc[-1].date()), **kwargs)


def test_none_policy_preserves_default_curves_and_schema():
    market = prices()
    decisions = orders(market)
    default = research.simulate(market, decisions, "2025-01-01", "2025-02-25")
    explicit = research.simulate(market, decisions, "2025-01-01", "2025-02-25", risk_policy=None)
    for left, right in zip(default, explicit):
        pd.testing.assert_frame_equal(left, right)
    assert "reason" not in default[1]
