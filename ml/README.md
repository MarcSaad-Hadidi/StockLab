# StockLab Machine Learning

## Purpose

StockLab's independent Python ML module will receive historical data, clean it,
prepare features, train models, and produce prediction probabilities followed by
BUY / SELL / HOLD signals with confidence. It will never execute a trade.
The frontend and .NET backend are not required to install or test this module.

## Requirements

Use **Python 3.12.x** across the team, with pip and the standard-library `venv`
module available. Use a current stable 3.12 patch release.
Python 3.12 is officially supported by all four selected dependencies:
[NumPy](https://pypi.org/project/numpy/2.5.3/),
[pandas](https://pypi.org/project/pandas/3.0.6/),
[scikit-learn](https://pypi.org/project/scikit-learn/1.9.1/), and
[pytest](https://pypi.org/project/pytest/9.1.1/).

## Setup — Windows

From the repository root, in PowerShell, with Python 3.12 on `PATH`:

```powershell
cd ml
python --version
python -m venv .venv
.\.venv\Scripts\Activate.ps1
```

Verify that the version is `3.12.x` before creating the environment. If the
Windows Python launcher selects another version, use `py -3.12 -m venv .venv`.
In Command Prompt, activate with `.venv\Scripts\activate.bat` instead.
If PowerShell blocks activation, use Command Prompt or invoke
`.\.venv\Scripts\python.exe` in place of `python` in the commands below.

## Setup — macOS/Linux

From the repository root, in bash/zsh:

```bash
cd ml
python3.12 --version
python3.12 -m venv .venv
source .venv/bin/activate
```

If `python` already selects Python 3.12, `python -m venv .venv` is equivalent.
Some Linux distributions require the Python 3.12 `venv` OS package first.

## Install dependencies

With the environment active, from `ml/`:

```shell
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
```

The direct dependencies are NumPy, pandas, scikit-learn, pytest, and requests.
The version ranges start at verified stable releases and allow patch updates
within their minor release lines. Transitive dependencies are resolved by pip;
this is a bounded development setup, not an exact environment lock.

## Run tests

From `ml/`, with the environment active:

```shell
python -m pytest
```

The smoke tests import `numpy`, `pandas`, `sklearn`, and `stocklab_ml` without
network calls. `pytest.ini` adds `src/` using pytest's built-in `pythonpath`
setting, relative to that configuration file. No per-file path changes or
packaging dependencies are needed. The package is currently importable in tests;
it is not installed as a distribution or exposed as a command-line application.
From the repository root, `python -m pytest ml` also works with this environment.

## Project structure

```text
ml/
├── README.md
├── requirements.txt
├── pytest.ini
├── src/
│   └── stocklab_ml/
│       └── __init__.py
├── tests/
│   └── test_environment.py
├── data/
│   ├── raw/
│   │   └── .gitkeep
│   └── processed/
│       └── .gitkeep
└── models/
    └── .gitkeep
```

The local `.venv/` is ignored by Git. Future ML code belongs in
`src/stocklab_ml/`; notebooks will not be the source of truth.

## Data policy

Raw datasets in `data/raw/`, processed datasets in `data/processed/`, and trained
models in `models/` stay local and unversioned. Git ignores all contents of these
directories, including nested files and model formats such as `.pkl`, `.joblib`,
`.pickle`, and `.onnx`; only the empty `.gitkeep` placeholders are tracked.
Do not store generated datasets or models outside these directories.

## Secrets

No API key is needed for the environment smoke tests. Issue #56 uses `TWELVE_DATA_ML_API_KEY` for
Python ML only. Never commit its value or any other secret.

The Twelve Data Website key (`TwelveData:Keys:Website`) belongs exclusively to
the .NET backend's Market / Stock Details features. Python ML must not use it,
and the Website backend must not use `TWELVE_DATA_ML_API_KEY`.
There is no fallback between the two keys.

## Architecture invariant

- **ML = WHAT**: future signals and confidence.
- **Risk Manager = WHETHER + HOW MUCH**: decide whether a trade is allowed and its size.
- **Paper Trading Engine = EXECUTE**: execute approved simulated trades.

ML never executes trades. These responsibilities are documented only at this stage.

## Current status

**#54 provides the environment; #56 adds historical ingestion; #58 adds a separate
local cleaning stage.** Imputation, feature engineering, returns, indicators, scaling, train/test splits,
training, predictions, risk management, backtesting, frontend/API integration,
AWS, and Azure remain outside this module's current implementation. Installation
downloads Python packages; pytest needs no external service, real API key, or credit.

## Historical data pipeline (#56)

`stocklab_ml.data.pipeline.prepare_historical_dataset` fetches daily OHLCV from
Twelve Data, validates the response, stores local raw JSON, strictly parses bars,
sorts them chronologically, and stores a standardized CSV for #58 data cleaning.
It returns a `HistoricalDatasetResult` with `symbol`, `rows`, actual first/last
session dates, `raw_path`, `processed_path`, `metadata`, and `dataframe`.

### Provider contract and credits

Contract checked on 2026-09-19 against the official
[time-series documentation](https://twelvedata.com/docs#time-series),
[historical range guidance](https://support.twelvedata.com/en/articles/5214728-getting-historical-data),
and [credit policy](https://support.twelvedata.com/en/articles/5615854-credits).
The synchronous HTTP dependency is [requests 2.34.2](https://pypi.org/project/requests/2.34.2/),
which supports Python 3.12; the requirement allows compatible 2.34 patch updates.

- Endpoint: `GET https://api.twelvedata.com/time_series`.
- Explicit parameters: `symbol`, `interval=1day`, `start_date`, `end_date`,
  `order=asc`, `format=JSON`, `adjust=splits`.
- `adjust` supports `all`, `splits`, `dividends`, and `none`. V1 explicitly
  requests split-adjusted prices and preserves provider volume. It performs no
  manual split repair or dividend/total-return adjustment.
- Both date boundaries are always supplied; `outputsize` is omitted because it
  can truncate the requested range. The provider maximum is **5,000 points**.
- Before any network call, reject ranges spanning 5,000 or more inclusive
  calendar days. This deliberately conservative upper bound also covers markets
  trading seven days per week; it is not an exchange-calendar estimate.
- Reject responses with **4,900 or more rows** as potentially truncated; callers
  must shorten their period. A successful result is not proof that all exchange
  sessions exist: holiday calendars, listing dates, and gaps are not inferred.
- One symbol per execution, **at most one external request**, expected cost
  **1 API credit**. Invalid input or existing outputs cost zero calls. There is
  no automatic pagination, retry, provider fallback, or earliest-date lookup.
- Timeout: 20 seconds; redirects disabled. Provider HTTP/JSON errors, including
  authentication/permission failures, rate limits, unavailable services, malformed
  JSON, and empty responses, raise typed errors rather than returning empty data.

### Inputs, dates, and validation

`symbol` is trimmed and uppercased. Letters/digits and common ticker punctuation
are accepted; comma-separated batches, whitespace inside a symbol, query strings,
and path traversal prefixes are rejected. `BRK.B` is preserved. An explicit
`TSLA:NASDAQ` qualifier maps to provider `symbol=TSLA` plus `exchange=NASDAQ`;
the qualified identifier stays in the dataset and is checked against metadata.
Numeric international tickers are supported without guessing exchange mappings.

Dates accept Python `date` or strict `YYYY-MM-DD` strings. Require
`start_date < end_date < date.today()` using the machine's local date. Today is
excluded to avoid current daily bars; callers remain responsible for provider
publication delays and selecting a completed session in the relevant exchange.
Returned dates must lie inside the requested boundaries. Daily dates are calendar
session labels, stored without a timezone or invented market-opening timestamp.

The response must include nonempty `meta` and `values`. Metadata is separate from
rows: symbol, interval, currency, exchange, exchange timezone, optional MIC and
name (the documented time-series response does not promise `name`). Symbol,
exchange qualifier, and interval must agree with the request.

Every row requires a valid date, finite positive OHLC prices, and nonnegative
integer volume in the signed 64-bit range. V1 requires volume for **every**
instrument, including US common stock where it is normally present. Instruments
without volume are rejected; nullable storage dtype does not permit missing
provider volume in V1. No missing values are imputed. Booleans, malformed numeric
strings, NaN/infinity, fractional volume, and duplicate `(symbol, date)` keys fail.
Require `low <= min(open, close) <= max(open, close) <= high`.

### Processed schema

Exact column order: `date,symbol,open,high,low,close,volume`.

| Column | In-memory pandas type | Meaning |
| --- | --- | --- |
| `date` | `datetime64[ns]`, timezone-naive | Provider session date; CSV uses `YYYY-MM-DD` |
| `symbol` | `string` | Normalized requested ticker, including explicit qualifier |
| `open`, `high`, `low`, `close` | `float64` | Strictly parsed provider prices |
| `volume` | `Int64` | Nonnegative integer; missing values rejected in V1 |

Rows always run oldest to newest with a fresh integer index. CSV omits this
index. Replaying the same raw response gives the same dataset; provider order
does not affect the result. CSV itself has no dtype metadata: when reading it,
specify `parse_dates=["date"]` and explicit dtypes, especially `symbol="string"`
for leading-zero tickers and `volume="Int64"`.

Normalization means column names, numeric parsing, types, ordering, and index.
There is **no statistical scaling**, `StandardScaler`, or `MinMaxScaler`.
Any future scaler must fit on training data only after a chronological split to
avoid leakage. No future prices/features are injected into past rows, and no
weekend, holiday, or missing-session rows are synthesized. Split adjustments
remain the provider's current historical series, not a point-in-time snapshot
of what was known before later corporate actions.

### Local storage and replay

Default destinations are `ml/data/raw/` (JSON) and `ml/data/processed/` (CSV),
resolved from the module location independently of the current working directory.
Names are deterministic, for example `AAPL_1day_2016-01-01_2026-09-18.json` and
the matching `.csv`. Special symbol characters are percent-encoded in filenames
(e.g. `TSLA%3ANASDAQ`, `BTC%2FUSD`) without changing the dataset identifier.

Both outputs are checked before network access. Default `overwrite=False`
refuses either existing file; `overwrite=True` explicitly replaces outputs.
Each file is written to a temporary sibling, flushed, and atomically published
with `os.replace` for overwrite or a no-clobber hard link for default writes.
NTFS and ordinary POSIX filesystems support this; filesystems without hard links
fail safely. Temporary files are cleaned up on handled failures. A process crash
can leave a temporary sibling, but never a partially published destination.

Raw metadata and values retain their original content and ordering. Invalid
envelopes or credential-bearing responses are not saved. If bar validation fails
after raw persistence, raw JSON remains for diagnosis and no new CSV is written.
The two files are **not a transaction**: on a failure during explicit overwrite,
an older CSV can remain alongside a newer raw file. Treat only a successful
returned result as a completed pipeline run; a directory listing is insufficient.

Raw/processed datasets, virtual environments, caches, and trained models remain
Git-ignored. Never commit a live sample. Tests use synthetic fixtures and
`tmp_path`, never the real dataset directories. Custom `data_dir` is intended
for controlled local storage; keep it outside tracked paths or explicitly ignored.

To replay a local raw response without any HTTP call, use
`validation.build_dataframe(payload, validation.validate_request(symbol, start, end))`.

### Credential and usage

Only `TWELVE_DATA_ML_API_KEY` is read from the environment. No dotenv file,
configuration-file credential, Website-key fallback, or secret argument is used.
If absent, the pipeline raises `TWELVE_DATA_ML_API_KEY is not configured.`
The credential is sent in `Authorization: apikey ...`, never in query parameters.
Ambient `.netrc` authentication and proxy configuration are disabled. The code
does not log requests, credentials, provider messages, or underlying exceptions.
Responses echoing the key or credential fields are rejected before persistence;
request URLs and authentication headers are never attached to stored metadata.

Configure the environment securely outside source control. Do not paste a real
key into a script, README, shell command history, test, or `.env` file.

The #54 package remains a source package. To use it without restructuring the
packaging, launch Python **from `ml/src/`** with the existing virtual environment:

```powershell
# Windows, starting at the repository root
cd ml/src
../.venv/Scripts/python.exe
```

```bash
# macOS/Linux, starting at the repository root
cd ml/src
../.venv/bin/python
```

Then run Python (no credential value belongs in this example):

```python
from stocklab_ml.data.pipeline import prepare_historical_dataset

result = prepare_historical_dataset("AAPL", "2016-01-01", "2026-09-18")
print(result.symbol, result.rows, result.start_date, result.end_date)
print(result.raw_path, result.processed_path)
```

Do not rerun blindly: a successful run uses one request; repeating with
`overwrite=True` spends another request. An optional live smoke test may run once
after offline tests when the ML key is configured, otherwise skip it. Report
external calls and expected credits, never claim the account's exact balance.

### Verification

From `ml/`, with Python 3.12 and the virtual environment active:

```shell
python -m pip install -r requirements.txt
python -m pip check
python -m pytest
```

From the repository root: `python -m pytest ml`. Tests block socket connections
and replace only the HTTP transport. Coverage includes the emitted request,
secret-free errors/storage, provider failures, strict data validation, chronology,
deterministic replay, truncation protection, collisions, and atomic-write failures.

## Data cleaning (#58)

`stocklab_ml.data.cleaning.clean_historical_dataset(dataframe, *, as_of_date=None)`
returns `CleanedDatasetResult(dataframe, report)` without mutating its input.
`clean_processed_dataset(input_path, *, output_path=None, report_path=None,
overwrite=False, as_of_date=None)` also saves local artifacts and returns their
paths. Ingestion remains a separate, explicit stage; cleaning calls no provider
or other API and requires no key or credits. No new dependencies are needed.

Input must contain exactly `date,symbol,open,high,low,close,volume`, in any order.
Missing, unexpected (including future/target), or repeated column names raise
`SchemaValidationError`. Output uses that exact order, the #56 dtypes
(`datetime64[ns]` naive date, `string` symbol, `float64` OHLC, `Int64` volume), a
fresh index, and ascending `(symbol,date)` order. Multiple symbols are supported.

Symbols are trimmed and uppercased using #56's symbol format; punctuation and
explicit exchange qualifiers remain intact. Dates accept ISO `YYYY-MM-DD`, naive
midnight ISO timestamps (space or `T`, optional zero fractional seconds), Python
dates/datetimes, pandas timestamps, or NumPy datetimes within the nanosecond date
range. Surrounding text whitespace is trimmed. Ambiguous date strings, timezone
offsets, and non-midnight times are invalid; no timezone conversion occurs.
`as_of_date` is inclusive and follows the same date rules. Its default is the
machine's local date; supply it explicitly for reproducibility.

Each removed row has **one primary reason**, in this priority order:

1. `missing_value`: any null/NaN/NaT or blank required cell.
2. `invalid_date`: unparseable, out-of-range, timezone-aware, or intraday date.
3. `invalid_symbol`: non-text or unsupported symbol format.
4. `invalid_numeric`: malformed, boolean, or nonfinite OHLC/volume (including
   textual `NaN`/infinity). Decimal/scientific numeric strings are supported.
5. `invalid_price`: any OHLC price <= 0.
6. `invalid_ohlc`: `low <= min(open,close) <= max(open,close) <= high` fails.
7. `invalid_volume`: negative, fractional, or outside signed Int64. `1000.0` is
   accepted; volume strings are parsed without rounding through float64.
8. `future_date`: date exceeds `as_of_date`.
9. `duplicate_exact_removed`: repeated normalized observation with identical
   `(symbol,date,open,high,low,close,volume)`; keep one.

Invalid/future rows leave **before** duplicate detection. Different remaining
observations for the same `(symbol,date)` raise `DuplicateConflictError`; none
is chosen or averaged. An empty input or no surviving rows raises
`DataCleaningError`. One valid row is sufficient, with no retention threshold.
The final schema, types, values, dates, uniqueness, and ordering are explicitly
validated. Repeating cleaning with the same cutoff is deterministic and
idempotent; cleaning an already clean dataset removes zero rows.

Missing values are dropped: no forward/backward fill, interpolation, mean/median
fill, or invented OHLCV. No future observation repairs a past row. No synthetic
weekend/holiday/session dates, statistical outlier removal, scaling, features, or
targets are created. Large or small structurally valid prices remain unchanged.

The report contains `input_rows`, `output_rows`, `rows_removed`, the nine
`removal_reasons` counts (including zeros), retained `date_min`/`date_max`, sorted
`symbols`, and `retention_ratio = output_rows / input_rows`. Reason counts sum to
`rows_removed = input_rows - output_rows`. It contains no source rows, provider
URLs, credentials, or generated timestamp.

File input is local UTF-8 CSV (optional BOM), capped at **64 MiB**. Cells stay
text until cleaning, preserving tickers such as `0700` and `NA`. Malformed CSV
row widths fail. Default outputs are `<input-parent>/cleaned/<dataset>.csv` and
`<dataset>.report.json`; for #56 this is `ml/data/processed/cleaned/`, already
Git-ignored. CSV omits the index. The original stays intact; input/output/report
aliases are rejected, including resolved paths and existing hard links.

Both destinations are checked before writing. Default `overwrite=False` rejects
either existing output; replacement requires `overwrite=True`. The existing #56
atomic writer publishes each file. The pair is **not a transaction**: a report
write failure can leave the new CSV and an absent/older report. Only a successful
returned result means both artifacts were saved. Custom paths should stay in
controlled, ignored local storage.

From the Python session launched in `ml/src/` as above:

```python
from stocklab_ml.data.cleaning import clean_processed_dataset

result = clean_processed_dataset(
    "../data/processed/AAPL_1day_2016-01-01_2026-09-18.csv",
    as_of_date="2026-09-18",
)
print(result.report)
print(result.output_path, result.report_path)
```

The same offline pytest commands above cover cleaning, count consistency,
immutability, idempotence, leakage prevention, #56 CSV compatibility, and atomic
storage failure boundaries using synthetic fixtures and temporary directories.
