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

## Completed historical artifact proof

At source commit `68cbb78`, one private proof prepares **16,345 rows / 457,660 feature cells**, all bit-identical to the frozen reference projection. The full feature digest is `493cab6362614bfbc534b35715b7e208822049b874db370c71c207844bbbd08d`; its 2026 subset is 1,085 rows / 217 sessions, digest `c5430a5f3c4e6c115f4fd9f23f362079c0a2e0f508c01e61174bdd8821d3cb1c`.

The existing real Archived artifact then returns every 2026 probability exactly and reproduces all policy rows using the SAME prepared context: 215 BUY, 14 SELL and 856 HOLD. These are historical signal rows, not newly executed portfolio trades. Explicit failing guards on HGBT fitting, calibration fitting and account simulation observe **zero calls**. The original binary, isolated registry, RFv7 registry/two inspected configs, all 53 earlier research sources and 84 snapshot bindings remain unchanged.

The source-bound normalized bundle retains the original daily/monthly recursive prefixes. Its 1,010 native monthly bars end October 2026; five incomplete November bars are excluded explicitly. Re-serialization of the actual legacy-decoded OHLCV values is independently recorded, with zero numerical differences versus the new round-trip decoder on these inputs. No feature or score is rounded to make parity pass. The external normalized manifest SHA is `6861452a69eddae7fc58a9742cb7d744fa1b99f4c825392d8fb4a62c3ba5c646`.

The old validated calendar remains byte-identical and covers only November 11. A NEW derived calendar adds November 12 as a scheduled Thursday under the current [official NYSE calendar](https://www.nyse.com/trade/hours-calendars), retained with actual November 13 UTC capture hashes. This is an explicit schedule inference, not extraordinary-closure or executable-open certification. The historical as-of November 12 uses the strict cutoff November 11; a November 13 as-of with this unchanged bundle is rejected as stale.

Actual training occurred November 13 at 02:01:59.101472 UTC, AFTER the historical November 12 as-of. Preparation/capture timestamps are current UTC, never backdated. This is historical reproduction, **not prospective evidence**; PENDING remains appropriate. No current worker, active model, acquisition process or authenticated UI is commissioned by this proof.

Local new-plus-adjacent verification passes 138 tests with two Windows symlink skips and no warnings. The complete ML suite passes **2,251 tests with three Windows skips and five known older calibration warnings**, in 550.93 seconds; source/test hashes are unchanged.

The independent proof audit passes at its first execution. It recalculates all28 features from normalized OHLCV/monthly/volatility sources, checks all457,660 cells against both the new output and old export, verifies1085 scores against both export and original annual forecasts, and derives every policy action/reason independently. Raw-to-normalized/manifest/proof bindings, current NYSE capture, complete calendar, actual UTC timestamps,105 unknown-outcome tails,53/84 sources and seven protected files pass. There are zero auditor fits, account simulations, preparer/producer calls or joblib/pickle loads. Private evidence is under `models/boosted-vintage-20261112/proof-v1`; independent-audit SHA `6a4bb12b9a162851ee0309d0773c442979e5214f9b0a6512a94d9145cdf519f6`. The pre-proof protocol remains unchanged.
