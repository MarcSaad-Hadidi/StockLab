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

The direct dependencies are NumPy, pandas, scikit-learn, pytest, requests,
joblib and boto3.
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
it is not installed as a distribution. The #77 one-shot CLI below uses `PYTHONPATH=src`.
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
storage is created by #61. Explicit metadata versioning is provided by #75 below;
binary model persistence remains #76.

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
model registry/version or cloud storage is added by #62; #75 below provides
metadata versioning and #76 will provide binary persistence.
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

## Model evaluation (#63)

`stocklab_ml.modeling.evaluate_predictions(*, model_name, predictions)` is the
generic, offline evaluator for any model that produces the existing prediction
contract. It requires no estimator and never calls fit. It evaluates the supplied
integer `predicted_class` directly, without recomputing it from probability,
changing the threshold, calibrating probabilities or modifying its input.

Positive class is **UP = 1**: the next observed clean session of the same symbol
closes higher. Negative class 0 means equal or lower. The shared target,
features, after-close availability, chronological split and boundary purge are
unchanged. Evaluation uses the holdout predictions from #61/#62; it does not
construct a different dataset or split.

### Metrics and edge cases

The four metrics use `sklearn.metrics` with positive label 1. Undefined
precision/recall/F1 explicitly use `zero_division=0`, without warnings. A
single-class actual holdout, no positive predictions and all-wrong predictions
are supported. For an all-negative perfect holdout, accuracy is 1 while
positive-class precision/recall/F1 are 0 under this policy.

| Metric | Definition |
| --- | --- |
| Accuracy | Correct predictions / total predictions |
| Precision | TP / (TP + FP) |
| Recall | TP / (TP + FN) |
| F1 | 2 × precision × recall / (precision + recall) |

`confusion_matrix(labels=[0,1])` has orientation `[[TN, FP], [FN, TP]]`.
`ModelEvaluationMetrics` contains `model_name`, `accuracy`, `precision`,
`recall`, `f1`, `true_positive`, `true_negative`, `false_positive`,
`false_negative`, `support_positive=TP+FN`, `support_negative=TN+FP`, and
`total_rows`. Counts must sum to the row count and metric values must be finite
in `[0,1]`. Values retain full float precision; presentation may round later.

Predictions must be a nonempty DataFrame with exactly these ordered columns:
`date,target_date,symbol,actual_class,predicted_class,probability_up`.
Classes must be integer 0/1 (not bool, float or text). Probabilities must be
finite real numbers in `[0,1]`; they are validated but not scored as log loss,
AUC, Brier score or calibration. Dates must be timezone-naive daily datetime
values, with `target_date > date`. Symbols must follow the existing symbol
contract and `(date,symbol)` keys must be unique. Missing/invalid data raises
`ModelEvaluationError`; no rows are repaired, reordered or dropped.

### Strict model comparison

`compare_model_results(logistic_result, random_forest_result)` compares already
trained results. It requires identical **complete holdout reports**, including
split date, test fraction, row counts, ranges, class balances, per-symbol counts
and boundary purge counts. Ordered `date,target_date,symbol,actual_class` values
must match exactly; the function does not sort mismatched rows into alignment.
It also verifies the current shared feature list, target name/definition,
prediction timing and split rule. Matching but stale test counts, ranges,
per-symbol counts or positive rates are rejected against the actual predictions.
Each recomputed accuracy must exactly equal its model report's `test_accuracy`.
Any discrepancy raises `ModelEvaluationError` instead of returning a comparison.

`ModelComparisonReport` contains the feature/target/timing/split contract,
`positive_class=1`, `zero_division=0`, split date, test fraction, test row count,
test date range, model metrics and `metric_deltas`. Model order is always
`logistic_regression_baseline`, then `random_forest_baseline`. All four deltas
are **Random Forest minus Logistic Regression**, in decimal units and within
`[-1,1]`; `0.60 - 0.55` is stored as approximately `0.05`, without an implicit
percentage-point conversion. No winner, active model or promotion is selected.

### Evaluate once, save locally

`evaluate_model_baselines(feature_dataframe, test_fraction=0.20)` is a thin
convenience API returning the comparison report. It calls the existing RF
training API, which already trains Logistic for #62's baseline comparison.
`RandomForestResult` now retains that in-memory result as optional
`baseline_result`, so the workflow fits each estimator **once**. Existing
constructors/imports remain compatible; training parameters, splits and model
outputs are unchanged. Neither evaluation nor observed metric values cause a
subsequent refit. Existing results can always be compared directly.

From a Python session in `ml/src/`:

```python
from stocklab_ml.modeling import (
    compare_model_results, load_feature_csv, save_evaluation_report,
    save_model_results, train_random_forest,
)

source = "../data/processed/features/AAPL_1day_2016-01-01_2026-09-18.csv"
forest = train_random_forest(load_feature_csv(source))
logistic = forest.baseline_result
comparison = compare_model_results(logistic, forest)
print(comparison.models, comparison.metric_deltas)
save_model_results(logistic, source_path=source)
save_model_results(forest, source_path=source)
print(save_evaluation_report(comparison))
```

`save_evaluation_report(report, output_path=None, overwrite=False)` uses the
existing atomic JSON writer. The default is
`ml/results/evaluation/comparison.json`, independent of the working directory.
An existing destination is refused unless overwrite is explicitly enabled.
Publication failures leave no partially published JSON; concurrent writers
cannot be silently overwritten by the default mode. The generated report is
Git-ignored and contains only deterministic metadata, metrics and deltas: no
raw predictions, market dataset, timestamp, absolute source path or model object.
The existing per-model prediction CSVs remain available through their storage API.

There is no new model, dependency, network request, API key or API credit.
There is no tuning, threshold search, winner promotion (#78), model versioning
or binary persistence in #63 (see #75 below and future #76), BUY/SELL/HOLD (#64), confidence conversion
(#65), trading, risk management, backtest metrics (#73), benchmark (#74), cloud
integration or frontend/API work. Classification measurements do not establish
profitability or justify model promotion.

Run the standalone synthetic evaluation smoke from `ml/`:

```powershell
$env:PYTHONPATH = "src"
python examples/model_evaluation_smoke.py
```

It builds 480 synthetic #60 feature rows, fits both existing estimators once,
checks the same holdout, finite metrics, confusion counts and deltas, then saves
the ignored comparison JSON. The output is a mechanics check, not a measurement
of market performance. Offline tests include explicit hand-calculated symmetric
and asymmetric examples, perfect/all-wrong predictions, zero-division cases,
contract/label/order mismatches, report accuracy consistency, input immutability,
determinism, single-fit orchestration and atomic storage failures.

## Trading signals (#64)

`stocklab_ml.signals.generate_trading_signals` converts existing model predictions
into daily ML decisions. It does not fit a model or call inference. The caller
provides one explicit `model_name` and that model's prediction DataFrame per batch.
Logistic Regression, Random Forest and future models use the same policy.
Evaluation metrics and `comparison.json` are not used to choose a model.

| Raw `probability_up` | Signal |
| --- | --- |
| `p >= 0.60` | `BUY` |
| `p <= 0.40` | `SELL` |
| `0.40 < p < 0.60` | `HOLD` |

BUY and SELL include their boundaries: `0.60` is BUY, `0.40` is SELL.
Examples: `0.81` BUY, `0.59` HOLD, `0.50` HOLD, `0.41` HOLD, `0.15` SELL.
The HOLD interval is `]0.40, 0.60[`: it expresses uncertainty around 0.50.
`predicted_class` is retained for audit only. A predicted class of 1 with
probability 0.55 still gives HOLD; a class of 0 with probability 0.45 also gives
HOLD. Neither the class nor another symbol's probability overrides the policy.

`BUY_THRESHOLD = 0.60`, `SELL_THRESHOLD = 0.40` and the string enum
`TradingSignal.BUY / SELL / HOLD` are centralized in `signals.contracts` and
exported by `signals`. The API accepts explicit threshold arguments, validated
as real numeric, finite, non-boolean `0 <= sell < buy <= 1`. Defaults are a V1
policy fixed before trading evaluation. Valid thresholds are represented as
float64 for both mapping and reporting; thresholds that collapse to the same
float are rejected. This implementation never searches or
tunes thresholds on the holdout, metrics or returns; #73 will assess trading
consequences.

### Prediction and decision contract

Required input columns are `date`, `symbol`, `predicted_class`, `probability_up`.
Historical predictions may additionally have `actual_class` and `target_date`.
Only the four required columns are read: optional historical values are not
validated, copied to the output or used in decisions. Changing or omitting them
produces exactly the same signals and report. Future inference therefore needs
no outcome labels or future session dates. Extra columns, including an input
`model_name` column, are rejected; model identity comes from the explicit argument.

Inputs must be nonempty, with unique column names, valid timezone-naive daily
datetime values representable in nanoseconds, normalized symbols under the
existing symbol contract, integer non-boolean classes 0/1, and finite real
non-boolean probabilities in [0,1]. Invalid rows, null required values and duplicate
`(date, symbol)` keys reject the whole operation with `TradingSignalError`.
No clipping, missing-row removal, symbol repair or silent threshold correction
occurs. Model names contain 1–128 ASCII letters, digits, underscores, dots or
hyphens, beginning with a letter or digit; whitespace is rejected, not trimmed
silently. Names are preserved and are not limited to a model whitelist.

The returned `TradingSignalsResult` contains `signals` and `report`.
Signals have exactly these columns, sorted by `(date, symbol)` with a fresh index:

| Column | dtype / meaning |
| --- | --- |
| `date` | `datetime64[ns]`; feature/prediction session date |
| `symbol` | `string`; unchanged normalized symbol, e.g. `TSLA:NASDAQ` |
| `model_name` | `string`; caller's explicit model identity |
| `predicted_class` | `int64`; original class for audit |
| `probability_up` | `float64`; raw model probability, uncalibrated here |
| `signal` | `string`; exactly `BUY`, `SELL`, or `HOLD` |

The logical unique key is `(model_name, symbol, date)`. All valid rows are retained.
The deterministic report contains model name, input/output rows, BUY/SELL/HOLD
counts, thresholds, ISO date bounds and sorted symbols. Counts sum to output rows,
which equal input rows. The caller's DataFrame is never mutated.

`date` is the decision's daily calendar date: features at close[t] become available
only **after close[t]**, and the decision concerns the next observed session.
There is no invented 09:30/16:00 timestamp and no claim of pre-close availability.
Future backtesting and execution must respect this timing.

```python
from stocklab_ml.signals import generate_trading_signals, save_trading_signals

# Existing Logistic or RF result; no retraining happens in the signal generator.
signals = generate_trading_signals(
    result.predictions, model_name=result.report.model_name,
)
print(signals.signals)
print(signals.report)
saved = save_trading_signals(signals)

# Target-free predictions from the existing inference API work identically:
# predictions = predict_direction(fitted_pipeline, feature_rows)
# or predict_random_forest_direction(fitted_forest, feature_rows)
# signals = generate_trading_signals(predictions, model_name="my_model_v2")
```

### Local storage and boundaries

`save_trading_signals(result, signals_path=None, report_path=None,
source_path=None, overwrite=False)` writes ignored `ml/results/signals/signals.csv`
and `ml/results/signals/report.json`. Defaults are independent of the working
directory. CSV contains the six columns above with no artificial index; JSON
contains only report metadata, never actual outcomes, models or source data.
Pass separate destination paths when retaining multiple model batches.

Storage reuses the existing atomic CSV/JSON writer. Both collisions are checked
before writing and replacement requires `overwrite=True`. Files are atomic
individually, not as a pair: if report publication fails, a complete CSV can
remain. Temporary files are cleaned up. Output aliases and hard links are
rejected. Callers loading predictions from a file should pass `source_path` to
protect that source from overwrite, including through aliases or hard links.

`probability_up` is a raw model probability. The separate confidence stage (#65)
below enriches these signals for downstream use. There is no model promotion
(#78), quantity, cash/position/exposure check, stop loss, take profit, allocation,
Risk Manager (#67), trade execution (#68), portfolio update or decision-history
database (#69). HOLD comes only from the probability zone, never from risk
rejection. No model training or evaluation is changed. No new dependency,
network request, API credit, cloud service, frontend or backend is involved.

Run the offline smoke from `ml/`:

```powershell
$env:PYTHONPATH = "src"
python examples/trading_signals_smoke.py
```

It verifies AAPL 0.75 → BUY, MSFT 0.50 → HOLD, NVDA 0.20 → SELL, saves the CSV
and report (explicitly overwriting earlier smoke outputs), and runs the same
policy on both real estimators fitted locally on synthetic data. Historical
and target-free results match exactly. These are mechanics checks; no trade
is executed and no market performance is measured.

## Confidence score (#65)

`add_confidence_scores(signals_result: TradingSignalsResult) -> ConfidenceSignalsResult`
enriches the existing #64 decisions with one `confidence` field. It consumes the
six-column signal DataFrame and its `TradingSignalsReport`. It does not regenerate
signals, call a model, repeat inference, or perform feature engineering.

**`confidence` is a deterministic signal-alignment score.** It is NOT guaranteed
to represent probability of trade success, expected return, probability of profit,
or calibrated forecast accuracy. No statistical calibration is performed.

| Existing signal | V1 confidence formula | Examples |
| --- | --- | --- |
| BUY | `probability_up` | p=0.60 → 0.60; p=0.75 → 0.75; p=1 → 1 |
| SELL | `1 - probability_up` | p=0.40 → 0.60; p=0.25 → 0.75; p=0 → 1 |
| HOLD | `1 - 2 * abs(probability_up - 0.5)` | p=0.50 → 1; p=0.55/0.45 → 0.90; p=0.59/0.41 → 0.82 |

The score is finite `float64` in **[0,1]**, stored as a decimal such as `0.82`,
never a percentage integer such as `82`. HOLD measures the **strength of model
neutrality**, not the probability of no trade or of an unchanged price. BUY at
p=1, SELL at p=0, and HOLD at p=0.5 all intentionally reach 1: each score measures
alignment with its own decision, not the same probabilistic hypothesis.
BUY/SELL and opposite HOLD probabilities are symmetric within floating-point
precision. The V1 formula is used without rounding or clipping. A very small
positive HOLD probability under thresholds `(sell=0, buy=1)` can round to zero
in float64; zero is valid and is retained, as are low positive scores.

### Thresholds, validation, and immutable decisions

The existing signal is the source of truth. Coherence is checked against
`buy_threshold` and `sell_threshold` from its report, including inclusive BUY and
SELL boundaries and the strict HOLD interval. Defaults remain 0.60 and 0.40.
For custom thresholds 0.70/0.30, p=0.65 is valid HOLD with confidence 0.70.
Thresholds determine the signal but **do not change the confidence formula**.

Malformed inputs raise `ConfidenceScoreError`: invalid probabilities, signals,
thresholds, schema/dtypes, model identities, dates, symbols, duplicate decisions,
or report row/count/metadata mismatches. Thus p=0.80 + HOLD under default thresholds
fails clearly; it is never repaired. Invalid computed confidence also raises.
No row is removed and no value is silently normalized or clipped by this stage.

Output columns and dtypes, in order:

| Column | dtype |
| --- | --- |
| `date` | `datetime64[ns]` |
| `symbol` | `string` |
| `model_name` | `string` |
| `predicted_class` | `int64` |
| `probability_up` | `float64` |
| `signal` | `string` |
| `confidence` | `float64` |

All six input columns, their values, row order, and index are preserved in a new
DataFrame. The unique key remains `(model_name, symbol, date)`. Neither the source
DataFrame nor its report (including the symbols list) is mutated or shared with
the output. `probability_up` remains the raw model output for audit: SELL at
p=0.20 retains probability 0.20 alongside confidence 0.80. `predicted_class`
remains audit-only. Each symbol and row is scored independently.

`ConfidenceScoresReport` retains model name, input/output row counts, BUY/SELL/HOLD
counts, thresholds, symbols and ISO date bounds, and adds `confidence_min`,
`confidence_max`, and `confidence_mean`. Input and output rows and signal counts
match #64. Means describe this batch only: they are not performance metrics and
must not be used to rank, select or promote Logistic Regression versus Random Forest.
Identical input gives identical decisions and report; there is no clock or random state.
Validated numeric report fields are copied to native Python integers/floats for
JSON storage, including when a caller constructs a report with NumPy counters.

### Usage, storage, and Risk Manager handoff

```python
from stocklab_ml.signals import (
    add_confidence_scores, generate_trading_signals, save_confidence_signals,
)

# Existing historical or target-free predictions from either model:
signals = generate_trading_signals(predictions, model_name="my_model_v2")
decisions = add_confidence_scores(signals)
print(decisions.signals)
saved = save_confidence_signals(decisions)
```

Target-free inference requires no historical outcome. Confidence uses only the
current signal and `probability_up`, never `actual_class`, `target_date`, future
close or future return. Historical label columns may be present before #64;
#64 discards them, and #65's exact input schema excludes them. Logistic Regression,
Random Forest and future models use the same formulas without model-specific rules.

`save_confidence_signals(result, signals_path=None, report_path=None,
source_path=None, overwrite=False)` writes ignored
`ml/results/confidence/decisions.csv` and `ml/results/confidence/report.json`.
Defaults are independent of the working directory. CSV contains exactly the seven
columns above, with `index=False`. JSON contains the deterministic report, without
timestamps, secrets, model objects or raw feature matrices. Storage validates both
decisions and report before any write, including formula consistency.
It reuses #64's collision/source-path protection and the existing atomic writer.
Replacement requires `overwrite=True`; pass `source_path` when loading from a file.
Writes are atomic **per file, not as a pair**: a failed report write can leave a
complete CSV. Temporary files are removed on failure.

Each output row is conceptually an ML decision ready for the future #67 Risk
Manager. For example, `{signal: "BUY", confidence: 0.78}` flows to that future
component, which will apply minimum confidence, exposure, cash and position rules.
HOLD confidence is useful for audit and the standard format; a future Risk Manager
will not execute HOLD. **#65 applies no minimum confidence and no risk approval**:
even confidence 0.01 is passed through, and confidence 1 grants no approval.
There is no ACCEPT/REJECT, quantity, sizing, execution, portfolio update, database,
cloud integration, backtest or performance evaluation. No dependency is added.
The confidence calculation and storage are offline and consume zero API credits.

Run the smoke from `ml/`:

```powershell
$env:PYTHONPATH = "src"
python examples/confidence_scores_smoke.py
```

It blocks network connections, verifies AAPL 0.75 → BUY/0.75, MSFT 0.50 → HOLD/1,
NVDA 0.20 → SELL/0.80, checks immutability and finite bounds, then saves the ignored
CSV/report with explicit overwrite. Automated tests also cover both real estimators
fitted on synthetic data and their target-free inference outputs, with model calls
forbidden during confidence scoring.

## ML backtesting engine (#73)

`run_backtest(decisions: ConfidenceSignalsResult, market_data: CleanedDatasetResult,
config=None) -> BacktestResult` simulates one supplied model's historical ML
decisions in one shared fictitious portfolio. It validates the existing seven
decision columns (`date, symbol, model_name, predicted_class, probability_up,
signal, confidence`) and cleaned OHLCV (`date, symbol, open, high, low, close,
volume`) without repairing either input. Every decision must match an observed
session of the same symbol, including HOLD and low-confidence decisions. Missing
symbols/sessions, duplicate keys, invalid schemas/dtypes, and tampered upstream
reports raise `BacktestError`. Confidence validation reuses #65, including its
formula check; execution consumes the supplied signal/confidence unchanged.
Clean OHLCV must retain its sorted symbol/date order and RangeIndex. Decisions
may preserve arbitrary upstream order/index; output is deterministic.

### Timing and period

**A decision using close[t] is available only after close[t]. It cannot execute
at open[t] or close[t]. BUY/SELL execute at the OPEN of the next observed cleaned
session for that same symbol, strictly after the decision date.** Friday's
decision executes Monday open, or Tuesday open if that is the next observed row.
No sessions or prices are synthesized. A terminal BUY/SELL with no next session
is skipped and counted, never executed at its decision close.

The period starts on the earliest decision date, with initial cash at EOD, and
continues through the last observed session among decision symbols. Equity uses
the union of those observed dates in the period; unrelated market symbols do
not extend it. Multiple executions use date ascending, then symbol ascending.
This daily V1 treats observed opens on a calendar date as one opening phase;
intraday sequencing across exchanges/timezones is outside this daily contract.

At the opening phase, held positions use that day's observed OPEN, or their
latest CLOSE strictly before that date if no open exists. Today's high/low/close
and future rows cannot influence sizing. Only after executions does EOD valuation
use the latest observed close <= that date. These carried valuation marks are
as-of prices, without filling or mutating source OHLCV. `actual_class`,
`target_date`, prediction accuracy, future returns, model objects, training,
network, random numbers, and the current clock are never consulted by execution.
Target-free and historical Logistic Regression/Random Forest decisions use the
same engine. Upstream labels are discarded by #64 before confidence/backtesting.
The caller remains responsible for supplying decisions generated causally;
execution timing cannot repair leakage in upstream model training.

### Portfolio policy

`BacktestConfig` is frozen and records explicit Python parameters; it imports
no backend policy or runtime. Defaults mirror the specified paper-risk numbers:

| Parameter | Default |
| --- | --- |
| `initial_capital` | 100000.0 USD-equivalent fictitious capital |
| `minimum_confidence` | 0.70, inclusive for BUY/SELL |
| `max_symbol_exposure` | 0.20 |
| `max_open_positions` | 10 |
| `max_trade_allocation` | 0.10 |
| `allow_short_selling` | False; True is rejected in V1 |
| `quantity_decimals` | 8; configurable integer 0..8 |
| Commission / slippage | 0 / 0 |

Capital must be finite, positive, real and non-boolean. Confidence is in [0,1],
allocation/exposure in (0,1], and the position limit is a positive integer.
HOLD never trades, regardless of confidence. BUY/SELL below minimum confidence
are skipped. Skip priority is HOLD, low confidence, no next session, then the
applicable execution risk checks; each decision has one outcome.

BUY notional is `min(cash, open_portfolio_value * max_trade_allocation,
open_portfolio_value * max_symbol_exposure - held_quantity * execution_open)`.
An existing holding can receive another BUY if exposure/cash permit it. A new
symbol cannot exceed the position-count limit. Fractional quantity is floored
to the configured precision, never rounded upward. The budget/price ratio uses
exact rational arithmetic on decimal representations before flooring, so an
affordable grid unit is not lost to binary rounding. Actual notional is checked
against the budget again after conversion to float64.
If conversion exceeds the budget, sizing moves to the next lower representable
float and reapplies the quantity grid until the amount is affordable.
Zero/too-small quantities are skipped. There is no borrowing or leverage.
Price appreciation can move a holding above its exposure cap; this blocks further
BUYs without forcing a rebalance or SELL.

New holdings use execution open as average cost. Additional BUYs use
`(exact_old_cost_basis + buy_quantity * execution_price) /
(old_quantity + buy_quantity)`. SELL requires an existing position and closes
its entire quantity. Realized P&L is sale notional minus the exact sum of the
position's exact BUY quantity-price products; unrealized P&L is marked value minus that
cost basis. This implements the algebraic `(sell_price - average_cost) * quantity`
definition using exact products, preserving even representable subnormal
cash gains/losses and avoiding a premature price-delta underflow. BUY has
nullable/NaN realized P&L. There are no shorts, synthetic sales, spread,
commissions, slippage, or forced final liquidation. Open positions remain marked
at the last as-of close. Arithmetic that cannot remain finite raises clearly.
Quantity accumulation uses exact decimal ratios and rejects any sum that
cannot be preserved in the published float64 quantity. Average cost is derived
from the exact accumulated cost basis divided by that quantity; a rounded
previous average never feeds the next calculation.

Quantity-price products, cash, cost basis, holding marks, opening portfolio
sums, risk products, remaining-exposure subtraction, and the budget/price
quotient retain exact rational values throughout simulation and replay.
Realized P&L is exact sale proceeds minus exact cost basis; unrealized P&L is
exact marked value minus that basis. Multiple lots at one unchanged price
therefore remain flat. Only presentation fields convert to float64, with both
exact and published trade notionals checked against the exact BUY budget.
Equity aggregates exact cash and all exact position values before rounding;
adding independently rounded output columns can differ at precision limits.
Replay reconstructs exact products from ledger quantities/prices and snapshot
marks, never feeding rounded amount, cash, market-value, or average-cost fields
back into accounting. For example, 0.30 cash buys exactly three shares at 0.10,
leaving zero cash; replay uses the same rules.

Representable tiny gains/losses remain nonzero, including subnormal values.
If nonzero exact realized or unrealized P&L would round to zero, the engine
raises `BacktestError` rather than silently classifying it as flat. Overflow and
unrepresentable accumulated quantities or positive SELL notionals that round to
zero also raise `BacktestError`. Return and
drawdown are reporting metrics derived from the published equity curve; changes
smaller than its float64 resolution may not appear in those metrics.

### Results and metrics

`BacktestResult` and `BacktestReport` are frozen dataclasses. Output tables are
independent pandas DataFrames with stable columns/dtypes, including empty tables:

| Table | Columns, in order |
| --- | --- |
| `trades` | `decision_date, execution_date, symbol, model_name, signal, confidence, quantity, execution_price, total_amount, cash_after, position_quantity_after, average_cost_after, realized_pnl, portfolio_value_before` |
| `positions_history` | `date, symbol, quantity, average_cost, mark_price, market_value, unrealized_pnl, opening_mark_price` |
| `equity_curve` | `date, cash, positions_value, total_value, drawdown` |

Historical positions contain EOD snapshots of every open holding, without rows
for dates with no holdings. The equity curve separately records those cash-only
dates and anchors drawdown to initial capital before the first execution.
The ledger's `portfolio_value_before` records the opening/as-of value used for
risk limits, so storage can check BUY allocation/exposure against reported policy.
Each position snapshot also retains `opening_mark_price`, the same day's open
or strictly earlier as-of close used for risk. Storage replays opening value
from every held position; holdings sold that day use their ledger execution
price. This prevents a self-declared opening portfolio value from bypassing
allocation/exposure checks while preserving the separate EOD close mark.

- Final value = final cash + final marked positions value.
- Total return = `(final_value / initial_capital) - 1`, as a decimal fraction.
- Win rate = SELL closes with realized P&L > 0 / executed SELL closes; 0.0 when
  none closed. Break-even is not a win. BUYs and still-open positions are excluded.
- Drawdown = `(running_equity_peak - total_value) / running_equity_peak`, a
  nonnegative magnitude. Max drawdown is its maximum over portfolio EOD equity,
  including the initial capital reference; it is not a prediction metric.

The JSON-compatible report includes these metrics, model identity, symbols,
period, full configuration, signal thresholds, execution timing, zero costs,
decision/trade/win/loss counts, final open-position count, and skip counts:
`skipped_low_confidence`, `skipped_no_position`, `skipped_max_positions`,
`skipped_exposure`, `skipped_buying_power`, `no_next_session`.

### Usage and safe local storage

```python
from stocklab_ml.backtesting import BacktestConfig, run_backtest, save_backtest_result

# Existing validated #65 decisions and #58 clean data, one model per run:
result = run_backtest(decisions, market_data, config=BacktestConfig())
print(result.report.final_value, result.report.total_return)
saved = save_backtest_result(result)  # overwrite=False
```

`save_backtest_result(result, output_dir=None, source_paths=(), overwrite=False)`
writes ignored `ml/results/backtesting/{trades.csv,equity_curve.csv,positions.csv,
report.json}`, independent of the working directory. CSV has no artificial
index; JSON uses plain scalars with no NaN, timestamp, secret, model, or raw input
dataset. Before any write, storage validates schemas, timing, ledger cash/cost/P&L,
historical position accounting, EOD equity/drawdown, report metrics, and decision
count reconciliation. This checks internal accounting consistency; verifying
price/decision authenticity still requires the original sources.

All destination collisions are checked first. Replacing outputs requires
`overwrite=True`. Pass all input CSV/JSON `source_paths` when using disk inputs
to protect them from output aliasing, including symlinks and hard links. Writes
reuse the existing atomic writer and clean temporary files: publication is
atomic **per file, not across the four files**. Failure on a later file may leave
earlier complete artifacts and never returns a successful `SavedBacktest`.

Run the assert-based smoke from `ml/` using Python 3.12 and the existing venv:

```powershell
$env:PYTHONPATH = "src"
python examples/backtesting_smoke.py
```

It blocks network connections, exercises BUY/HOLD/SELL over four observed sessions,
checks accounting/determinism/immutability, saves ignored outputs with explicit
overwrite, and prints metrics. Synthetic returns validate mechanics only and
provide no evidence of real profitability. No dependencies or API credits are
added. No backend/frontend/cloud is involved. Model versioning is a separate
explicit stage (#75 below); the expanded test issue (#92) remains deferred.

## Buy & Hold benchmark comparison (#74)

`compare_to_buy_and_hold(backtest, market_data)` consumes an existing validated
`BacktestResult` from #73 and the **same supplied `CleanedDatasetResult`**. It
never reruns the AI strategy, trains a model, infers predictions, downloads data,
or accesses a clock. Logistic Regression and Random Forest use the same API;
model identity is metadata. Both inputs remain unchanged.

The comparison preserves `backtest.report.initial_capital`, `start_date`,
`end_date`, and the complete `symbols` universe exactly, including symbols the
AI never traded. Benchmark equity dates equal `backtest.equity_curve.date` in
count, order, and endpoints. Additional source symbols and observations outside
the comparison period are ignored. Every report symbol must exist in the input;
missing symbols fail. Daily OHLCV and the cleaning report use #73's validators.
Within-period sessions must match AI equity dates; recorded AI execution opens
and historical opening/EOD marks must agree with supplied prices. #73 does not
retain all original OHLCV: these checks reconcile its observable price evidence,
not a cryptographic proof that unrecorded source observations are identical.
Callers remain responsible for supplying the original cleaned dataset.

### Reference strategy and causal timing

- One shared capital pool, with a fixed **equal-weight** tranche
  `initial_capital / N` for each report symbol. Tranches are computed as exact
  rational values; published float64 targets can round when N does not divide
  the capital evenly. Accounting continues to use exact tranches and notionals.
  Published amounts and metrics follow float64 resolution; extremely small
  subnormal tranche amounts can round to zero without changing exact cash accounting.
- Capital begins entirely in cash. Each symbol buys at most once, at its first
  observed session **OPEN strictly after `start_date` and <= `end_date`**.
  A decision available after the start day's close cannot execute at that day's
  open. Friday-to-Monday and other gaps follow observed sessions, with no
  synthetic calendar or start-day opening advantage.
- Fractional shares are floored to **8 decimal places**, using #73's sizing
  helper. Neither exact nor published invested amounts may exceed the tranche.
  A tranche too small for one quantity unit remains cash with a null entry.
- A symbol with no eligible next session stays uninvested. Its full tranche is
  retained as cash and **never redistributed**. Quantity-rounding residuals stay
  cash too. Cash earns zero interest, and nothing is reinvested.
- Hold the initial quantities without rebalancing, tactical SELLs, stops,
  confidence filters, AI trade/exposure limits, or final forced liquidation.
- EOD value is cash plus holdings at each symbol's latest observed **CLOSE <=
  equity date**. Entry-day EOD uses that day's close. Missing sessions use the
  earlier as-of close without modifying/imputing source OHLCV. No future close
  enters historical marks; final holdings use the latest close <= `end_date`.
- Commission and slippage are zero, matching #73. No spread, fees, taxes, or
  dividend payments are added. `dividends_included=False`: this is a
  **price-return** benchmark, not a dividend total-return series. Split handling
  is inherited from supplied cleaned prices; there is no second adjustment.

### Outputs and metrics

The frozen `BenchmarkComparisonResult` contains independent tables and frozen
`BuyAndHoldReport` / `BenchmarkComparisonReport` dataclasses:

| Table | Columns, in order |
| --- | --- |
| `benchmark_allocations` | `symbol, target_allocation, entry_date, entry_price, quantity, invested_amount, residual_cash, final_mark_date, final_mark_price, final_market_value, component_final_value, component_return` |
| `benchmark_equity_curve` | `date, cash, positions_value, total_value, drawdown` |
| `benchmark_positions_history` | `date, symbol, quantity, mark_date, mark_price, market_value` |

The position history records every held symbol on each equity date, including
the source date of each as-of mark. It allows daily equity reconciliation before
storage. Uninvested allocation rows have null entry/final-mark fields, zero
quantity/investment/market value/return, and their whole tranche as residual cash.

- Benchmark final value = exact remaining cash + exact marked holdings,
  published once as float64. Benchmark return = `final_value / initial_capital - 1`.
- Component final value = tranche residual + final market value; component return
  = `component_final_value / target_allocation - 1`, computed with exact tranches.
  Portfolio return uses the shared portfolio capital, never invested capital.
- AI final value, total return, and max drawdown come directly from #73's validated
  report. `return_delta = ai_total_return - benchmark_total_return` and
  `final_value_delta = ai_final_value - benchmark_final_value`. Returns/deltas are
  decimal fractions: 0.04 means four percentage points.
- Benchmark drawdown uses #73's nonnegative `(running_peak - total_value) /
  running_peak` convention, anchored to initial capital. Maximum drawdown is the
  maximum over benchmark equity. No Sharpe, Sortino, alpha/beta, winner, ranking,
  or promotion decision is produced. #75 model versioning is a separate explicit
  stage below; #78 model promotion remains future work. These mechanics make no profitability claim.

### Local storage and offline smoke

```python
from stocklab_ml.backtesting import compare_to_buy_and_hold, save_benchmark_comparison

comparison = compare_to_buy_and_hold(backtest, market_data)
print(comparison.comparison_report.return_delta)
saved = save_benchmark_comparison(comparison, source_paths=(cleaned_csv, ai_report_json))
```

`save_benchmark_comparison(result, output_dir=None, source_paths=(), overwrite=False)`
defaults to Git-ignored `ml/results/benchmark/`, independently of working directory:

- `buy_and_hold_allocations.csv`
- `buy_and_hold_equity_curve.csv`
- `buy_and_hold_positions.csv` (daily reconciliation audit)
- `comparison.json`, containing both reports and their timing/cost/dividend assumptions

CSV has no index and stable columns. JSON contains plain finite JSON types.
Before writing, `validate_benchmark_comparison` checks allocations and fractional
floor, cash conservation, every historical holding/mark, equity/drawdown, report
metrics, exact dates/universe/capital, and comparison deltas. As with #73,
persistence validation checks internal consistency; source-price authenticity
requires original inputs. DataFrames remain mutable even inside frozen results,
so validation runs again on each save.

All output collisions are checked before publication. Explicit `overwrite=True`
is required to replace outputs. Pass every input artifact path in `source_paths`
to protect sources from direct, symlink, and hardlink aliases. Existing atomic
writers publish **per file**; the four-file set is not transactional. A late
failure may leave earlier complete files and returns no successful saved result.

From `ml/`, with the existing Python 3.12 environment:

```powershell
$env:PYTHONPATH = "src"
python examples/benchmark_comparison_smoke.py
```

This deterministic two-symbol synthetic smoke runs actual #73 backtesting, then
compares equal-weight Buy & Hold with the same 100000 capital, verifies causal
entries, accounting, immutability, and repeated results, and saves local outputs.
It blocks network connections and uses zero market API calls or credits.
Synthetic returns demonstrate mechanics only, not real market outperformance.

## Model version registry (#75)

`stocklab_ml.versioning` records existing `LogisticRegressionResult` and
`RandomForestResult` objects in one local `ModelVersionRegistry`. Registration
never calls fit, changes training/splits/features, registers RF's Logistic
comparator implicitly, or selects a winner. Existing #61–#65 DataFrame schemas
and #73/#74 APIs remain unchanged and work without versioning.

### Identity and immutable training metadata

The permanent key is **(ModelName, ModelVersion)**. `model_name` comes from the
real training report: currently `logistic_regression_baseline` or
`random_forest_baseline`. ModelName is distinct from the version and follows
#64's case-sensitive 1–128 ASCII-character convention: initial letter/digit,
then letters/digits/underscores/dots/hyphens, with no whitespace or path separators.
The current training reports must retain their canonical family names and types.

Each family has its own positive `version_number`, allocated as max + 1:
`1 -> v000001`, `2 -> v000002`. The representation is `v` followed by at least
six decimal digits, padded only as necessary (`1000000 -> v1000000`). Exact
canonical versions are required; `latest`, `current`, padded input and unknown
versions never resolve to an Active or newer version. Archived numbers remain
reserved. There is no delete, counter reset, or repair API.

`register_version(result, training_run_id=..., trained_at_utc=...)` returns a
deeply immutable `ModelVersionRecord`. The caller supplies a nonzero canonical
lowercase UUID **TrainingRunId for each real training run** and retains it for
retries. Identical canonical metadata and the same UUID return the existing
record, including its current status, without a write or counter increment.
Changed metadata with the same UUID raises `TrainingRunConflictError`; a new
UUID creates a new version even when all metrics/configuration/timestamps match.
The SHA-256 of the ordered historical predictions also detects changed holdout
rows/probabilities on a retry; it is not an estimator/artifact fingerprint.

`trained_at_utc` is an explicit timezone-aware UTC `datetime` declaring actual
training completion. It is never inferred from the current date or `train_end`.
`registered_at_utc` is the registry's creation time; `ModelVersionRegistry(...,
clock=...)` permits deterministic injection. Registration cannot precede declared
training completion. `record.training_date` derives its UTC calendar date.

The JSON record stores the **entire real training report** under `training_report`,
without a binary estimator, source path or secrets. It retains:

- Ordered `feature_columns`: `return_1d, ma_5, ma_20, rsi_14, volume,
  momentum_10, volatility_20`.
- `target_name=target_up_1d`, `target_definition`, `prediction_timing`, `split_rule`.
- Original dataset counts and split metadata (`train_start`, `train_end`,
  `test_start`, `test_end`, `split_date`, fraction, purge counts, class rates,
  and per-symbol row counts), without creating a new partition.
- Actual JSON-safe `hyperparameters` from the report, checked against the supplied
  estimator; original convergence/scaler or RF importances/comparison metadata.
- `model_type`: `logistic_regression` or `random_forest`, consistently with the
  result/report/estimator and family. One ModelName cannot change model type.

Record properties expose `feature_columns` as a tuple, `hyperparameters` as a
read-only mapping, `target_name`, `train_start`, `train_end`, `test_start`,
`test_end`, and `split_date`. `to_dict()` returns a detached JSON-safe snapshot.
Nested reports and metrics cannot be updated through the public contract.

Metrics are the existing #63 `ModelEvaluationMetrics`, obtained by
`evaluate_predictions(model_name=..., predictions=result.predictions)` on the
original historical holdout only: accuracy, precision, recall, F1, TP/TN/FP/FN,
positive/negative support and total rows. Finite [0,1] metrics, integer counts,
support/count reconciliation, holdout boundaries and exact training-report
`test_accuracy` are validated. Persisted metrics are also checked against their
confusion counts with sklearn's weighted metric functions, without new formulas.
No future labels, ranking, tuning or performance-based activation is introduced.

### Status and exact reads

`ModelStatus` has exactly `Active` and `Archived`. New records are **Archived**:
in V1 this means *registered and non-active*, not deleted or necessarily poor.
Registration never replaces an Active version, even with better metrics.

`list_versions(model_name)` returns immutable records by `version_number ASC`.
`get_version(model_name, model_version)` returns an exact record or raises
`ModelVersionNotFoundError`. `get_active_version(model_name)` returns the one
Active version or `None`, never the latest registered version as a fallback.

`set_active_version(model_name, model_version)` explicitly activates an existing
version and archives the old Active version in one atomic registry transaction.
At most one version is Active **per family**; Logistic and RF statuses are
independent. This is an administrative primitive, not a global production model
selection policy. `archive_version(...)` preserves the record and may leave the
family with zero Active versions; it does not choose a replacement. Only status
changes: training date/configuration/features/metrics/identity remain immutable.
Active describes metadata status; it does not mean a binary model is deployed or
available after restart.

### Persistence, concurrency and failures

Default metadata path: **`ml/models/registry.json`**, independent of working
directory. JSON has exactly `schema_version: 1` and `models: [...]`. A new
instance reloads metadata, statuses and version counters without retraining.
The absent file starts an empty registry; an existing invalid/unreadable file
raises `ModelVersionError` and is never silently reset or repaired. Duplicate
JSON keys, model/version keys or TrainingRunIds, gaps in version history, invalid
dates/metrics/statuses and multiple Active versions in a family are rejected.

Every mutation holds a sibling **`registry.json.lock`** created exclusively
across the complete read/validate/modify/write operation. Contention waits up to
five seconds, then raises `RegistryBusyError`; the caller can retry explicitly.
Resolved paths share a lock; hard-linked registry aliases are rejected. This is
a local filesystem guarantee, not a distributed/cloud lock.

The existing atomic writer creates a temporary sibling, flushes, fsyncs and uses
`os.replace`. Failed publication leaves the previous registry intact and returns
no successful record. Status transitions use the same transaction. A crash after
publication but before the caller receives success can be retried with the same
TrainingRunId. A killed process can leave a temporary file or lock: **verify that
no writer is alive before manually removing its stale lock**. There is no timeout
based lock stealing or automatic repair. Filesystem/power-loss durability follows
the local filesystem; there is no cloud or distributed transaction guarantee.

### Versioned predictions and confidence decisions

`registry.bind_version(result, model_name=..., model_version=...)` returns a
`RegisteredModel` context only after matching the existing result/report/type,
hyperparameters, metrics and holdout digest to that exact registered record.
Wrong families, changed results and unknown versions fail; no Active lookup is
used to assign historical lineage. `context.historical_predictions()` returns
`VersionedPredictions` with all original holdout rows. `context.predict(features)`
calls the existing appropriate inference API on the supplied in-memory estimator,
requiring no `actual_class`, `target_date`, future close or future label.

`VersionedPredictions.predictions` returns a deep copy in the unchanged #61/#62
schema, preserving values, dates, order and original index. `to_dataframe()` adds
explicit `model_name,model_version` after `symbol`; historical `target_date` and
`actual_class` remain when present. Pass `.predictions` to #64, which discards
outcome columns as before. Thresholds stay BUY >= .60, SELL <= .40, HOLD between.

`version_confidence_decisions(confidence_result, predictions=versioned_predictions)`
returns `VersionedConfidenceDecisions`, after validating #65 and matching every
decision's date/symbol/class/probability and model name to the bound batch. It
neither regenerates signals nor changes probabilities/confidence. `.result`
returns a detached unchanged seven-column `ConfidenceSignalsResult` suitable
for existing consumers; `.to_dataframe()` adds `model_version` after `model_name`.
BUY uses p, SELL uses 1-p, HOLD uses 1-2*abs(p-.5), as before. Row order and index
of the supplied decision result remain unchanged.

Each exported row includes exact explicit `model_name` and `model_version`, ready
for later mapping to backend #69 `AiDecision.ModelName/ModelVersion`. No backend
code changes. Activating v2 never relabels predictions/decisions produced by v1;
Archived versions and their historical exports remain available.

**V1 provenance limit:** this is a controlled local association with an in-memory
result, not cryptographic proof of the estimator's fitted weights. Metadata and
holdout checks cannot detect every in-memory estimator mutation or establish a
binary artifact's origin. #76 must add verified artifact storage/fingerprints;
no artifact is serialized/loaded here. Registry and wrappers should be used
through their public APIs, not by modifying private Python fields or JSON files.

`save_versioned_predictions(..., batch_id=...)` and
`save_versioned_decisions(..., batch_id=...)` use a caller-supplied canonical UUID
per output batch. Defaults (`overwrite=False`) are:

```text
ml/results/versioned_predictions/<model_name>/<model_version>/<batch_id>/predictions.csv
ml/results/versioned_predictions/<model_name>/<model_version>/<batch_id>/metadata.json
ml/results/versioned_decisions/<model_name>/<model_version>/<batch_id>/decisions.csv
ml/results/versioned_decisions/<model_name>/<model_version>/<batch_id>/metadata.json
```

Different batches of one version get different UUIDs; existing batches are not
automatically overwritten. CSV omits an artificial index, as existing storage
does. Both CSV and JSON retain exact model identity. JSON includes schema, batch
UUID, row counts, dates, symbols, data SHA-256, immutable model metadata snapshot
and the original confidence report for decisions. Snapshot status is historical,
not a query for current Active status. Before writing, storage revalidates the
registered reference, schemas, nulls, dates, counts, reports and batch integrity.
Registry and known feature-file sources are protected against output aliasing.
Explicit custom `output_dir` must be controlled, local and Git-ignored.

Exports reuse atomic **per-file** publication; CSV/JSON are not a two-file
transaction. A metadata failure may leave a complete CSV and returns no success.
For interrupted exports, inspect both files and their data SHA-256 before an
explicit `overwrite=True` retry, or use a new batch UUID. Only a successful
`SavedVersionedOutput` denotes completion. Existing #64/#65 output files are not
rewritten. Default registry, lock, results, temporary files and smoke outputs are
already Git-ignored. Never put credentials in model metadata.

### Minimal local workflow

From the Python session launched in `ml/src/` as in the existing model examples,
using an already prepared #60 feature CSV:

```python
from datetime import UTC, datetime
from uuid import uuid4
from stocklab_ml.modeling import load_feature_csv, train_random_forest
from stocklab_ml.signals import generate_trading_signals, add_confidence_scores
from stocklab_ml.versioning import (
    ModelVersionRegistry, save_versioned_predictions, save_versioned_decisions,
    version_confidence_decisions,
)

features = load_feature_csv("../data/processed/features/AAPL_1day_2016-01-01_2026-09-18.csv")
forest = train_random_forest(features)
trained_at = datetime.now(UTC)  # Capture actual completion here, not during a later retry.
training_run_id = str(uuid4())  # Retain this UUID and trained_at for retries of this run.
registry = ModelVersionRegistry()
record = registry.register_version(
    forest, training_run_id=training_run_id, trained_at_utc=trained_at,
)
context = registry.bind_version(
    forest, model_name=record.model_name, model_version=record.model_version,
)
predictions = context.historical_predictions()
signals = generate_trading_signals(predictions.predictions, model_name=record.model_name)
confidence = add_confidence_scores(signals)
decisions = version_confidence_decisions(confidence, predictions=predictions)
batch_id = str(uuid4())
saved_predictions = save_versioned_predictions(predictions, batch_id=batch_id)
saved_decisions = save_versioned_decisions(decisions, batch_id=batch_id)
inference = context.predict(features.tail(1))  # Target-free, same exact registered identity.
print(inference.to_dataframe())
print(ModelVersionRegistry().get_version(record.model_name, record.model_version).metrics)
registry.set_active_version(record.model_name, record.model_version)  # Explicit administrative choice.
```

For an illustrative identity only: `random_forest_baseline / v000001`, trained
`2026-10-14T14:00:00Z`, initial status `Archived`. An illustrative decision row
could be `2026-10-14,AAPL,random_forest_baseline,v000001,BUY,0.82` (selected
date/symbol/name/version/signal/confidence fields). These are **not measured
StockLab performance**.

An executable assert-based synthetic workflow requires no local market dataset:
from `ml/`, set `PYTHONPATH=src` and run `python examples/model_versioning_smoke.py`.
It blocks network connections, trains both real estimators, registers only the
explicit runs, checks restart/idempotency/history and saves ignored batches.
Offline tests use only synthetic/local data: zero Twelve Data, Alpha Vantage,
AWS or Azure calls and zero API credits. No dependencies are added.

#75 remains metadata/versioning/traceability only; binary persistence is owned
by #76 below. **#77** owns automatic retraining and **#78** owns promotion.
#77 is implemented below; #78 and the separate #92 work remain deferred.

## Model artifact storage / AWS S3 (#76)

`stocklab_ml.artifacts` saves the complete fitted Logistic Regression
`Pipeline(StandardScaler, LogisticRegression)` or `RandomForestClassifier`
(including its 300 trees). It reuses #75's `RegisteredModel`, permanent
`(model_name, model_version)` identity and `training_run_id`. Save requires
`registry.bind_version(...)`; the result/report/holdout, estimator family,
features, classes and parameters are validated before serialization. There is
one registry: its schema and training metadata are unchanged. Storage neither
trains nor activates, archives, promotes or executes a trade.

### Manifest, integrity and compatibility

Format: joblib, pickle protocol 5, no compression. Direct dependencies
[joblib 1.6.0](https://pypi.org/project/joblib/1.6.0/) and
[boto3 1.43.113](https://pypi.org/project/boto3/1.43.113/) are explicitly declared;
boto3 requires Python >=3.10 and supports our Python 3.12 environment.

Each schema-v1 `manifest.json` contains `model_name`, `model_version`,
`training_run_id`, `model_type`, `serialization_format`, `artifact_filename`,
`artifact_sha256`, `artifact_size_bytes`, `environment`, a complete
`model_record` snapshot and `metadata_sha256`. The latter hashes #75's canonical
immutable record fields, excluding only administrative `status`. Snapshot status
is historical; it never changes current registry status. `holdout_sha256`
identifies historical predictions and is **not** the binary artifact hash.

The artifact digest covers its exact serialized bytes. Size and SHA-256 are
verified before every load; loading uses those same in-memory bytes to avoid
reopening a changed file. The S3 ETag is never used as a SHA-256 substitute.
Malformed/duplicate JSON keys, extra/missing fields, inconsistent identities,
metadata, filenames, digests and sizes fail. S3 keys are derived internally;
manifests cannot supply arbitrary object keys. Download/load defaults to a
configurable **100 MiB** artifact limit and a **1 MiB** manifest limit. Reads
are bounded, close response bodies, and reject partial content. This V1 uses
single `PutObject` requests, not multipart upload.

`environment` records Python, scikit-learn, NumPy, SciPy and joblib versions.
Loading requires the recorded Python major/minor and **exact** package versions;
Python patch differences are allowed. sklearn's inconsistent-version warning
becomes a controlled compatibility error. After load, fitted state, exact
feature/class order, hyperparameters, scaler statistics, iterations, tree count
and feature importances are checked. Nothing is refitted to repair a mismatch.
See the official [scikit-learn persistence guidance](https://scikit-learn.org/stable/model_persistence.html).

### Trusted loading and registry availability

**joblib/pickle can execute arbitrary code.** `trusted_source=True` must be
supplied to `load_model_artifact` after independently approving the private
source and local directory. No user URL or public artifact-loading API exists.
SHA-256 detects a mismatch with the manifest; it does **not** authenticate an
attacker who can replace both files. IAM, bucket policies and controlled local
write access are the provenance boundary. This V1 does not implement signing,
authenticate fitted weights against #75's metadata, or sandbox deserialization.

With a local registry, all immutable metadata must match `get_version` exactly;
only current status can differ. A missing version, corrupt registry or metadata
conflict cannot be bypassed with cold-start opt-in. By default the usual local
registry is used. On a new machine with **no default local registry**, callers
can explicitly choose `allow_without_registry=True` for download and load from
an approved source. The fully validated manifest record is then a historical
snapshot, including its original status; no registry/history or Active status
is created. If a registry is explicitly passed, it must contain that version.
Cold start relies on the trusted source, not independent local provenance.

### Local publication and recovery

Default paths, already Git-ignored:

```text
ml/models/artifacts/<model_name>/<model_version>/model.joblib
ml/models/artifacts/<model_name>/<model_version>/manifest.json
```

Paths use #75's exact name/version validators and remain inside the selected
directory. Registry and known training-file aliases are protected. Each file
uses temporary sibling / flush / fsync / atomic no-clobber publication on
NTFS/POSIX. The binary is published first, the manifest last. **The pair is not
a transaction.** A late failure returns no successful artifact and can leave
an orphan binary; a missing manifest never authorizes a load. Inspect interrupted
files before retrying `save_model_artifact(..., overwrite=True)`: this flag only
permits reuse of identical verified content or completion of an identical orphan.
It never replaces a version's different binary or manifest. Corrupt existing
files fail without automatic deletion/repair. A killed process can leave an
ignored temporary sibling; remove it manually only after confirming no writer
is alive. Unsupported filesystems fail safely without a weaker fallback.

`overwrite=False` rejects any existing output. Download may reuse a complete
cache only after comparing its validated manifest and recomputing binary size
and SHA-256. Different versions have distinct directories. Keep any custom
output directories controlled and Git-ignored; never commit binary models or
generated registries.

### Private S3 configuration, publication and recovery

Use an **existing, explicitly approved DEV general-purpose bucket** and region:

```powershell
$env:STOCKLAB_ML_S3_BUCKET = "your-existing-private-dev-bucket"
$env:STOCKLAB_ML_AWS_REGION = "ca-central-1"
$env:STOCKLAB_ML_S3_PREFIX = "dev/stocklab"
```

All three variables are required by `S3ArtifactConfig.from_environment`.
Bucket names, region syntax and relative prefix segments are validated; no
slashes/traversal in identities, empty prefix segments, absolute keys or URLs.
`trusted_private_source=True` is an explicit caller acknowledgment that this
bucket/prefix is approved and private. The code does not inspect or change bucket
policies. An injected S3 client is for controlled offline tests (or equivalent
trusted SDK usage); no endpoint URL option is exposed. The real SDK client uses
HTTPS, signed requests, ignores configured endpoint overrides, and has bounded
standard retries (three total attempts), five-second connect and 30-second read
timeouts. Missing/invalid configuration fails; there is no alternate bucket.

Credentials use the official [boto3 provider chain](https://docs.aws.amazon.com/boto3/latest/guide/credentials.html):
AWS profile / environment / IAM role / temporary credentials. No AWS keys are
hardcoded, printed, stored in manifests or required for offline tests. No
credentials or AWS connection is needed when using an injected fake client.
The code never creates buckets, access keys, IAM policies or AWS services.

```text
<prefix>/models/logistic_regression_baseline/<version>/artifacts/<sha256>.joblib
<prefix>/models/random_forest_baseline/<version>/artifacts/<sha256>.joblib
<prefix>/models/<model_name>/<version>/manifest.json
```

Upload validates the registered local manifest and exact binary bytes. If a
manifest already exists, an identical canonical manifest and verified remote
binary return the same reference; different content conflicts. Otherwise it
uploads the content-addressed binary with `IfNoneMatch="*"`, downloads it to
verify size/SHA-256, then conditionally publishes the manifest **last** and reads
it back. [S3 conditional writes](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes.html)
protect concurrent writers: 412 reads/verifies the winner; different manifests
conflict. A 409 returns a typed conflict for explicit retry; no infinite retry
or HEAD/PUT pseudo-lock. A failed manifest upload leaves an unpublished orphan
binary. An identical retry verifies that binary and can finish publication.
Concurrent different uploads can leave a losing orphan, but only one manifest
publishes a version. No cleanup deletes or changes other versions.

Download requests the exact manifest from the approved bucket/prefix, validates
identity/metadata/runtime, derives the digest key, retrieves bounded bytes,
checks size/SHA-256 and publishes the local cache. It never unpickles during
download. There is no `latest`/Active fallback or implicit new version.
Administrative deletion/overwrite outside this API can break immutability;
prevent it through separately managed bucket policy and writer permissions.

Enable S3 Block Public Access separately; no public ACLs or unauthenticated URLs
are emitted. Grant only `s3:GetObject` and `s3:PutObject` on the exact artifact
prefix. `HeadObject` uses `s3:GetObject`; this implementation uses no HEAD or LIST
and requires no `s3:ListBucket`, delete or ACL permission. Without ListBucket,
[AWS returns 403 for an absent key](https://docs.aws.amazon.com/AmazonS3/latest/API/API_GetObject.html).
Initial upload treats this discovery result as unknown and proceeds only through
conditional creation; it never overwrites an unreadable existing manifest.
Successful binary and final-manifest reads remain mandatory. Actual read/write
denial still returns a controlled error; a missing download may therefore report
AccessDenied rather than NotFound when listing is not permitted.
Uploads inherit the bucket's server-side encryption (SSE-S3 or SSE-KMS). For
[SSE-KMS](https://docs.aws.amazon.com/AmazonS3/latest/userguide/UsingKMSEncryption.html),
grant `kms:GenerateDataKey` for writes and `kms:Decrypt` for reads on the selected
key, including its key policy. No IAM or encryption policy is modified here.
S3 storage, requests, transfer and KMS can incur costs; remote upload verification
adds a GET. No cost is claimed without a live measurement.

Errors are controlled: `ModelArtifactError`, `ArtifactNotFoundError`,
`ArtifactConflictError`, `ArtifactIntegrityError`, `ArtifactCompatibilityError`,
`ArtifactStorageError`. Missing credentials, AccessDenied, missing bucket/key,
wrong region, timeout, throttling and connection failure never report success
or fallback. Public error messages omit raw AWS responses and secrets; technical
causes are retained internally. Avoid logging full chained tracebacks containing
SDK response/connection details.

### Save, upload, download, load and predict

Using the existing trained result and registry (training itself is unchanged):

```python
from stocklab_ml.artifacts import (
    S3ArtifactConfig, S3ModelArtifactStore, save_model_artifact, load_model_artifact,
)

context = registry.bind_version(result, model_name=record.model_name, model_version=record.model_version)
local = save_model_artifact(context)
store = S3ModelArtifactStore(S3ArtifactConfig.from_environment(trusted_private_source=True))
reference = store.upload_model_artifact(local, registry=registry)
del context, result  # The trained estimator is no longer needed.
downloaded = store.download_model_artifact(record.model_name, record.model_version, registry=registry)
loaded = load_model_artifact(downloaded, registry=registry, trusted_source=True)
versioned = loaded.predict(new_feature_rows)  # No actual_class, target_date or fit required.
predictions = versioned.predictions  # Unchanged #61/#62 columns for #64 signals.
export_rows = versioned.to_dataframe()  # Exact model_name and model_version.
```

`LoadedModel` exposes the fitted `estimator`, exact `record`, `artifact_sha256`
and local `artifact_reference`; it is not a fabricated `RegisteredModel` training
result. `.predict()` reuses existing inference and returns #75
`VersionedPredictions`. Existing `version_confidence_decisions`, versioned
prediction/decision exports and #64/#65 thresholds/confidence formulas work
unchanged. Archived versions remain usable without promotion.

### Offline verification and live AWS limitation

```powershell
# Repository root, Python 3.12 environment:
.\ml\.venv\Scripts\python.exe -m pip check
.\ml\.venv\Scripts\python.exe -m pytest ml -q
# From ml/, with its venv active:
python -m pytest -q
$env:PYTHONPATH = "src"
python examples/model_artifact_storage_smoke.py
```

The assert-based smoke trains both real synthetic baselines, registers/saves,
uploads into fake S3, discards the original estimators, disables fit/training,
downloads/loads and compares exact target-free predictions and versioned
decisions. It blocks sockets and uses zero live AWS/market API calls. Tests
exercise tampering, runtime conflicts, cache separation, duplicate/concurrent/
partial publication, AWS failures and the real SDK request model via Stubber.
Synthetic data demonstrates mechanics, never profitability.

Live AWS validation is optional and requires an existing approved DEV bucket,
isolated DEV prefix, region and credentials. No such configuration is provided
by tests or created automatically. Without it report:
**Live AWS validation not performed — secure configuration unavailable.**
Automatic retraining #77 is documented below; promotion #78, metrics #79, API #80,
frontend #59 and #92 remain deferred. ML = WHAT; Risk Manager = WHETHER + HOW MUCH; Paper Trading
Engine = EXECUTE. Storage performs none of the latter two responsibilities.

## Automatic model retraining (#77)

`stocklab_ml.retraining.run_retraining_check(config, *, dependencies, now_utc=None,
retry_run_id=None, trigger_source="manual")` performs **one check**. It has no loop,
background thread, import-time training or trading integration. An external scheduler
invokes it approximately every three days. This refreshes the training sample as new
observed sessions arrive; it makes no claim that a candidate is better than Active.
Every new #75 version is **Archived**. The runner never calls `set_active_version`
or `archive_version`, compares candidates with Active, selects a winner, backtests,
changes thresholds or executes trades. Promotion belongs exclusively to **#78**.

### Configuration and explicit bootstrap

`RetrainingConfig` is frozen. Explicit `symbols` and `dataset_path` are required;
there is no inferred example ticker. `model_families` selects `logistic_regression`,
`random_forest`, or both. Defaults: `enabled=True`, `check_interval_days=3`,
`minimum_new_sessions=1`, `test_fraction=0.20`, `overlap_days=7`,
`maximum_refresh_days=90`, `aws_enabled=False`, `upload_to_s3=False`, `dry_run=False`.
The minimum counts actual new sessions in **at least one configured symbol**, not
elapsed calendar days. Other symbols can have unchanged or missing sessions; none
are fabricated. The shared pooled model retains each row's actual symbol identity.

Default outputs: `ml/models/retraining/state.json`, the existing
`ml/models/registry.json`, and `ml/models/artifacts/`. Symbols/families, flags,
fractions, intervals, canonical UUIDs, timestamps and paths are validated.
Data/journal/registry/lock aliases, hard-linked input files and output traversal
fail. All generated default models, state and processed data are already ignored.
Keep custom output directories private and Git-ignored as well.

Bootstrap is deliberate: first prepare sufficient historical OHLCV with #56 using
explicit dates, then #58 cleaning, and supply the resulting clean CSV containing
**exactly** the configured symbols. The runner neither downloads a full history nor
initializes training from a few recent days. A missing, reordered, dirty or incomplete
bootstrap fails. An insufficient snapshot returns `SKIPPED_INSUFFICIENT_DATA` without
a version. Existing registry history is retained. On the first local check, the
prepared dataset's last dates become an explicit bootstrap reference, **not a claim
about an undocumented previous training run**; subsequent watermarks come from
each family's successfully published candidate snapshot. Preserve the matching
registry, dataset and journal when moving an existing scope to another machine.

For example, create an ignored `ml/models/retraining/config.json` at repository root:

```json
{
  "symbols": ["AAPL", "MSFT", "NVDA"],
  "model_families": ["logistic_regression", "random_forest"],
  "dataset_path": "ml/data/processed/retraining-clean.csv",
  "state_path": "ml/models/retraining/state.json",
  "registry_path": "ml/models/registry.json",
  "artifacts_path": "ml/models/artifacts",
  "check_interval_days": 3,
  "minimum_new_sessions": 1,
  "test_fraction": 0.20
}
```

Choose your own explicit symbols and bootstrap file. Relative configuration paths
are resolved against the worker's current directory. From repository root:

```powershell
$env:PYTHONPATH = "ml/src"
# Read-only decision from supplied local rows; no network, fit, lock or output writes.
& ml/.venv/Scripts/python.exe -m stocklab_ml.retraining.cli --once --config ml/models/retraining/config.json --local-data ml/data/processed/incoming.csv --dry-run
# Actual local publication, still without a provider or AWS call.
& ml/.venv/Scripts/python.exe -m stocklab_ml.retraining.cli --once --config ml/models/retraining/config.json --local-data ml/data/processed/incoming.csv
# Explicit provider check; only TWELVE_DATA_ML_API_KEY is consumed.
& ml/.venv/Scripts/python.exe -m stocklab_ml.retraining.cli --once --config ml/models/retraining/config.json
```

The CLI requires `--once`; it emits one JSON result to stdout and small structured
checkpoint logs to stderr. Exit codes: **0** completion/normal skip/dry-run,
**1** retraining failure or partial publication, **2** configuration/state/storage
preflight failure. `--now-utc` injects a canonical timezone-aware UTC test clock;
`--retry-run-id` replays a known durable run. No raw error causes or credentials are
printed. Reports include UUIDs, dates, family metrics, version, artifact SHA-256,
local/remote references, session counts, request count, duration and Active before/after.
Reports without a run retain the invocation's trigger source; replayed runs keep their original source.

### New observations, incremental refresh and credit budget

The cadence gate uses the last completed check; D+1 returns `SKIPPED_NOT_DUE`,
D+3 permits a check. A pending failure resumes before this gate so the same run
can recover explicitly. Each eligible check compares validated per-symbol dates
with the last successful family watermarks (or bootstrap reference), then counts
observations strictly after those dates. An identical refresh returns
`SKIPPED_NO_NEW_DATA`: no features, fit, version, artifact or S3 operation.
Weekends and holidays have exactly this behavior when no new sessions are returned;
there is no assumed exchange calendar. A minimum greater than one accumulates new
sessions across checks even when the mutable clean dataset has already been refreshed.

Only #56 `prepare_historical_dataset` calls Twelve Data, once per configured symbol
per check, shared across families. The window starts seven calendar days before that
symbol's latest observation (bounded by its first observation) and ends at the
conservative cutoff. It is limited to 90 inclusive calendar days by default and
always below #56's single-request limit. Every symbol's window is checked before
creating pending work or spending credits. An oversized window raises a preflight
`RetrainingDataError` (CLI exit 2), leaving the journal and bootstrap unchanged.
For an explicit bounded catch-up, supply the prepared observations through
`--local-data` and deliberately set `maximum_refresh_days` to cover the overlap
through the cutoff (at most 4998 days). The runner merges them under the same
journal and training watermarks; do not replace those files outside the runner.
No hidden full-history fetch or extra check-then-fetch is performed.
There is no Twelve Data retry, polling, backend/frontend key fallback or alternate
provider. The API counter is checkpointed before each attempted request, including
failed attempts; explicit retries add their attempted calls. #56's `1day`, `splits`,
envelope validation and request safeguards remain unchanged.

The cutoff excludes the current **UTC-12 calendar day** and is additionally bounded
by the execution host's yesterday to satisfy #56. This deliberately conservative
choice can delay availability by an additional day around UTC noon; it prevents
using a partial session even on a UTC-configured AWS host. New local/provider rows
are cleaned with #58's explicit cutoff. Invalid/future local rows are removed by
that existing contract; conflicting valid duplicate keys return
`DATA_REVISION_CONFLICT`. Exact duplicates collapse deterministically. An adjusted
price revision in the overlap is never silently merged: the old dataset is left
intact and no training version is published. Reconciliation after splits/corporate
actions requires a deliberate separate historical rebuild. A revision conflict
remains `FAILED` (CLI exit 1) but closes its run and cadence checkpoint; replaying
that UUID cannot publish or repeatedly retry the same conflict.

Pause scheduling the conflicted scope during reconciliation. Preserve its dataset
and journal, and use #56/#58 to build a corrected bootstrap at a **new CSV path**.
Create an explicit new config with that `dataset_path` and a **new `state_path`**,
keeping the same `registry_path` and `artifacts_path`. Choose the rebuilt bootstrap
reference date deliberately and pass later observed sessions with `--local-data`.
The new scope resumes training when its session threshold is met, keeps version
numbering and existing Active records, and leaves the old scope's audit history
intact. Do not overwrite a dataset already identified by the old journal.
Revisions outside the bounded overlap cannot be detected by this incremental check.

### Immutable training, evaluation and publication

Each run saves a no-clobber `snapshots/<run_uuid>/dataset.csv` beside the journal.
Its journal metadata records symbols, first/last dates, row count, canonical #75
CSV fingerprint, per-symbol dates, cutoff and preparation rules. Every check verifies
all referenced snapshots against their complete metadata, including per-symbol
final dates and fingerprints; keep historical snapshots available with their
journal. A retry uses its frozen snapshot even if newly supplied rows differ.
The mutable clean CSV
is replaced atomically only after the immutable snapshot and journal checkpoint
exist; recovery permits the previous or new fingerprint, never arbitrary edited data.

#60 `engineer_features` runs on the full clean snapshot: the seven official features,
Wilder RSI and warm-up rules are unchanged. #61/#62 keep their exact parameters,
train-only `StandardScaler` for Logistic, 300-tree RF with `random_state=42`, and
the shared purged chronological 20% holdout. No targets extend past observed data;
train feature **and target dates** precede the holdout boundary. Holdout observations
are never refitted after evaluation. With both families selected, RF's retained
`baseline_result` supplies Logistic, avoiding a second Logistic fit. RF alone does
not register its unselected baseline; if RF fails, Logistic can still publish independently.

#75's `training_payload` reuses #63 `evaluate_predictions` and validates the fitted
estimator, classes/features, report, actual holdout predictions and finite metrics
before registration. Accuracy, precision, recall, F1, confusion counts and supports
are the calculated metrics, not invented improvements. The existing registry schema
is unchanged: dataset lineage is journaled under its stable `TrainingRunId` rather
than creating a second version registry. #75 allocates the next per-family version,
defaults to Archived, and binds the exact training result. #76 saves/validates the
binary and manifest, then optionally uploads through `S3ModelArtifactStore`.

S3 uses only the existing #76 variables `STOCKLAB_ML_S3_BUCKET`,
`STOCKLAB_ML_AWS_REGION`, `STOCKLAB_ML_S3_PREFIX`. Configure an independently approved
private source, set `upload_to_s3=true`, and pass **`--approve-private-s3`**.
Only then does the CLI call `S3ArtifactConfig.from_environment(trusted_private_source=True)`.
No bucket, key or policy is created; local-only success has `s3_reference=null`.
The bucket/region/prefix/size limit are frozen in the run, so a retry cannot silently
publish into another destination. #76's conditional PUT, binary verification,
manifest-last commit marker and idempotent retry remain authoritative. No untrusted
joblib is loaded by the retraining runner; artifact recovery verifies bytes/manifest
through #76 without deserialization.

An administrative #75 Active change during a check appears in the before/after
maps and an `ACTIVE_CHANGED_EXTERNALLY` log event. The worker retains its actual
outcome and original failure category, preserves that administrative choice, and
can recover older runs failed for this reason without another version.

### Durable state, failures, locks and recovery

The bounded schema-v1 JSON journal tracks all checks and family checkpoints:
`CHECKING -> DATA_READY -> TRAINING -> EVALUATED -> VERSION_REGISTERED ->
ARTIFACT_SAVED -> ARTIFACT_UPLOADED (when requested) -> COMPLETED`.
`SKIPPED_NO_NEW_DATA`, `SKIPPED_INSUFFICIENT_NEW_SESSIONS` and
`SKIPPED_INSUFFICIENT_DATA` finish a check without publishing an invalid model.
`FAILED` or `PARTIAL_PUBLICATION` retains each family's last verified phase,
payload, trained timestamp, UUID/version and local/S3 result. A multi-family run is
not a global transaction: a successful family's version remains verifiable when
another family fails. It is never reported as all-completed.

A run UUID and deterministic family UUIDs are persisted **before fit/publication**.
Evaluated immutable metadata and training time are checkpointed before #75 registration.
After a crash, the runner finds the same registry UUID even if journaling its version
was interrupted. A complete verified local artifact resumes publication without fit.
Otherwise it reproduces training from the same snapshot/config and requires exactly
matching #75 metadata/holdout before re-registering that same UUID; #75 rejects conflicts.
Existing orphan binaries are reused only through #76's identical-content retry.
Completed families are verified locally and kept completed. `--retry-run-id` on a
finished check returns `SKIPPED_ALREADY_PROCESSED` without another version or state write.
Retries retain the previous attempt's completion timestamp until the next completion.
Retry and completion clocks cannot precede durable run, training or registration times.
Historical terminal runs must also enclose their training and registration times.
Snapshot watermarks stay within the recorded date range, never precede the same
symbol's previous date, and their maximum must equal the snapshot's final date.
Bootstrap dates are checked against the actual per-symbol history prefixes and
their original fingerprint; incremental refresh preserves those observations.
Every deterministic family UUID found in #75 must retain its journal lineage.
New training times are validated before their checkpoint or registration.

Writes reuse the existing temporary sibling / flush / fsync / atomic replace writer.
On POSIX, #77 additionally fsyncs publication directories and their parents for
the journal, snapshots, mutable dataset and local artifact completion.
Duplicate JSON keys, invalid identity/phase/metrics, dropped pending work, missing
registry history, changed dataset/config/destination and corrupt snapshots fail closed;
no registry counter, journal or dataset is silently reset. The journal read limit is
8 MiB; preserve/export lineage and deliberately upgrade storage before reaching it.
Files/versions are not automatically pruned. Process-crash recovery is tested; power-loss
and real network filesystem behavior still require deployment validation.

One canonical journal path coordinates a scope. `state.json.lock` uses a nonblocking
kernel lock (`msvcrt` byte lock on Windows, `flock` on Linux); a second process returns
`SKIPPED_ALREADY_RUNNING`. The stable lock file is never deleted based on age; the
kernel releases ownership when the process dies. #75 retains its own separate
exclusive-create registry lock. A killed #75 writer can leave that lock file: verify
all writers have stopped and inspect registry integrity before manual removal.
Do not treat a local registry lock or copied journals as universal coordination.

### Dry-run and offline verification

Dry-run reads validated configuration, registry/journal, cadence and the clean
bootstrap, then computes a theoretical refresh/session decision **only from an
explicit local frame/CSV**. It never creates a lock, journal, snapshot, version,
model, dataset write or S3 client, and performs no external calls. Without local
new rows it returns `DRY_RUN_NEEDS_LOCAL_DATA`; pending work returns
`DRY_RUN_PENDING_RETRY`. A local refresh returns `DRY_RUN_NO_NEW_DATA` when no
sessions were added, `DRY_RUN_INSUFFICIENT_NEW_SESSIONS` below the configured
minimum, or `DRY_RUN_READY` when that threshold is met. It does not fit or promise
valid training metrics.

From `ml/`, with Python 3.12 and `PYTHONPATH=src`:

```shell
python examples/automatic_retraining_smoke.py
python -m pytest -q
python -m pip check
```

The assert-based smoke blocks sockets, creates synthetic OHLCV, trains the real
initial Logistic/RF models and explicitly activates v000001 **only for setup**.
Three synthetic new sessions produce v000002 Archived, verified artifacts and fake
S3 publication. An interrupted manifest upload resumes with no fit, replay creates
no additional version, and a due identical refresh skips. Old Active records remain
exactly unchanged. Metrics demonstrate mechanics on synthetic data, not real market
performance. All tests remain offline, including a two-process killed-owner lock
test, crash after registration, partial multi-model failure and SDK-backed fake S3.

On Windows in the managed sandbox, existing atomic no-clobber writers require
NTFS hard-link permissions. Use the approved unrestricted test runner and dedicated
ignored temporary/cache directories rather than weakening storage semantics:

```powershell
# From repository root (use a fresh basetemp for each command).
& ml/.venv/Scripts/python.exe -m pytest ml -q --basetemp=ml/results/pytest-77-root -o cache_dir=ml/results/pytest-cache-77-root
# From ml/:
& .venv/Scripts/python.exe -m pytest -q --basetemp=results/pytest-77-ml -o cache_dir=results/pytest-cache-77-ml
```

### AWS execution and persistent prerequisites (not deployed)

The prepared integration is [EventBridge Scheduler's ECS RunTask target](https://docs.aws.amazon.com/scheduler/latest/UserGuide/managing-targets-templated.html)
using `rate(3 days)`, one Linux Fargate task, and a shared EFS access point.
`ml/aws/automatic-retraining.template.json` is parameterized CloudFormation for
**only** the task definition and a **DISABLED** schedule. All cluster, roles, EFS,
access point, subnets/security groups, private ECR image, log group, ML secret and
approved S3 bucket/prefix must already exist. It creates nothing unless separately
reviewed and manually deployed. No deployment command is run by this code.

Build the scientific Python container explicitly when DEV is ready:
`docker build -f ml/aws/Dockerfile -t stocklab-ml-retraining:dev ml`.
Publish/pin its private ECR digest separately. The image runs as UID/GID 1000,
with Python 3.12 and the existing requirements. The template starts with 2 vCPU /
4 GiB, single-thread numerical libraries and a two-hour `timeout` (TERM, then KILL
after 60 seconds); container `StopTimeout` is 120 seconds. These are configuration
starting points, **not measured adequate resources**. Profile actual symbols/history,
training duration, memory and artifacts before enabling. #76's default artifact limit
is 100 MiB. A standard Lambda zip is not assumed to fit this native scientific stack.

[Fargate Linux 1.4.0+ supports EFS volumes](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/efs-volumes.html).
Use transit encryption, IAM authorization, an access point enforcing UID/GID 1000,
and root directory `/` or omitted as in the template. Permit NFS/2049 from the task's
security group to EFS mount targets. Private subnets need approved egress for Twelve
Data and appropriate ECR/Secrets Manager/CloudWatch/S3 endpoints or routing.
The read-only container root uses an explicitly prepared EFS `/mnt/stocklab/tmp`.

Before the first AWS worker, provision the same persistent root with:

```text
/mnt/stocklab/.stocklab-retraining-volume.json
/mnt/stocklab/data/clean.csv
/mnt/stocklab/models/registry.json (+ its lock when held)
/mnt/stocklab/models/artifacts/
/mnt/stocklab/retraining/config.json
/mnt/stocklab/retraining/state.json (+ snapshots/ingestion/lock)
/mnt/stocklab/tmp/
```

Copy `ml/aws/retraining.config.example.json`, choose your symbols and a fresh
canonical volume UUID, and create the marker as
`{"schema_version":1,"volume_id":"<the same UUID>"}`. Provision the existing trained
registry/history and clean dataset deliberately; never use an empty per-task copy.
For a new retraining journal, perform an explicit offline no-new-data local check
outside ECS using the same mounted paths and the bootstrap as local input, initially
with `aws_enabled=false` and `upload_to_s3=false`; this seeds checked baseline dates
without fitting/registering. Then save the approved AWS configuration on the volume.
For an existing scope, migrate its whole journal, registry and snapshots together,
with no pending run/config-path changes; inspect/approve the migration first.

AWS preflight requires the configured root to be an actual Linux NFSv4 mount,
matching marker UUID, and verified pre-existing dataset, **nonempty registry** and
journal. All state and artifacts must remain below that root. ECS ambient context
also requires `aws_enabled=true`; a local-mode misconfiguration cannot bypass the
guard. An absent/unmounted/unrecognized/corrupt persistent state fails before provider
calls or registration, never manufactures a first version. EFS supports NFSv4.1
[advisory locks](https://aws.amazon.com/efs/faq/); test overlap, cancellation/restart,
atomic publication and lock recovery with two actual DEV tasks before enabling.
Mount detection/marker checks do not independently authenticate a volume or prove
all distributed failure modes. Restrict write access and retain reliable backups.

Use distinct least-privilege existing roles, never `AdministratorAccess` or `s3:*`:

- Scheduler: trust `scheduler.amazonaws.com`, allow `ecs:RunTask` on this task
  definition restricted to the cluster, and `iam:PassRole` on the exact execution/task
  roles with `iam:PassedToService=ecs-tasks.amazonaws.com`.
- Task execution: trust `ecs-tasks.amazonaws.com`; approved ECR read, log stream/events
  on the existing log group, `secretsmanager:GetSecretValue` on the ML-only secret,
  and KMS decrypt on its chosen key when required.
- Task: `elasticfilesystem:ClientMount` and `ClientWrite` on this filesystem/access
  point; `s3:GetObject` and `PutObject` on only `<prefix>/models/*`; selected KMS
  `GenerateDataKey`/`Decrypt` if SSE-KMS is used. No delete, ACL or bucket creation.

The [ECS IAM role requirements](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/CWE_IAM_role.html)
include passing the task/execution roles. Scheduler delivery retries are bounded;
successful `RunTask` submission does **not** mean the training process succeeded.
Monitor task exit codes/stopped reasons and structured logs separately, and recover
pending publication from the journal. Scheduler, Fargate runtime, EFS storage,
S3 requests/storage (including verification GETs), logging, egress/KMS and provider
credits may incur costs; no live cost or resource adequacy is claimed here.

**AWS scheduler not deployed — secure DEV configuration unavailable.**
No real credentials/bucket/EFS/scheduler/worker, live retraining or S3 upload have
been configured or validated for #77. No cloud resources are created/modified by
offline tests. #92 remains outside this implementation.

## Model promotion (#78)

`stocklab_ml.promotion` evaluates **existing registered artifacts**, then permits
an independently explicit application. It never trains, refreshes historical
prices, runs the #77 scheduler, uploads artifacts, executes trades, or creates AWS
resources. #77 continues to register new candidates as `Archived`; registration,
artifact publication and successful evaluation never activate a candidate.

Each request names an exact `model_name`, `candidate_version` and
`expected_active_version`. There is no latest/newest/best-file lookup. Logistic
(`logistic_regression_baseline`) and Random Forest (`random_forest_baseline`)
remain independent families. Unknown candidates are `BLOCKED/UNKNOWN_CANDIDATE`;
cross-family artifact references are `BLOCKED/WRONG_MODEL_FAMILY`. An already
Active candidate returns `BLOCKED/ALREADY_ACTIVE` without any status mutation.

### Evaluate, inspect, then explicitly apply

```python
from datetime import UTC, datetime
from uuid import uuid4
from stocklab_ml.artifacts.local_storage import artifact_paths
from stocklab_ml.promotion import (
    ModelPromotionPolicy, ModelPromotionRequest, PromotionAuditStore,
    evaluate_model_promotion, apply_model_promotion,
)
from stocklab_ml.versioning import ModelVersionRegistry

# cleaned is an explicitly supplied, validated CleanedDatasetResult.
registry = ModelVersionRegistry("ml/models/registry.json")
audit = PromotionAuditStore("ml/models/promotion")
request = ModelPromotionRequest(
    model_name="logistic_regression_baseline",
    candidate_version="v000002",
    expected_active_version="v000001",
    evaluation_dataset=cleaned,
    promotion_policy=ModelPromotionPolicy(),
    evaluation_timestamp_utc=datetime.now(UTC),
    evaluation_id=str(uuid4()),
    evaluation_start="2024-07-01",
    evaluation_end="2024-10-31",
)
sources = dict(
    registry=registry, audit_store=audit,
    active_artifact=artifact_paths(request.model_name, "v000001"),
    candidate_artifact=artifact_paths(request.model_name, "v000002"),
    trusted_source=True,  # Caller explicitly approves these executable private sources.
)
decision = evaluate_model_promotion(request, **sources)
print(decision.decision, decision.reason_codes, decision.evidence_sha256)
# Read-only with respect to registry and source artifacts; only audit/cache files are written.
# After an authorized caller has reviewed the exact APPROVED decision:
# application = apply_model_promotion(decision, request=request, **sources)
```

The evaluation and apply calls are separate operations. `APPROVED` means the
complete policy passed, **not activated**. `REJECTED` means valid comparable
evidence failed a policy gate. `BLOCKED` means trustworthy evidence is unavailable
or insufficient: missing/corrupt artifacts, incompatible runtime, stale registry,
no Active, prediction/input mismatch, or insufficient common/forward observations.
All failed gates appear in `reason_codes`, including `LOWER_F1`, `LOWER_RETURN`,
`DRAWDOWN_TOO_HIGH`, `INSUFFICIENT_IMPROVEMENT`, absolute-score failures and
`BENCHMARK_REQUIREMENT_FAILED`. Neither rejection nor blocking changes statuses.

No Active model is invented. Initial activation requires a separate explicit
`initialize=True` request with `expected_active_version=None`, an exact Archived
candidate, verified artifact, sufficient evidence, absolute thresholds and any
configured benchmark/forward gates. Only its approved explicit apply can activate
it. Relative replacement gates have no incumbent in this mode.

### Verified existing artifacts and retraining integration

`candidate_artifact` and `active_artifact` accept `LocalModelArtifact`, `LoadedModel`,
or an approved `S3ModelArtifactStore`. Promotion always loads through #76:
manifest identity and TrainingRunId, immutable registry metadata, bounded bytes,
SHA-256, fitted estimator shape/parameters, Python major/minor and exact recorded
scikit-learn/NumPy/SciPy/joblib versions are validated before inference. A passed
`LoadedModel` is reloaded from its verified local reference; an arbitrary mutable
in-memory estimator is never accepted as provenance. Explicit trust is required;
SHA-256 does not authenticate a manifest and binary that an attacker can replace.

An S3 store downloads the exact registered version into the audit store's
`downloads/` cache and uses the same local loader. Only an already approved private
bucket/prefix is accepted by #76. There are no S3 writes or infrastructure changes
during promotion. Local artifacts allow every test and smoke to run offline.
`RetrainingRunResult.report["model_results"][family]` already supplies exact
model/version/run identities and artifact paths/digests; callers may use these
after checking the registry. The #77 scheduler, cadence and journal are unchanged.

### Common out-of-sample evidence

The caller fixes an explicit daily interval before inference. Both versions use
the same supplied cleaned OHLCV, symbols, ordered feature and target dates,
actual labels, market window and `BacktestConfig`. #60 causal feature engineering
runs once with unchanged formulas/warm-up and no imputation. #61's official
`target_up_1d` uses the next **observed** clean close per symbol; terminal unknown
labels are excluded. Inference receives only date, symbol and `FEATURE_COLUMNS`,
never the label, target date or future close.

#75 validates the recorded target definition and purged split rule: both training
feature dates **and training target dates** strictly precede `split_date`. The
common window conservatively starts at or after the later split boundary of both
versions and the requested start. Target dates must be observed and no later than
the explicit end. Earlier source rows supply causal warm-up only; rows after the
end cannot influence features, labels, executions or valuations. The requested
end must precede the evaluation UTC date to avoid claiming a still-open daily bar
is observed. No period is selected by candidate performance. Old registry holdout
scores from different periods are never directly compared.

#63 recomputes accuracy, precision, recall, F1, confusion counts and class support
on exactly the same new common observations. #64 and #65 retain BUY >= .60,
SELL <= .40, HOLD otherwise, and their unchanged confidence formulas. Versioned
predictions and decisions retain permanent ModelName/ModelVersion identities;
their fingerprints and version records are preserved in the audit.

Both #73 simulations use the same capital (default 100000), minimum confidence
(.70), exposure (.20), open positions (10), trade allocation (.10), no shorts,
8dp quantities and zero fees/slippage. Execution remains the next observed
same-symbol **open** after a decision close. The market slice deliberately ends
at the last common observed target session. Reports compare final value, total
return, SELL-based closed-trade win rate, drawdown and executed trades. With no
closed SELL trades, #73's win rate is zero; this is limited evidence, not a claim
about realized trading quality.

Both #74 Buy & Hold comparisons run on the same market slice, dates, symbols and
capital. Their benchmark reports, equity dates and equity fingerprints must be
identical or the evaluation blocks. Equal weighting, first next-session entry,
idle unavailable allocations, no rebalancing/dividends and price-return accounting
are unchanged. `evidence["benchmark"]` is the shared report; each model includes
its own strategy-versus-benchmark comparison.

`metric_deltas` are always candidate minus Active, including all four
classification metrics, final value, return, win rate, drawdown and trade count.
Return and drawdown use decimals: .04 means four percentage points. A positive
drawdown delta is **worse**, since #73 drawdown is nonnegative.

Historical OOS simulation is not live performance. Each exact record retains its
real `trained_at_utc`. A forward row conservatively requires its feature-session
date to be strictly after both versions' UTC training-completion dates; same-day
availability cannot be proved without exchange close timestamps. The audit marks
evidence `historical`, `mixed` or `prospective` and counts common forward rows.
`minimum_forward_rows` can block a retrospective-only evaluation. Even prospective
historical replay is not broker-observed live P&L. Reusing holdouts to select
versions introduces selection bias; no future profitability is guaranteed.

### Conservative immutable policy

| Field | Default | Meaning |
| --- | --- | --- |
| `minimum_evaluation_rows` | 30 | Common labeled observations across symbols; shortage blocks. |
| `minimum_sample_symbols` | 1 | Distinct symbols with common eligible rows; shortage blocks. |
| `minimum_accuracy` | .50 | Absolute candidate classification floor. |
| `minimum_f1` | .50 | Absolute candidate F1 floor. |
| `minimum_return_improvement` | 0 | Minimum candidate minus Active total return, in decimals. |
| `maximum_drawdown_increase` | 0 | Maximum candidate minus Active drawdown, in decimals. |
| `require_benchmark_comparison` | True | Require a valid shared #74 benchmark; V1 computes it for both models regardless. |
| `require_benchmark_outperformance` | False | Candidate return must strictly exceed Buy & Hold when enabled. |
| `minimum_forward_rows` | 0 | Common rows after both availability dates; shortage blocks. |

Counts require real integers; numeric thresholds must be finite, nonboolean and
nonnegative, with accuracy/F1/drawdown tolerance bounded by one. Flags require
actual booleans. Direct policy evidence requires a nonempty list or tuple of
distinct symbols following the existing #58 symbol contract; malformed symbol
evidence is `BLOCKED/INVALID_METRICS`. The frozen policy is copied into every decision. Replacement
requires **all** absolute/sample/benchmark gates, F1 and return nonregression,
drawdown within tolerance, and a strict improvement in F1, return or drawdown.
Accuracy alone cannot authorize promotion. Perfect ties and numerical noise
(relative tolerance 1e-9, absolute 1e-12) are insufficient. Defaults allow no F1 or
return regression and no drawdown increase beyond numerical tolerance.

### Atomic switching, audit and interrupted applications

`apply_model_promotion` requires the exact saved approved evaluation, identical
request/dataset fingerprint, the same registry path, unchanged policy, immutable
version metadata and newly verified artifact digests/runtime. The new
`registry.set_active_version_if_current(name, candidate, expected_active_version,
expected_records=...)` acquires the existing #75 lock, rereads schema-v1 records,
checks the expected Active and evaluated snapshots, then publishes one atomic
status change. A stale or concurrent promotion gets a controlled conflict. At
most one candidate can replace the same expected Active; other families, versions,
training metrics, TrainingRunIds and artifacts remain unchanged. Existing #75
administrative APIs and JSON schema remain compatible.

Default audit root: `ml/models/promotion/` (already Git-ignored):

- `evaluations/<EvaluationId>.json`: immutable decision, policy, full version and
  training reports, artifact/runtime identities, source/feature/label/market
  fingerprints, classification/confusion evidence, backtest/benchmark summaries,
  equity fingerprints/dates, deltas, reasons and evaluation timestamp.
- `intents/<EvaluationId>.json`: durable exact evidence hash, registry path,
  candidate/previous Active identities and prepared timestamp, published before CAS.
- `applications/<EvaluationId>.json`: final `APPLIED` result, evidence hash and
  applied timestamp, published after CAS.

Files have strict bounded JSON envelopes and a canonical SHA-256, reject duplicate
keys/nonfinite content, use sibling temporary files, flush/fsync and atomic
no-clobber publication. Identical evaluation retries reuse existing evidence;
changed content with the same EvaluationId conflicts. Corrupt or missing required
history fails closed and is never silently repaired. Audit storage must itself be
trusted: hashes detect corruption, not an attacker replacing both evidence and hash.

**There is no multi-file transaction between registry and audit.** Failure to
publish an intent prevents CAS. A durable intent without a final application is
`PromotionRecoveryError`, whether interruption occurred before CAS or after CAS
but before final audit. Wrapped #75 CAS publication or lock-cleanup failures also
report `PromotionRecoveryError` and preserve the durable intent.
Retrying inspects the registry but **never replays** that
intent or infers success merely because the candidate is currently Active: an
administrative/ABA transition could produce the same state. Preserve the intent,
verify the registry and sources, and resolve the ambiguous operation explicitly
under administrative control. V1 has no automatic reconciliation that can invent
missing proof. A finalized verified audit plus matching resulting statuses and
artifacts permits `ALREADY_APPLIED` without another registry mutation. A later
administrative change makes that retry conflict. A killed #75 writer may leave
its lock sentinel; never remove a live lock or steal a lock automatically.

### Offline smoke and verification

From repository root, using the existing Python 3.12 environment:

```powershell
$env:PYTHONPATH = (Resolve-Path ml/src).Path
& ml/.venv/Scripts/python.exe ml/examples/model_promotion_smoke.py
& ml/.venv/Scripts/python.exe -m pip check
& ml/.venv/Scripts/python.exe -m pytest ml -q --ignore=ml/results --ignore=ml/models --ignore=ml/ml --basetemp=ml/results/pytest-78-root -o cache_dir=ml/results/pytest-cache-78-root
# From ml/:
& .venv/Scripts/python.exe -m pytest -q --basetemp=results/pytest-78-ml -o cache_dir=results/pytest-cache-78-ml
```

Use fresh ignored basetemp/cache directories and the NTFS-capable runner described
above. The root command excludes generated local results/models, which can contain
many old test directories. The assert-based smoke blocks sockets, trains two
synthetic runs **only for setup**, registers exact versions, explicitly activates
the initial version, saves/loads through #76, then forbids estimator/scaler fitting
during promotion. It verifies unchanged Active after evaluation, reloads the audit,
and applies explicitly only on APPROVED. Its ordinary example honestly rejects a
lower-F1 candidate despite a better simulated return. Tests separately exercise an
approved synthetic replacement, explicit initialization, two-process CAS and apply
races, stale evidence, tampering, incompatible artifacts and interrupted audit.
No live AWS calls, API credits, deployed scheduler or real trades are involved.

## Dataset and backtest storage / AWS S3 (#48)

Issue #48 extends the existing #76 model storage with `S3DatasetStore`
(`stocklab_ml.data.s3_storage`) and `S3BacktestStore`
(`stocklab_ml.backtesting.s3_storage`). All three reuse the same validated
`S3ArtifactConfig` and private HTTPS object transport. No training, promotion,
risk-manager, paper-trading or execution policy is changed.

### Configuration and permissions

Use only the already provisioned private development resources:

```powershell
$env:STOCKLAB_ML_S3_BUCKET = "stocklab-ml-248557779705"
$env:STOCKLAB_ML_AWS_REGION = "ca-central-1"
$env:STOCKLAB_ML_S3_PREFIX = "ml"
$env:AWS_PROFILE = "stocklab-dev"
$env:PYTHONPATH = "ml/src" # commands below run from the repository root
aws login --profile stocklab-dev
```

Use the approved temporary login or task-role credential chain, never root or
embedded access keys. The existing role/policy are `StockLabMLTaskRole` and
`StockLabMLS3AccessPolicy`. The code creates no bucket, IAM principal, policy,
key, scheduler or compute resource and changes no resource configuration.
Retain S3 Block Public Access on the bucket; no public ACL or URL is emitted.

The storage classes need only `s3:GetObject` and `s3:PutObject` on the selected
`ml/datasets/*`, `ml/backtests/*` and existing `ml/models/*` objects. They use
neither LIST, DELETE, HEAD nor ACL operations. No policy update is performed.
Existing bucket encryption is inherited; an existing KMS configuration can also
require the already-approved KMS permissions described in #76. CLI live commands
require `--live-aws`, verify the exact configuration above, and check the caller
with STS before any S3 request, rejecting root and another account. SDK consumers
must independently approve their configured private source as in #76.

### Object organization, integrity and collisions

```text
ml/datasets/<URL-encoded-symbol>/<start>_<end>/<raw|cleaned>/<dataset-id>/
  files/<sha256>/<original-filename>
  manifest.json
ml/backtests/<backtest-id>/
  files/<sha256>/trades.csv
  files/<sha256>/equity_curve.csv
  files/<sha256>/positions.csv
  files/<sha256>/report.json
  manifest.json
ml/models/<model-name>/<model-version>/... # unchanged #76 layout
```

Dates are ISO calendar dates. Dataset periods can include a single day. Symbols
follow the existing historical-data contract; for example `BRK/B` becomes
`BRK%2FB`. Run identifiers and filenames must be portable safe path segments.
Raw dataset bytes can be CSV, provider JSON or another file format; the adapter
does not silently parse or rewrite the source. A cleaned dataset is a separate
publication with `kind="cleaned"`. Callers provide its symbol/period and metadata;
the storage layer verifies bytes and identity, not the financial meaning of a
caller-provided file.

Each schema-v1 manifest contains the exact identity, JSON metadata, required
filenames, SHA-256 and byte lengths. Files are bounded to
`config.max_artifact_bytes` (100 MiB by default), manifests to 1 MiB. Dataset
uploads contain one file; backtests contain the four existing engine files.
Uploads use `If-None-Match: *`, verify stored bytes with GET, then publish the
manifest last. Downloads derive keys internally, reject invalid/duplicate
manifest fields, unsafe paths, wrong identities, missing files and changed
bytes, and verify the downloaded local bytes. An ETag is never a digest.
These [conditional writes](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes.html)
prevent overwriting existing objects, including concurrent publications.

Re-uploading the same bytes and metadata under the same identity is idempotent.
Different bytes or metadata under an already published identity raise
`ArtifactConflictError`; choose a new dataset/backtest identifier. The manifest
is the completion marker: interrupted uploads can leave unreferenced objects,
and retrying the same inputs can finish without deleting anything. Different
backtest identifiers remain isolated. There is no transaction across all the
workflow's dataset, model and backtest bundles.

Downloads require a new destination directory and never overwrite an existing
directory, including an empty one. All remote bytes are verified before creating
it. A local disk failure can leave partial files **without a completed manifest**;
select a new destination after resolving the failure. Files and JSON metadata
are preserved; filesystem timestamps, ownership and original S3 system headers
are not part of this portable contract. At most four bounded files are buffered
in memory. Multipart uploads and automatic garbage collection are outside V1.

The existing controlled error types apply: `ArtifactNotFoundError`,
`ArtifactIntegrityError`, `ArtifactConflictError` and `ArtifactStorageError`.
Timeouts and access failures never become successful results or trigger a
different target. SDK connection/read timeouts are 5/30 seconds with three total
attempts. Without ListBucket, a missing object may appear as AccessDenied (403):
downloads then report a storage/access error; uploads still use conditional
publication as #76 does. Caller-facing CLI errors omit raw SDK responses.

### Reproducible local backtest

```powershell
& ml/.venv/Scripts/python.exe -m stocklab_ml.backtesting.s3_workflow run `
  --output ml/results/s3-demo/run --backtest-id demo-48 `
  --registry ml/results/s3-demo/registry.json
```

This invokes actual existing components: deterministic synthetic daily OHLCV,
`clean_historical_dataset`, causal `engineer_features`,
`train_logistic_regression`, the version registry and model artifact serializer,
`generate_trading_signals`, `add_confidence_scores`, `run_backtest`, and
`save_backtest_result`. Training uses the existing purged chronological train
partition; only holdout predictions feed the simulation. Orders execute at the
same symbol's next observed open after a decision. No provider client, AWS
client, Risk Manager, Paper Trading Engine or real order service is invoked.

Outputs include the raw/cleaned CSVs and cleaning report, fitted model artifact,
predictions, confidence signals, the four engine result files, and
`workflow.json` with provenance and exact model identity. Synthetic results
validate mechanics and **are not real investment performance**. Outputs and model
predictions are reproducible for the same inputs/runtime; UUIDs, model versions
and registration timestamps intentionally differ. The default registry is the
existing shared `ml/models/registry.json`; use the same registry across runs to
avoid reusing a model version. Independent machines/registries must coordinate
version identities; a collision fails safely instead of replacing a model.
No version is automatically activated or promoted.

A caller can explicitly supply a local, single-symbol historical CSV with the
existing `date,symbol,open,high,low,close,volume` schema and an explicit cutoff:

```powershell
& ml/.venv/Scripts/python.exe -m stocklab_ml.backtesting.s3_workflow run `
  --csv C:/data/AAPL-history.csv --as-of 2026-10-23 `
  --output ml/results/aapl-s3-run --backtest-id aapl-20261023
```

The original CSV bytes are preserved. Cleaning discards invalid observations
according to #58; the cleaning report records removals. Input must contain enough
valid history and both training classes for the existing model. Multi-symbol
workflow CSVs are rejected; run each symbol separately. `caller_supplied_csv`
records the source category, not a certification that the file is real market
data. No local historical dataset was available during implementation, so the
recorded validation uses synthetic fixtures only.

### Upload and retrieve existing files without another run

```powershell
# Explicit live AWS request; does not run training or backtesting again.
& ml/.venv/Scripts/python.exe -m stocklab_ml.backtesting.s3_workflow upload `
  --directory ml/results/s3-demo/run --live-aws

# Recover and consult a backtest without loading a model or executing the engine.
& ml/.venv/Scripts/python.exe -m stocklab_ml.backtesting.s3_workflow download-backtest `
  --backtest-id demo-48 --output ml/results/recovered-demo-48 --live-aws
Get-Content ml/results/recovered-demo-48/report.json
Import-Csv ml/results/recovered-demo-48/trades.csv

# Independently publish or retrieve a dataset (replace dates/symbol with its identity).
& ml/.venv/Scripts/python.exe -m stocklab_ml.backtesting.s3_workflow upload-dataset `
  --file ml/results/s3-demo/run/datasets/cleaned.csv --symbol SYNTH `
  --start-date 2023-01-02 --end-date 2023-12-01 --kind cleaned --dataset-id standalone-demo-48 --live-aws
& ml/.venv/Scripts/python.exe -m stocklab_ml.backtesting.s3_workflow download-dataset `
  --symbol SYNTH --start-date 2023-01-02 --end-date 2023-12-01 --kind cleaned `
  --dataset-id standalone-demo-48 --output ml/results/recovered-dataset-48 --live-aws

# Existing four-file engine outputs can also be uploaded independently.
& ml/.venv/Scripts/python.exe -m stocklab_ml.backtesting.s3_workflow upload-backtest `
  --directory ml/results/backtesting --backtest-id existing-run-48 --live-aws
```

Use `workflow.json` for the actual symbol/date/run identities. The standalone
dataset/backtest CLI commands use empty metadata; choose a fresh identity when
publishing independently instead of reusing a workflow identity with different
metadata. For custom JSON metadata use the SDK adapters directly:

```python
from stocklab_ml.artifacts import S3ArtifactConfig
from stocklab_ml.data.s3_storage import S3DatasetStore
from stocklab_ml.backtesting.s3_storage import S3BacktestStore

config = S3ArtifactConfig.from_environment(trusted_private_source=True)
datasets = S3DatasetStore(config)
ref = datasets.upload_dataset("raw.json", symbol="AAPL", start_date="2024-01-02",
    end_date="2024-01-31", kind="raw", dataset_id="aapl-raw-001",
    metadata={"source": "caller_supplied", "synthetic": False})
bundle = datasets.download_dataset(symbol="AAPL", start_date="2024-01-02",
    end_date="2024-01-31", kind="raw", dataset_id="aapl-raw-001", output_dir="new-cache")
assert bundle.metadata == {"source": "caller_supplied", "synthetic": False}
# Existing save_backtest_result(...) returns the SavedBacktest accepted here:
backtests = S3BacktestStore(config)
# backtests.upload_backtest(saved, backtest_id="run-001", metadata={"synthetic": True})
# backtests.download_backtest("run-001", output_dir="new-backtest-cache")
```

Model upload/download/loading uses `S3ModelArtifactStore` and
`load_model_artifact` exactly as documented in #76, with the exact model name and
version from `workflow.json`. The registry is still a local #75 authority; backtest
and dataset recovery need no registry. Keep the original workflow directory and
registry for retrying a model publication. Upload does not repeat a fit or engine
run. Standard S3 requests, storage, GET verification, transfer and applicable KMS
operations can incur small charges; no expensive AWS computation is launched.

### Offline verification and optional live AWS smoke

```powershell
& ml/.venv/Scripts/python.exe -m pytest ml -q
$env:PYTHONPATH = "ml/src"
& ml/.venv/Scripts/python.exe ml/examples/s3_storage_smoke.py

# Optional, explicitly enabled, approved development resources only:
& ml/.venv/Scripts/python.exe ml/examples/s3_storage_smoke.py --live-aws
```

The default smoke blocks sockets, runs the actual ML/backtest workflow, transfers
both datasets, the fitted model and all four backtest files through an in-memory
S3 client, and verifies recovery. Model loading occurs with `Pipeline.fit`
disabled. Tests cover conditional SDK requests, exact bytes/metadata, corruption,
missing inputs/objects, denied access, network errors, concurrency, isolation,
collisions, partial publication and recovery without retraining/backtesting.
On Windows, a restricted sandbox may deny NTFS hard links used by the existing
atomic local writers; run the same offline tests in a normally permitted local
shell rather than weakening no-overwrite behavior.
The shared local writer also retries a transient Windows replacement denial
(WinError 5/32/33) at most four times, with 10/20/40 ms delays. A persistent
denial fails safely and preserves the existing target. This only retries an
already requested atomic replacement; no-clobber publication is unchanged.

Live AWS validation for #48 has **not** been executed as part of this change.
`--live-aws` additionally requires a usable approved temporary login and leaves
the uniquely identified validation objects in the bucket: cleanup requires
separate approval/permissions and is never done by this tool. Neither pytest nor
the default smoke consumes Twelve Data credits, contacts AWS, or submits trades.

Validation performed for #48 on Python 3.12: **1,585 tests passed** across all
36 ML test files (1,221 core, 127 promotion, 237 storage/retraining), with separate
temporary directories for each group. The 42 targeted storage/workflow/local-writer
tests passed, as did 10,000 actual atomic local writes. The final offline smoke
used 240 synthetic observations, produced 15 simulated trades, recovered both
datasets and all four backtest files byte-identically, and loaded the recovered
model with fitting disabled. Python compilation, CLI help and `git diff --check`
also passed. These are software-validation results, not real market returns.
