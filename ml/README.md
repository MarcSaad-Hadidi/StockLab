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
local cleaning stage; #60 adds deterministic feature engineering.** Imputation, scaling, train/test splits,
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

## Feature engineering (#60)

`stocklab_ml.features.engineer_features(dataframe)` returns a
`FeatureDatasetResult(dataframe, report)` without mutating the caller's frame.
It requires #58's **exact cleaned output contract**, including column order,
normalized dtypes, ascending `(symbol,date)` order, unique keys, and a fresh
integer index. Invalid input, extra columns, or reapplying it to a feature
dataset raises `FeatureEngineeringError`; nothing is cleaned or repaired.
The structural validator from #58 is reused. The cleaner owns the historical
as-of cutoff; features do not read today's date or the system timezone.

The source columns `date,symbol,open,high,low,close,volume` remain intact for
lineage. Six derived float64 columns are appended in the order below; source
volume remains observed `Int64`. `FEATURE_COLUMNS` selects the seven model
inputs in this order:

```python
["return_1d", "ma_5", "ma_20", "rsi_14", "volume", "momentum_10", "volatility_20"]
```

All windows count **observations/sessions per symbol**, include the current
session, and use only dates up to that session. Gaps, weekends, and holidays
are neither filled nor added to the calendar.

| Feature | Definition | First defined observation (1-based) |
| --- | --- | --- |
| `return_1d` | `close[t] / close[t-1] - 1`, decimal units | 2 |
| `ma_5` | Simple mean of `close[t-4] ... close[t]`, `min_periods=5` | 5 |
| `ma_20` | Simple mean of `close[t-19] ... close[t]`, `min_periods=20` | 20 |
| `rsi_14` | Wilder RSI, defined below | 15 |
| `volume` | Unchanged cleaned daily volume | 1 |
| `momentum_10` | `close[t] / close[t-10] - 1`, decimal units | 11 |
| `volatility_20` | Standard deviation of the last 20 `return_1d` values, `min_periods=20`, `ddof=0` | 21 |

Volatility is **not annualized** and uses decimal return units. For example,
`0.015` means a 1.5% daily-return standard deviation. Volume is not transformed.

Wilder RSI starts with `gain=max(delta,0)` and `loss=max(-delta,0)`, where
`delta=close[t]-close[t-1]`. Seed average gain/loss with the means of the first
14 changes. Subsequently use `(previous_average * 13 + current_gain_or_loss)/14`.
RSI is `100 - 100/(1 + average_gain/average_loss)`. Only gains gives 100, only
losses gives 0, and both averages zero gives 50. The valid range is `[0,100]`.
No external technical-analysis library is used.

Each symbol requires **at least 21 observations**; a shorter symbol fails the
entire request. The expected leading missing values are checked separately for
each feature before removing warm-up rows. V1 removes 20 leading rows per
symbol. An unexpected missing/nonfinite feature, even before another feature
finishes warming up, fails instead of silently discarding valid source rows.
The final nonempty dataset has exact columns, finite features, positive moving
averages, bounded RSI, nonnegative volatility, ordered dates, and unique keys.

The deterministic report includes actual `input_rows`, `output_rows`,
`warmup_rows_removed`, sorted `symbols`, final `date_min`/`date_max`,
`feature_columns`, `rows_per_symbol` (output counts), and
`warmup_removed_per_symbol`. `parameters` records return period 1, MA windows
5/20, RSI window 14, momentum window 10, volatility window 20, `volatility_ddof=0`,
and `volatility_annualized=false`. There is no generated timestamp.

**The same feature formulas must be used for training and inference.** This
module is their single implementation. Prefix-invariance tests verify that
appending future prices cannot change earlier features. Wilder RSI is recursive:
exact parity also requires the **same historical starting point/seed**. Supplying
only the latest 21 observations produces a causal result initialized from that
slice, but its RSI can differ from one initialized on the full history. Future
inference should replay the same history or explicitly preserve equivalent
Wilder state; stateful inference is outside #60.

No target/label, scaling, global normalization, statistical outlier removal,
train/test split, model training, trading signal, or future value is created.
There are no provider calls, required API credentials, or API credits.

### Local feature files

From the Python session launched in `ml/src/` as above:

```python
from stocklab_ml.features import FEATURE_COLUMNS, engineer_cleaned_dataset

result = engineer_cleaned_dataset(
    "../data/processed/cleaned/AAPL_1day_2016-01-01_2026-09-18.csv",
)
X = result.dataframe[FEATURE_COLUMNS]
print(result.report)
print(result.output_path, result.report_path)
```

`engineer_cleaned_dataset(input_path, *, output_path=None, report_path=None,
overwrite=False)` decodes the local #58 CSV with explicit dtypes, preserving
leading-zero/`NA` symbols and exact Int64 volume. It reuses the bounded CSV
reader (64 MiB) and never invokes the cleaner. Dates must be `YYYY-MM-DD`.
Default files are `ml/data/processed/features/<dataset>.csv` and
`<dataset>.features.json`, independent of the working directory and already
Git-ignored. CSV uses exact output columns, no index, ISO dates, and float64
precision. Both destinations are checked before writing. All three paths must
be distinct, including resolved aliases and existing hard links.

The #56/#58 atomic writer is reused; `overwrite=False` refuses either existing
file and concurrent writers cannot be clobbered. Each file is atomic, **not the
CSV/report pair**. A report failure can leave a new CSV with an absent/older
report; only a successful returned result denotes completion. The cleaned source
is never overwritten. Keep explicit custom destinations in ignored local storage.

The same offline test commands cover independent formula examples, a mixed
Wilder RSI regression, flat/rising/falling prices, constant-return volatility,
20/21-observation boundaries, multi-symbol isolation, prefix invariance, strict
input rejection, unexpected NaN/inf, immutable input, reports, CSV round trips,
collisions, and atomic publication failures. Tests block network connections.

## Logistic Regression baseline (#61)

`stocklab_ml.modeling.train_logistic_regression(feature_dataframe,
test_fraction=0.20)` fits a real scikit-learn `LogisticRegression` and returns
`LogisticRegressionResult(pipeline, predictions, report)`. The input must be
the exact #60 `OUTPUT_COLUMNS` contract, with its dtypes, finite values, unique
keys, ascending `(symbol, date)` order and fresh integer index. Raw cleaned
OHLCV is insufficient. Modeling does not repair, recompute, or modify features.

The only model inputs, in order, are the existing
`stocklab_ml.features.FEATURE_COLUMNS`: `return_1d`, `ma_5`, `ma_20`, `rsi_14`,
`volume`, `momentum_10`, `volatility_20`. Neither identifiers nor raw prices nor
labels/target dates enter X. Multi-symbol observations are pooled; symbol is
not encoded as a feature.

### Target and availability

`target_up_1d[t] = 1` when the **next observed clean session** closes above
`close[t]`; equal or lower produces integer `0`. It is a supervised label.
`build_supervised_dataset(feature_dataframe)` returns a copied DataFrame and
report. It shifts close and date separately **within each symbol**, records
`target_date`, and removes the last unlabeled row of every symbol. Removed
counts are reported as `unlabeled_rows_removed_per_symbol`. An entirely
unlabeled input fails. Weekends, holidays and missing dates are not synthesized;
Friday-to-Monday is one observed session, not one calendar day.

Features for t include `close[t]`, so the prediction is available **only after
the close of t**. It predicts direction at the next observed session close.
Future backtests must respect that availability and must never execute at an
earlier price using information from `close[t]`.

### Reusable chronological holdout

`chronological_holdout_split(supervised.dataframe, test_fraction=0.20)` is the
shared split API for #62. Sort distinct supervised feature dates ascending;
for N dates reserve the latest `ceil(test_fraction * N)` dates for test.
`split_date` is the first reserved date. This is approximately 80/20 by dates,
not necessarily by row counts, and uses one boundary for all symbols.

Train requires both `date < split_date` **and** `target_date < split_date`.
Rows before the boundary whose target lands on or after it are purged. Test
requires `date >= split_date` and an observable target. Returned partitions
are sorted by `(date, symbol)`. There is no shuffle, stratification, resampling,
cross-validation or tuning. Empty partitions, invalid fractions and a single
training class fail clearly. A single-class test set is allowed.

`HoldoutReport` records the exact boundary, fraction, train/test counts,
`purged_boundary_rows`, feature-date ranges, counts per symbol and positive
rates. To compare #62 fairly, reuse these public target/split functions on the
same feature dataset and fraction; preserve the resulting test identifiers.

### Fitting, inference and evaluation

The pipeline is `StandardScaler -> LogisticRegression(solver="lbfgs", C=1.0,
max_iter=1000, class_weight=None)`. Only the purged training matrix enters
`pipeline.fit(X_train, y_train)`. The scaler never fits the full dataset or
test data. Nonconvergence raises `ModelTrainingError`; successful reports
include convergence status and actual iteration counts.

`predict_direction(fitted_pipeline, feature_rows)` reuses the fitted scaler
and exact feature order. It requires finite real numeric `FEATURE_COLUMNS`
and valid daily `date`/`symbol` identifiers, but no raw close, future close or
label. Rows retain their input order. It returns identifiers, integer
`predicted_class` and raw `probability_up`, using the explicit class-1 index
from the classifier's `classes_`. All probabilities must be finite in `[0,1]`.
Test predictions also contain `target_date` and integer `actual_class`.

Evaluation is only `test_accuracy` using sklearn `accuracy_score`, plus the
reported class balance. This is a technical baseline, with no claim about
financial performance. #63 owns precision/recall/F1 and model comparison.
There are no BUY/SELL/HOLD signals, confidence score, risk decisions, orders,
position sizing, portfolio or backtesting operations. No provider, API key,
network request or API credit is involved. Results are reproducible for the
same data, sklearn version and configuration.

### Local results and feature files

From the Python session in `ml/src/`:

```python
from stocklab_ml.modeling import (
    load_feature_csv, predict_direction, save_model_results,
    train_logistic_from_feature_file,
)

source = "../data/processed/features/AAPL_1day_2016-01-01_2026-09-18.csv"
result = train_logistic_from_feature_file(source)
saved = save_model_results(result)
print(result.report.test_accuracy, result.report.split)
print(saved.report_path, saved.predictions_path)
latest = load_feature_csv(source).tail(1)
print(predict_direction(result.pipeline, latest))
```

`load_feature_csv` reuses the bounded local CSV reader, preserves symbols such
as `NA` and exact Int64 volume, and restores the #60 dtypes without calculating
features. Malformed data fails. The file-training helper records the source
path in memory to protect it during saving; this path is excluded from JSON.
For a DataFrame loaded by your own code, supply its `source_path` explicitly
to `save_model_results` to enable the same source protection.

Default output is `ml/results/logistic_regression/report.json` and
`predictions.csv`, independent of the working directory. Optional
`report_path` and `predictions_path` select other local destinations.
`overwrite=False` is the default. Both destinations are checked before writing;
known source paths and both outputs must be distinct, including resolved
aliases and existing hard links. The #56 atomic writer is reused: each file
is atomic, **the pair is not a transaction**. A second-file failure may leave
a complete predictions CSV with an absent/older report, and raises an error.

JSON contains deterministic metadata: feature list, target definition,
prediction timing, split rule/report, sample counts, unlabeled row counts,
accuracy, scaler, all classifier hyperparameters and convergence iterations.
It contains no model object, raw dataset, absolute source path or timestamp.
CSV has `date,target_date,symbol,actual_class,predicted_class,probability_up`,
ISO dates and no index. The results directory ignores generated files; custom
destinations should also be kept in ignored local storage. The fitted pipeline
stays in memory: no pickle, joblib artifact, model version, registry or cloud
storage is created. Model persistence/versioning belongs to later issues (#75).

The offline tests cover per-symbol labeling, equal/down prices, last-row
removal, sparse multi-symbol dates, boundary purging, single-class/empty
partitions, exact training columns, train-only scaling and coefficients,
class-order-safe probabilities, inference parity, immutability, determinism,
nonconvergence, CSV round trips and atomic publication failures. Tests block
network connections and use synthetic data and temporary output directories.

Run the reproducible synthetic smoke from `ml/` (no market data download):

```powershell
$env:PYTHONPATH = "src"
python examples/logistic_regression_smoke.py
```

It generates two symbols locally, runs the existing feature engineering,
trains the baseline, verifies row/probability/accuracy invariants, and writes
ignored results. The synthetic accuracy checks mechanics, not market quality.

## Random Forest baseline (#62)

`stocklab_ml.modeling.train_random_forest(feature_dataframe,
test_fraction=0.20)` trains a real scikit-learn `RandomForestClassifier` and
returns `RandomForestResult(model, predictions, report)`. It also runs the
unchanged #61 Logistic Regression baseline on the same input and fraction to
report a minimal accuracy comparison. It neither replaces nor promotes a model.

### Shared dataset and holdout

RF calls the existing `build_supervised_dataset` and
`chronological_holdout_split`. `FEATURE_COLUMNS`, `TARGET_NAME`,
`TARGET_DEFINITION`, `SPLIT_RULE` and `PREDICTION_TIMING` remain unchanged.
The exact seven features are consumed in their existing order; symbol, date,
raw OHLC prices, target dates, labels and future closes never enter X.
No feature is recomputed, dropped, clipped or normalized, and input is not mutated.

The label is still integer `target_up_1d`: 1 if the next observed clean session
of the same symbol closes higher, otherwise 0. The final unlabeled row per
symbol is excluded. Features at t, including its close, are available only
after close[t]. This predicts the next observed session, not the next calendar
day; future backtests must respect the same after-close availability.

The shared holdout uses the latest `ceil(test_fraction * unique feature dates)`
dates, with one global boundary for all symbols. Train requires both feature
and target dates before that boundary; crossing targets are purged. Fit sees
only this purged train partition. Single-class training and empty partitions
retain #61's errors; single-class test sets are supported.

Before computing a comparison, the implementation checks that the complete
holdout reports and ordered `(date, target_date, symbol, actual_class)` test
columns match exactly. A discrepancy raises `ModelTrainingError`; accuracy
from different validation datasets is never compared.

### Fixed estimator and inference

| Parameter | Value |
| --- | --- |
| `n_estimators` | `300` |
| `criterion` | `"gini"` |
| `max_depth` | `None` |
| `min_samples_split` | `2` |
| `min_samples_leaf` | `1` |
| `max_features` | `"sqrt"` |
| `bootstrap` | `True` |
| `class_weight` | `None` |
| `random_state` | `42` |
| `n_jobs` | `1` |

These parameters were checked against the project's installed scikit-learn
1.9.1. Internal bootstrap/feature randomness has a fixed seed, and `n_jobs=1`
keeps execution independent of available machine parallelism. Repeated runs
with the same dataset, configuration and sklearn version are deterministic.
There is no hyperparameter search, cross-validation, test-set optimization,
resampling or automatic class weighting.

**RF has no scaler.** Trees consume raw #60 features and use thresholds; they
do not require standardization. The separate Logistic comparator retains its
train-only `StandardScaler -> LogisticRegression` pipeline and all its original
hyperparameters. No Logistic public API or behavior is changed.

`predict_random_forest_direction(fitted_model, feature_rows)` requires valid
`date`/`symbol` identifiers and finite real numeric `FEATURE_COLUMNS`. It needs
no target, raw close or future observation, preserves row order and never fits.
Outputs are `date,symbol,predicted_class,probability_up`. Class 1 is looked up
explicitly in `classes_`, which must contain exactly `{0,1}`. Predicted classes
are integers; probabilities must be finite in `[0,1]` with one value per row.
Holdout predictions use the same six columns and order as #61:
`date,target_date,symbol,actual_class,predicted_class,probability_up`.

### Importances and minimal comparison

`RandomForestReport` records `random_forest_baseline`, the shared feature/target/
timing/split metadata, dataset and holdout reports, all actual hyperparameters,
`random_state=42`, `scaler=None`, `test_accuracy`, `baseline_model_name`,
`baseline_accuracy` and `accuracy_delta = RF accuracy - Logistic accuracy`.
Accuracy uses sklearn's `accuracy_score` without internal rounding. A negative
delta is valid; there is no expected winner or financial performance guarantee.
#63 `feature/ml-model-evaluation` owns precision/recall/F1 and full comparison.

`feature_importances` maps `feature_importances_` to the exact `FEATURE_COLUMNS`
order. These impurity-based values must be finite, nonnegative and sum to
approximately 1. A forest with no impurity-reducing splits (for example, all
constant input features) has undefined normalized importance and raises a clear
`ModelTrainingError`; no importance is fabricated. These are properties of the
fitted model, **not evidence that a feature causes market movements**. No SHAP
or permutation-importance framework is introduced.

### Local files and verification

From the Python session in `ml/src/`:

```python
from stocklab_ml.modeling import (
    load_feature_csv, predict_random_forest_direction, save_model_results,
    train_random_forest_from_feature_file,
)

source = "../data/processed/features/AAPL_1day_2016-01-01_2026-09-18.csv"
result = train_random_forest_from_feature_file(source)
print(result.report.test_accuracy, result.report.baseline_accuracy,
      result.report.accuracy_delta)
saved = save_model_results(result)
latest = load_feature_csv(source).tail(1)
print(predict_random_forest_direction(result.model, latest))
```

The existing strict CSV loader and atomic writer are reused. RF defaults to
`ml/results/random_forest/report.json` and `predictions.csv`; Logistic keeps
its existing directory. JSON includes importances and comparison metadata, with
no model object or absolute source path. `overwrite=False`, source/alias checks,
CSV without index and **per-file** atomicity are unchanged. The two-file pair
is not a transaction; a report failure may leave a complete predictions CSV.
File-based training records the source for overwrite protection; callers who
load their own DataFrame should pass `source_path` when saving.

Outputs are Git-ignored. The estimator stays in memory: no pickle, joblib, ONNX,
model registry/version or cloud storage is added; #75/#76 own persistence.
There are no BUY/SELL/HOLD signals, confidence score, risk/position decisions,
trading operations, external provider calls, API keys or API credits. No new
dependencies are required. #63 is not implemented here.

Run the synthetic comparison smoke from `ml/`:

```powershell
$env:PYTHONPATH = "src"
python examples/random_forest_smoke.py
```

It creates 480 #60 feature rows across two symbols locally, verifies identical
holdout identifiers and labels, validates both probability outputs and RF
importances, and saves ignored RF results. Synthetic accuracy verifies mechanics
only. The offline pytest suite additionally tests every fitted tree for invariance
when only test features change, repeat-run determinism, raw training features,
comparison mismatch rejection, target-free inference and storage failure cases.
