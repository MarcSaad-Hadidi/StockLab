"""Real cash-event accounting: entitlement, receivable and spendable cash are distinct."""

import hashlib
import importlib.util
import json
from pathlib import Path

import numpy as np
import pandas as pd
import pytest

from stocklab_ml import research


def prices(symbols=("AAPL",)):
    return pd.DataFrame([(day, symbol, 100., 101., 99., 100., 1000)
        for symbol in symbols for day in pd.bdate_range("2025-01-01", "2025-01-10")],
        columns=["date", "symbol", "open", "high", "low", "close", "volume"]).astype({
            "date": "datetime64[ns]", "symbol": "string", "volume": "Int64"})


def orders(days, actions, symbols=None):
    return pd.DataFrame({"date": pd.to_datetime(days).astype("datetime64[ns]"),
        "symbol": symbols if symbols is not None else "AAPL", "action": actions, "allocation": 1.})


def dividend(ex_date="2025-01-03", pay_date="2025-01-06"):
    return pd.DataFrame({"symbol": pd.Series(["AAPL"], dtype="string"),
        "ex_date": pd.to_datetime([ex_date]).astype("datetime64[ns]"),
        "pay_date": pd.to_datetime([pay_date]).astype("datetime64[ns]"), "amount": [1.]})


def test_ex_date_seller_keeps_receivable_but_cash_waits_until_after_payment():
    market = prices()
    market.loc[market.date >= "2025-01-03", ["open", "high", "low", "close"]] = [99., 100., 98., 99.]
    curve, trades = research.simulate(market, orders(["2025-01-01", "2025-01-02"], ["BUY", "SELL"]),
        "2025-01-01", "2025-01-10", fee_bps=0, slippage_bps=0, dividends=dividend())
    curve = curve.set_index("date")
    assert trades.quantity.tolist() == [100., 100.]
    assert curve.loc["2025-01-03", "cash"] == 99900
    assert curve.loc["2025-01-03", "dividends_receivable"] == 100
    assert curve.loc["2025-01-06", "dividends_paid"] == 0
    assert curve.loc["2025-01-07", "cash"] == 100000
    assert curve.loc["2025-01-07", "dividends_receivable"] == 0
    assert curve.loc["2025-01-10", "dividends_paid"] == 100
    assert curve.total_value.eq(100000).all()


def test_ex_date_buyer_has_no_right_to_an_earlier_holders_dividend():
    curve, _ = research.simulate(prices(), orders(["2025-01-02"], ["BUY"]),
        "2025-01-01", "2025-01-10", fee_bps=0, slippage_bps=0, dividends=dividend())
    assert curve.dividends_earned.eq(0).all()
    assert curve.dividends_paid.eq(0).all()


def test_unpaid_dividend_is_equity_and_cannot_fund_a_purchase():
    symbols = ("AAPL", "JNJ", "JPM", "MSFT", "XOM")
    rows = [(day, symbol) for day in ("2025-01-01", "2025-01-02") for symbol in symbols]
    rows += [(day, "AAPL") for day in ("2025-01-03", "2025-01-06", "2025-01-07", "2025-01-08")]
    signals = orders([day for day, _ in rows], ["BUY"] * len(rows), [symbol for _, symbol in rows])
    curve, trades = research.simulate(prices(symbols), signals, "2025-01-01", "2025-01-10",
        fee_bps=0, slippage_bps=0, dividends=dividend("2025-01-06", "2025-01-08"))
    assert trades.loc[trades.execution_date > "2025-01-03", "execution_date"].tolist() == [pd.Timestamp("2025-01-09")]
    assert curve.set_index("date").loc["2025-01-08", "cash"] == 0
    assert curve.set_index("date").loc["2025-01-08", "total_value"] == 100200
    assert curve.cash.ge(0).all()
    assert trades.all_in.iloc[-1] == 40


@pytest.mark.parametrize("bad", ["negative", "nonfinite", "duplicate_ex", "duplicate_pay", "before_ex", "calendar", "symbol"])
def test_invalid_cash_events_fail_before_simulation(bad):
    events = dividend()
    if bad == "negative":
        events.loc[0, "amount"] = -1
    elif bad == "nonfinite":
        events.loc[0, "amount"] = np.inf
    elif bad == "duplicate_ex":
        events = pd.concat([events, events], ignore_index=True)
    elif bad == "duplicate_pay":
        events = pd.concat([events, dividend("2025-01-02")], ignore_index=True)
    elif bad == "before_ex":
        events.loc[0, "pay_date"] = pd.Timestamp("2025-01-02")
    elif bad == "calendar":
        events.loc[0, "ex_date"] = pd.Timestamp("2025-01-04")
    else:
        events.loc[0, "symbol"] = "OTHER"
    with pytest.raises(ValueError):
        research.simulate(prices(), orders(["2025-01-01"], ["BUY"]),
                          "2025-01-01", "2025-01-10", dividends=events)


def test_omitted_dividends_preserve_previous_curve_schema():
    curve, _ = research.simulate(prices(), orders(["2025-01-01"], ["BUY"]), "2025-01-01", "2025-01-10")
    assert list(curve.columns) == ["date", "cash", "total_value"]


def test_dividend_snapshot_requires_complete_coverage_correct_basis_and_hash(tmp_path, monkeypatch):
    examples = Path(__file__).parents[1] / "examples"
    monkeypatch.syspath_prepend(str(examples))
    spec = importlib.util.spec_from_file_location("dividend_runner", examples / "dividend_replay.py")
    runner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runner)
    rows = []
    symbols = ["AAPL", "JNJ", "JPM", "MSFT", "XOM"]
    for symbol in symbols:
        for year in range(2018, 2027):
            for quarter in range(4 if year < 2026 or symbol == "JPM" else 3):
                ex = pd.Timestamp(year=year, month=quarter * 3 + 1, day=6)
                rows.append((symbol, ex, ex + pd.Timedelta(days=14), 1.))
    csv = tmp_path / "dividends.csv"
    pd.DataFrame(rows, columns=["symbol", "ex_date", "pay_date", "amount"]).to_csv(csv, index=False)
    metadata = {"schema_version": 1, "coverage_start": "2018-01-01", "coverage_end": "2026-11-11",
                "currency": "USD", "amount_basis": "current_split_adjusted", "symbols": symbols,
                "rows": 176, "files": {"dividends.csv": hashlib.sha256(csv.read_bytes()).hexdigest()}}
    manifest = tmp_path / "manifest.json"
    manifest.write_text(json.dumps(metadata))
    assert len(runner.load_dividends(tmp_path)) == 176
    metadata["amount_basis"] = "unadjusted"
    manifest.write_text(json.dumps(metadata))
    with pytest.raises(ValueError):
        runner.load_dividends(tmp_path)
    metadata["amount_basis"] = "current_split_adjusted"
    manifest.write_text(json.dumps(metadata))
    csv.write_text(csv.read_text().replace("1.0", "2.0"))
    with pytest.raises(ValueError):
        runner.load_dividends(tmp_path)
