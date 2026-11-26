# Market-wide inputs for the directional boost

The daily scanner takes a Twelve Data common-stock catalog and dated OHLCV. It checks the stock's symbol and MIC against the catalog, requires an observed close for the decision session and scores each eligible listing. ETF prices and benchmark prices remain outside the stock score. A stock needs complete indicator warm-up within a continuous run of observed sessions. The scanner reports coverage across the catalog and emits `BUY`, `SELL` and `HOLD` to a read-only ledger.

The underlying frozen model estimates the probability of a price return above 0.2% over 20 sessions from the following open. An entry requires a score of at least 0.65 and a close above the stock’s 50-session average. A sale requires a score of at most 0.35, a close below its 200-session average and negative 20-session momentum. In the existing directional portfolio, an order uses the following actual tradable opening. Sales reduce the held shares by 25% and are spaced by at least 20 sessions for each stock.

## Data scope

The November 26 Twelve Data snapshot returned 16,460 active US common-stock rows, including 10,669 OTC rows. It returned 28,199 identifiers when delisted records were requested. Some rows use vendor transition aliases that cannot serve as orderable symbols. The catalog fields establish typed coverage. Prices, exact listing intervals and corporate actions require separate validation. Basic time series cost one credit per ticker and have an 800-credit daily ceiling. [Twelve Data stock catalog](https://twelvedata.com/docs/asset-catalogs/stocks-list) · [Twelve Data credit accounting](https://support.twelvedata.com/en/articles/5615854-credits)

## Run the scanner

Use an OHLCV CSV with `date,symbol,mic,open,high,low,close,volume` and a CSV of the stock exchange sessions with a `date` column. Use split-adjusted prices and normalized volume. The models folder is the trusted local fit directory with annual estimator hashes and fit boundaries. The output directory must be new.

```powershell
python ml/scripts/scan_boosted_market.py `
  --catalog path/to/twelve-active.json --bars path/to/daily-bars.csv `
  --calendar path/to/sessions.csv --models path/to/fitted-v1 `
  --session 2026-11-24 --output path/to/market-scan
```

`decisions.csv` records the score and action for every scored identity. `coverage.json` gives the number scored and the number of catalog identities without a result.

## Observed coverage

On November 24, 2026, the scanner scored 603 common-stock listings from 610 matched price histories. It returned 42 buys, 5 sells and 556 holds. Forty-one of the buy signals came from outside the original eight-stock universe. The strongest directional scores among the new buy signals were TTD, BSX and WYNN. The data came from the Yahoo rows in a public historical price package. Eight overlapping closing prices agreed exactly with the previous price tape. The scan covers part of the 16,458 reportable catalog identities. [Recent price source and methodology](https://github.com/Johnbrick123/sp500-data)

## Historical coverage and identity review

The WIKI archive contains 3,199 tickers ending March 27, 2018. The original screen produced forecasts for 2,152 of the 2,173 unambiguously matched US exchange ticker strings. This is an archive coverage measurement. Listing intervals and legal share classes need independent verification. November 8, 2017 is absent across the archive. Resetting features on that gap ends the usable scored period on November 7 despite later price marks. [Nasdaq Data Link WIKI coverage](https://data.nasdaq.com/databases/WIKIP) · [Public archive mirror](https://www.kaggle.com/datasets/marketneutral/quandl-wiki-prices-us-equites)

The exploratory ledger touched 99 distinct stocks from the 1993 start. It incorrectly carried some holdings across a change of share class. Tidewater and Nuverra began quoting new common stock under the same tickers after a reorganization. Sterling Financial shareholders received Umpqua stock and cash upon merger. Those cases invalidate the exploratory dollar figures until the exchange ratios, payments and successor shares are reconciled. [Tidewater share-class notice](https://www.sec.gov/Archives/edgar/data/98222/000119312517238946/d431576dex991.htm) · [Nuverra cancellation filing](https://www.sec.gov/Archives/edgar/data/1403853/000140385317000028/nuverra-form15.htm) · [Sterling merger filing](https://www.sec.gov/Archives/edgar/data/1077771/000110465914028751/a14-10421_38k.htm)

`ml/examples/boosted_market_archive.py` measures signal coverage. It takes a `--identity-breaks` CSV with `symbol,date`, resets feature history across known share-class changes and records a separate count of buy candidates. Use August 1, 2017 for the successor TDW quote, August 7, 2017 for NES and April 18, 2014 for STSA. A complete historical execution dataset needs dates and cash or stock treatment for all held positions affected by corporate actions.

The paper trader still needs a daily feed with sufficient market-wide history and a separately verified execution build before adopting the read-only scan output.
