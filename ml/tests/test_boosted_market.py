import numpy as np
import pandas as pd
from urllib.parse import parse_qs, urlsplit

from stocklab_ml.boosted_market import (BAR_COLUMNS, FEATURES, common_stock_listings, latest_candidates, scored_candidates,
                                         stock_features)
from stocklab_ml.data.twelve_data import fetch_us_stock_catalog


def listing(symbol, mic, kind="Common Stock", country="United States"):
    return {"symbol": symbol, "mic_code": mic, "exchange": "NYSE", "figi_code": None,
            "country": country, "currency": "USD", "type": kind}


def test_catalog_keeps_every_supported_identity_and_excludes_funds_and_aliases():
    rows = [listing("NEW", "XNAS"), listing("NEW", "XNYS"), listing("BRK.B", "XNYS"),
            listing("SPY", "ARCX"), listing("IWM", "ARCX", "ETF"), listing("!otc/OLD", "XNYS"),
            listing("SHOP", "XTSE", country="Canada")]
    universe = common_stock_listings({"status": "ok", "count": len(rows), "data": rows})
    assert [(entry.symbol, entry.mic) for entry in universe] == [("BRK.B", "XNYS"), ("NEW", "XNAS"), ("NEW", "XNYS")]


def test_catalog_request_uses_user_ml_header_and_delisting_filter(transport):
    transport.body = {"status": "ok", "count": 1, "data": [listing("NEW", "XNYS")]}
    assert fetch_us_stock_catalog(include_delisted=True) == transport.body
    request, _ = transport.calls[0]
    url = urlsplit(request.url)
    assert url.path == "/stocks" and url.hostname == "api.twelvedata.com"
    assert parse_qs(url.query) == {"country": ["United States"], "type": ["Common Stock"],
                                  "include_delisted": ["true"], "show_plan": ["true"]}
    assert request.headers["Authorization"] == "apikey ML-TEST-KEY"


def test_stock_features_cannot_cross_a_gap_or_use_a_future_outcome():
    days = pd.bdate_range("2015-01-01", "2020-01-31").as_unit("ns")
    levels = 100. + np.arange(len(days)) * .05 + np.sin(np.arange(len(days)) / 10)
    frame = pd.DataFrame({"date": days, "symbol": pd.Series("NEW", index=range(len(days)), dtype="string"),
                           "open": levels, "high": levels + 1., "low": levels - 1., "close": levels,
                           "volume": pd.Series(3000, index=range(len(days)), dtype="Int64")})[BAR_COLUMNS]
    gap = pd.Timestamp("2017-06-15")
    bars = frame.loc[frame.date.ne(gap)]
    feature_rows = stock_features(bars, days)
    first_recovered = feature_rows.loc[feature_rows.date.gt(gap), "date"].min()
    assert first_recovered > gap + pd.Timedelta(days=500)
    assert feature_rows.loc[feature_rows.date.lt(gap), "target_date_20"].dropna().lt(gap).all()
    assert feature_rows.target_20.isna().tail(21).all()

    cutoff = pd.Timestamp("2016-11-17")
    changed = bars.copy()
    changed.loc[changed.date.gt(cutoff), ["open", "high", "low", "close"]] *= 1.5
    later = stock_features(changed, days)
    pd.testing.assert_frame_equal(feature_rows.loc[feature_rows.date.le(cutoff), ["date", *FEATURES]],
                                  later.loc[later.date.le(cutoff), ["date", *FEATURES]])


def test_signal_uses_stock_trend_on_each_candidate():
    class Model:
        feature_names_in_ = FEATURES
        classes_ = [0., 1.]

        def predict_proba(self, features):
            assert list(features) == FEATURES
            return np.array([[.35, .65], [.3, .7], [.65, .35], [.8, .2]])

    features = pd.DataFrame(0., columns=FEATURES, index=range(4))
    features["symbol"] = ["NEW", "B", "C", "D"]
    features["date"] = pd.Timestamp("2025-01-02")
    features["close_ma50"] = [.1, -.1, -.1, -.2]
    features["close_ma200"] = [.1, .1, -.1, -.2]
    features["momentum_20"] = [.1, -.1, -.1, .1]
    assert scored_candidates(Model(), features).action.tolist() == ["BUY", "HOLD", "SELL", "HOLD"]


def test_latest_scan_accepts_new_listings_and_skips_stale_history():
    class Model:
        classes_ = [0., 1.]
        feature_names_in_ = FEATURES

        def predict_proba(self, features):
            return np.repeat([[.2, .8]], len(features), axis=0)

    days = pd.bdate_range("2023-01-02", "2025-01-02").as_unit("ns")
    price = np.arange(len(days)) * .01 + 50
    bars = pd.DataFrame({"date": days, "symbol": "NEW", "mic": "XNYS", "close": price, "open": price,
                         "low": price, "high": price, "volume": pd.Series(500, index=range(len(days)), dtype="Int64")})
    histories = pd.concat([bars, bars.iloc[:-1].assign(symbol="STALE"), bars.assign(symbol="SPY")], ignore_index=True)
    rows = [listing("NEW", "XNYS"), listing("STALE", "XNYS"), listing("SPY", "XNYS", "ETF")]
    catalog = common_stock_listings({"status": "ok", "count": len(rows), "data": rows})
    scores = latest_candidates(Model(), histories, days, catalog)
    assert scores.symbol.tolist() == ["NEW:XNYS"]
    assert scores.action.tolist() == ["BUY"]
