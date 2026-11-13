# Local target-free HGBT28 vintage inputs

`stocklab_ml.boosted_vintage.prepare_vintage(bundle_dir, *, manifest_sha256, as_of_utc)` reads one explicitly trusted, externally hash-bound local bundle and returns `(features, metadata)`. It does not fetch, fit, publish, activate, execute orders, change the environment, or modify any input file. The only supported profile is `hgb_swing20_market28`.

The output columns are exactly `date,symbol` and the ordered 28 snapshot features. Dates are normalized `datetime64[ns]`, symbols use pandas StringDtype, and features are finite float64. Unknown future outcomes require no labels: targets, future returns and target dates never enter the preparer. Existing inference remains explicit and Archived:

```python
features, provenance = prepare_vintage(
    bundle_dir, manifest_sha256=expected_manifest_sha256, as_of_utc=aware_utc_cutoff)
predictions = loaded.predict(features)
decisions = predictions.policy_decisions(features)  # The SAME keyed policy context.
```

This does not connect the native worker or authorize a model change. Financial qualification, acquisition, prospective assessment and next-session execution remain separate work.

## Closed local bundle

One real directory contains exactly `manifest.json` and these six ordinary files; links, junctions, hard links, subdirectories, missing files and extra files are rejected. The manifest is limited to 64 KiB, each CSV to 64 MiB, and their combined bytes to 128 MiB. Relative names are fixed, not caller-selected paths.

| File | Exact normalized CSV columns and order |
|---|---|
| `stocks.csv`, `monthly.csv`, `spy.csv` | `date,symbol,open,high,low,close,volume` |
| `vix.csv`, `vix3m.csv` | `date,open,high,low,close` |
| `expected-sessions.csv` | `date` |

CSV is strict UTF-8 without a BOM, blank records, duplicate columns or inconsistent record widths. Dates are exact ISO calendar dates. OHLC values are positive finite float64 with coherent low/open/close/high ordering. Volume is a nonnegative base-10 integer below 2^63. OHLCV files are already sorted by symbol then date; index/calendar files have unique ascending dates. No sorting repair, row removal or fill occurs. Floats are read with round-trip precision: normalization of an older source must retain its original parsing/transform evidence, rather than silently changing the source parser's represented values.

The manifest has exactly these fields:

```json
{
  "schema_version": 1,
  "profile": "hgb_swing20_market28",
  "identities": {
    "AAPL": {"mic": "XNAS", "kind": "Common Stock", "currency": "USD", "timezone": "America/New_York"},
    "JNJ":  {"mic": "XNYS", "kind": "Common Stock", "currency": "USD", "timezone": "America/New_York"},
    "JPM":  {"mic": "XNYS", "kind": "Common Stock", "currency": "USD", "timezone": "America/New_York"},
    "MSFT": {"mic": "XNAS", "kind": "Common Stock", "currency": "USD", "timezone": "America/New_York"},
    "XOM":  {"mic": "XNYS", "kind": "Common Stock", "currency": "USD", "timezone": "America/New_York"},
    "SPY":  {"mic": "ARCX", "kind": "ETF", "currency": "USD", "timezone": "America/New_York"}
  },
  "price_basis": {"stocks.csv": "splits", "monthly.csv": "splits", "spy.csv": "all"},
  "intervals": {"stocks.csv": "1day", "monthly.csv": "1month", "spy.csv": "1day", "vix.csv": "1day", "vix3m.csv": "1day"},
  "macro_units": "index_points",
  "prefixes": {"daily": "2010-01-04", "monthly": "2010-01-01"},
  "recursive_prefix_attestation": "complete_recursive_prefixes_supplied",
  "calendar": {
    "coverage_start": "2010-01-01",
    "coverage_end": "2026-11-12",
    "attestation": "independently_supplied_expected_us_sessions"
  },
  "files": {
    "stocks.csv": {"bytes": 1, "sha256": "REPLACE_WITH_ACTUAL_LOWERCASE_SHA256"},
    "monthly.csv": {"bytes": 1, "sha256": "REPLACE_WITH_ACTUAL_LOWERCASE_SHA256"},
    "spy.csv": {"bytes": 1, "sha256": "REPLACE_WITH_ACTUAL_LOWERCASE_SHA256"},
    "vix.csv": {"bytes": 1, "sha256": "REPLACE_WITH_ACTUAL_LOWERCASE_SHA256"},
    "vix3m.csv": {"bytes": 1, "sha256": "REPLACE_WITH_ACTUAL_LOWERCASE_SHA256"},
    "expected-sessions.csv": {"bytes": 1, "sha256": "REPLACE_WITH_ACTUAL_LOWERCASE_SHA256"}
  }
}
```

The dates above illustrate shape only. Every size/hash must describe the actual bytes, and the caller must retain the expected manifest SHA outside this bundle. Duplicate JSON keys, nonfinite values, wrong scalar types and unknown fields at every object level fail. Identity, adjustment, interval, prefix completeness and calendar provenance are explicit caller attestations; hashing does not authenticate the upstream source.

## Cutoff, recursive history and timing

`as_of_utc` must be an aware zero-offset datetime no later than the actual clock. The cutoff is the last supplied expected session strictly before its UTC date, preserving the existing conservative closed-day rule. Calendar coverage must include the as-of date and the daily seed; calendar rows must be unique, ascending weekdays within that coverage. Stock and SPY rows must equal its expected sessions from the daily prefix through cutoff, with all five stock identities and one SPY identity ending at cutoff. Extra/future/missing rows fail. Completeness and closure are relative to that attested calendar, not independently proved official session identity or exchange-close evidence.

Every stock's native monthly series must be consecutive month-start candles from the declared monthly prefix through EXACTLY the calendar month preceding cutoff. Partial current months and stale monthly context fail. Monthly candle availability is the calendar month end, used only on strictly later feature dates. The only supported existing profile pins the daily prefix to **2010-01-04** and the native monthly prefix to **2010-01-01**; a rehashed truncated bundle with a redefined seed is a different recipe and is rejected. The entire history seeds Wilder RSI; 252 bars or a four-year moving window cannot replace those recursive prefixes. The helper can verify the supplied prefix and its hashes, not certify that no upstream earlier history exists.

The 13 common features reuse `research_features(..., labeled_only=False)`; the 11 trailing swing formulas preserve the original operation order. Volatility sources are restricted to cutoff, reindexed to SPY sessions BEFORE shifting, and require all needed observations without filling. Index-only dates are counted and excluded before shifts. The research availability floor remains 2013-11-01. With SPY-session index `t`, market inputs are:

- `vix_level = VIX[t-2] / 100`
- `vix_change5 = VIX[t-2] / VIX[t-7] - 1`
- `vix_term_ratio = VIX[t-2] / VIX3M[t-2] - 1`
- `vix_term_change5 = term[t-2] - term[t-7]`

The returned metadata records actual internally generated `prepared_at_utc`, supplied as-of/cutoff, exact manifest/source/calendar/frame/context digests, attested prefixes, eligible row counts, latest monthly/macro observation dates and excluded index-only counts. It contains no fabricated retrieval timestamp. Preparation now with a historical as-of is historical reproduction, not evidence that these bytes or a model existed at that time. Current adjusted histories/index corrections, reported identities and calendar attestation do not certify historical point-in-time availability. Two-session macro lags do not change that limitation. This input stage produces no assessment or qualification result.

## Verification boundary

Synthetic tests cover the closed input contract, exact matrix parity, causal prefix invariance, UTC/month/calendar/macro boundaries, and unchanged source bytes/environment. A separately authorized zero-fit actual-artifact proof must compare every eligible 28-feature cell and all 1,085 retained 2026 probabilities and policy rows on the same context. No real-data proof or model export is performed by this guide or by preparation itself.
