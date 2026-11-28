"""Scan the current catalog against a local daily common-stock OHLCV snapshot."""

import argparse
import json
from pathlib import Path
import sys

import pandas as pd

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from stocklab_ml.boosted_market import BAR_COLUMNS, common_stock_listings, latest_candidates, load_directional_model


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for parameter in ("catalog", "bars", "calendar", "models", "output"):
        parser.add_argument(f"--{parameter}", type=Path, required=True)
    parser.add_argument("--session", required=True)
    args = parser.parse_args()
    date = pd.Timestamp(args.session)
    catalog = common_stock_listings(json.loads(args.catalog.read_bytes()))
    dates = pd.read_csv(args.calendar, usecols=["date"], parse_dates=["date"]).date
    calendar = pd.DatetimeIndex(dates.loc[dates.le(date)]).as_unit("ns")
    if len(calendar) == 0 or calendar[-1] != date:
        raise ValueError("The requested date must be a completed stock-market session in the calendar.")
    bars = pd.read_csv(args.bars, usecols=BAR_COLUMNS + ["mic"], parse_dates=["date"],
                       dtype={"volume": "Int64", "symbol": "string", "mic": "string",
                              **{col: "float64" for col in ("open", "high", "low", "close")}})
    bars["date"] = bars.date.astype("datetime64[ns]")
    scores = latest_candidates(load_directional_model(args.models, date), bars, calendar, catalog)
    args.output.mkdir()
    scores.to_csv(args.output / "decisions.csv", index=False)
    receipt = {"decision_session": str(date.date()), "catalog_identities": len(catalog),
               "stocks_scored": len(scores), "not_scored": len(catalog) - len(scores),
               "actions": scores.action.value_counts().to_dict()}
    (args.output / "coverage.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt), flush=True)


if __name__ == "__main__":
    main()
