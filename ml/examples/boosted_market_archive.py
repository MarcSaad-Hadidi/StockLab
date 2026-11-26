"""Measure directional-signal coverage on dated US ordinary-stock histories."""

import argparse
from collections import Counter, defaultdict
from concurrent.futures import ProcessPoolExecutor
import hashlib
import json
import os
from pathlib import Path
import sys

os.environ.setdefault("OMP_NUM_THREADS", "1")
os.environ.setdefault("OPENBLAS_NUM_THREADS", "1")
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

import numpy as np
import pandas as pd
from threadpoolctl import threadpool_limits

from stocklab_ml.boosted_market import (BAR_COLUMNS, FEATURES, KEY, common_stock_listings, load_directional_model,
                                       scored_candidates, stock_features)


FIRST = pd.Timestamp("1993-01-29")
PRICE_COLUMNS = ["open", "high", "low", "close"]
RAW_COLUMNS = ["ticker", "date", *PRICE_COLUMNS, "volume", "split_ratio"]


def file_hash(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def archive_tickers(path):
    pending = pd.DataFrame()
    seen = set()
    with pd.read_csv(path, compression="zip", usecols=RAW_COLUMNS, dtype={"ticker": "string"},
                     chunksize=180_000) as chunks:
        for chunk in chunks:
            block = pd.concat([pending, chunk], ignore_index=True)
            tail = block.ticker.iloc[-1]
            pending = block.loc[block.ticker.eq(tail)]
            for ticker, records in block.loc[block.ticker.ne(tail)].groupby("ticker", sort=False):
                if ticker in seen:
                    raise ValueError(f"Split ticker records in archive for {ticker}.")
                seen.add(ticker)
                yield str(ticker), records
    if not pending.empty:
        tail = str(pending.ticker.iloc[0])
        if tail in seen:
            raise ValueError(f"Repeated final ticker {tail}.")
        yield tail, pending


def supported_tickers(payload):
    candidates = defaultdict(list)
    for listing in common_stock_listings(payload):
        if listing.exchange in {"NYSE", "NASDAQ", "IEX", "CBOE", "NYSE American", "NYSE Arca"}:
            candidates[listing.symbol].append(listing)
    # An unqualified archive ticker cannot identify multiple provider listings.
    return {symbol for symbol, matches in candidates.items() if len(matches) == 1}


def prepare_stock(item):
    ticker, raw, calendar, transitions = item
    day = pd.to_datetime(raw.date).astype("datetime64[ns]")
    date_mask = day.isin(calendar)
    rows = raw.loc[date_mask].copy()
    rows["date"] = day[date_mask]
    rows = rows.sort_values("date").reset_index(drop=True)
    if rows.date.duplicated().any():
        return None, {"duplicate_quotes": 1}
    sane = (rows[PRICE_COLUMNS + ["split_ratio"]].gt(0).all(axis=1) & rows.volume.ge(0)
            & rows.high.ge(rows[["open", "close", "low"]].max(axis=1))
            & rows.low.le(rows[["open", "close", "high"]].min(axis=1)))
    bad = int((~sane).sum())
    rows = rows.loc[sane].reset_index(drop=True)
    if len(rows) < 380:
        return None, {"too_short": 1, "invalid_bars": bad}
    features = []
    # The successor must rebuild all indicators. No old-class outcomes cross a break.
    classes = pd.DatetimeIndex(transitions).searchsorted(rows.date, side="right")
    for _, episode in rows.groupby(classes, sort=False):
        split = episode.split_ratio.shift(-1, fill_value=1.).iloc[::-1].cumprod().iloc[::-1]
        bars = episode.assign(symbol=pd.Series(ticker, index=episode.index, dtype="string"))
        bars[PRICE_COLUMNS] = bars[PRICE_COLUMNS].div(split, axis=0)
        bars["volume"] = (episode.volume * split).round().astype("Int64")
        panel = stock_features(bars[BAR_COLUMNS].reset_index(drop=True), calendar)
        eligible = panel.loc[panel.date.ge(FIRST) & panel.date.isin(episode.loc[episode.volume.gt(0), "date"])]
        if not eligible.empty:
            features.append(eligible[KEY + FEATURES])
    if not features:
        return None, {"feature_warmup": 1, "invalid_bars": bad}
    return pd.concat(features, ignore_index=True), {"scored_equities": 1, "invalid_bars": bad}


def score_archive(archive, calendar, eligible, models, breaks, output):
    stats, counts = Counter(), Counter()
    cover = np.zeros(len(calendar), dtype="int64")
    buyers = set()
    output_file = output / "signals.csv"

    def finish(results):
        frames = []
        for features, tally in results:
            stats.update(tally)
            if features is not None:
                frames.append(features)
        if not frames:
            return
        panel = pd.concat(frames, ignore_index=True)
        np.add.at(cover, calendar.get_indexer(panel.date), 1)
        for year, frame in panel.groupby(panel.date.dt.year, sort=False):
            scores = scored_candidates(models[year], frame)
            counts.update(scores.action)
            buyers.update(scores.loc[scores.action.eq("BUY"), "symbol"])
            scores.loc[scores.action.ne("HOLD")].to_csv(output_file, index=False, mode="a", header=not output_file.exists())

    with ProcessPoolExecutor(max_workers=min(6, os.cpu_count() or 1)) as workers, threadpool_limits(limits=1):
        batch = []
        matched = set()
        for ticker, rows in archive_tickers(archive):
            stats["archive_symbols"] += 1
            symbol = ticker.replace("_", ".")
            if symbol not in eligible:
                continue
            if symbol in matched:
                raise ValueError(f"Multiple archive tickers match {symbol}.")
            matched.add(symbol)
            batch.append((symbol, rows, calendar, breaks.get(symbol, ())))
            if len(batch) == 48:
                finish(workers.map(prepare_stock, batch, chunksize=2))
                batch = []
                print(f"Scanned {stats['archive_symbols']:,}, with {stats['scored_equities']:,} eligible histories", flush=True)
        if batch:
            finish(workers.map(prepare_stock, batch, chunksize=2))
    if not stats["scored_equities"]:
        raise ValueError("No stocks qualified for the historical scanner.")
    pd.DataFrame({"date": calendar, "stocks_scored": cover}).to_csv(output / "daily-coverage.csv", index=False)
    return {"coverage": dict(stats), "catalog_matched": len(matched), "actions": dict(counts),
            "distinct_buy_candidates": len(buyers), "last_scored_session": str(calendar[cover > 0][-1].date())}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("archive", "catalog", "stock-calendar", "identity-breaks", "models", "output"):
        parser.add_argument(f"--{name}", required=True, type=Path)
    args = parser.parse_args()
    catalog = json.loads(args.catalog.read_bytes())
    calendar = pd.DatetimeIndex(pd.read_csv(args.stock_calendar, parse_dates=["date"]).date).as_unit("ns")
    if not calendar.is_monotonic_increasing or not calendar.is_unique or calendar[-1] < FIRST:
        raise ValueError("The daily calendar must include the research window in chronological order.")
    transitions = pd.read_csv(args.identity_breaks, usecols=["symbol", "date"], parse_dates=["date"])
    breaks = {symbol: tuple(sorted(notes.date)) for symbol, notes in transitions.groupby("symbol")}
    first_sessions = calendar[calendar >= FIRST].to_series().groupby(lambda day: day.year).min()
    models = {year: load_directional_model(args.models, first) for year, first in first_sessions.items()}
    args.output.mkdir()
    summary = score_archive(args.archive, calendar, supported_tickers(catalog), models, breaks, args.output)
    summary["source_sha256"] = {path.name: file_hash(path) for path in (args.archive, args.catalog, args.stock_calendar, args.identity_breaks)}
    (args.output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(summary, flush=True)


if __name__ == "__main__":
    main()
