# First prospective boosted observation

The genuine Archived `hgb_swing20_market28` v000001 produced five scores for the **November 25, 2026** session before the next scheduled open. The original audited .NET binary and a new isolated build from the updated `develop` integration both returned the same outputs apart from their actual preparation and inference timestamps. The output is eligible for prospective scoring and remains execution-ineligible. All five actions are HOLD. AAPL's low score was blocked by the sell trend condition; the other four held at their score thresholds. The automated paper cycle continues to use RFv7.

The new closed vintage appends 50 stock bars from the manifest-bound local paper-cycle cache, ten SPY bars from one bounded Twelve Data request and ten values each from the public Cboe VIX and VIX3M histories. The October monthly context remains byte-identical. The archived history through November 11 supplies the recursive prefix. New source revisions that overlap its final day are preserved as evidence: five stock rows differ across seven OHLC cells and five volumes, and SPY differs on low and volume. The earlier overlapping rows align exactly. This is a recorded append-only composition of two acquisition vintages. [Twelve Data historical requests](https://support.twelvedata.com/en/articles/5214728-getting-historical-data), [Cboe historical volatility data](https://www.cboe.com/tradable_products/vix/vix_historical_data).

Both invocations occurred on November 25 in Toronto. The acquisition was bound at `2026-11-26T04:15:45.766628Z`. Inference completed at `04:16:33.034783Z` and `04:16:37.369256Z`, after actual model creation on November 13 and before the scheduled November 27 regular open at 14:30 UTC. Thanksgiving closes the market on November 26, and November 27 has an early close. The timing uses the exchange schedule and the actual local clock. [NYSE holiday and trading hours](https://www.nyse.com/trade/hours-calendars).

The manifest SHA is `640bf480d818d3c57080755488cd58374b495c18374405668ebb115ea100d3d1`. The verification receipt SHA is `8483888af8c89907c9675a6cc1b8a278bdb188257c8b33e1cc072a1f5cb81e1d`. Private input responses, the exact stock normalization and overlap records, actual UTC times, both diagnostic payloads and both assembly sets' hashes remain under `models/boosted-prospective-20261125/acquisition-v1`. The proof records one observation date and five scores. Performance remains pending.

## Check outcomes using the recorded scores

`examples/check_boosted_observation.py` reads the existing diagnostic payload and requires the SHA-256 recorded at capture. It revalidates the bound capture and outcome vintages, the exact feature and trend context, actual training/inference ordering and inference before the scheduled regular open. Supply an outcome vintage populated as of an actual later time. For Windows from the repository root, use the existing ML environment:

```powershell
$mlSourcePath = Join-Path (Get-Location).Path 'ml/src'
$savedPythonPath = $env:PYTHONPATH
$observation = 'C:\path\to\captured-operator.json'
$observationSha = 'paste-sha256-from-the-capture-receipt'
$capturedBundle = 'C:\path\to\captured-bundle'
$laterBundle = 'C:\path\to\later-bundle'
$laterManifestSha = 'paste-sha256-from-the-later-bundle-receipt'
$laterAsOf = 'paste-actual-UTC-as-of-in-ISO-format'
try {
    $env:PYTHONPATH = $mlSourcePath
    & ml/.venv/Scripts/python.exe -B ml/examples/check_boosted_observation.py `
        --observation $observation --observation-sha256 $observationSha `
        --captured-bundle $capturedBundle --outcome-bundle $laterBundle `
        --outcome-manifest-sha256 $laterManifestSha --outcome-as-of-utc $laterAsOf
} finally {
    $env:PYTHONPATH = $savedPythonPath
}
```

The checker returns `PENDING`, no outcomes and the number of observed subsequent sessions until the 21st subsequent open becomes available. The real November 25 observation currently returns zero subsequent sessions. At maturity it uses the same split-adjusted price event as the training label, `open[t+21]/open[t+1]-1 > .002`, and computes a descriptive loss over the five fixed scores. It fails closed if the observed entry date differs from the scheduled date recorded at capture. This result applies to one observation, and the prospective performance assessment still needs multiple matured dates. The command is read-only and imports the existing feature preparer to validate source bytes.

## Score from a completed local paper-cycle snapshot

Pass `--outcome-cycle-snapshot` with the published snapshot folder, its manifest SHA-256, `--completed-session-ledger` and `--completed-session-ledger-sha256`. The snapshot supplies observed split-adjusted opens and the completed session ledger binds its exact cleaned data. The original forecast, captured vintage, actual pre-open cutoff and five scores still receive all checks. Every symbol must cover each expected market session through the outcome cutoff. A missing internal session, a still-running cycle or mismatched source hashes causes rejection.

The real November 25 forecast was checked from its completed native snapshot on November 26 at 00:22 Toronto time. It is `PENDING` with zero subsequent sessions and a November 27 expected entry. Five bound files remained byte-identical. The private direct invocation receipt SHA-256 is `d4b5185585bb13b638f13d13358ecc6bfdbaf3e83e6cb68ec80dd6f8e1022e13`.

On the local Windows installation, `StockLab-ML-Boosted-Outcome-Check` runs daily at 21:45 Toronto time under the signed-in user. It reads the most recent completed session for the existing paper cycle and writes at most one outcome receipt per session. It retries on the next scheduled invocation if the snapshot has yet to be published. After the first matured result it reuses that receipt. The observed manual task invocation completed with return code 0 and reused the same pending receipt, SHA-256 `628d6fdadec625c39fe0b1ad38e1f4cace17d864ec14242f8a354b724ef61b02`. The task uses local files and zero provider requests. Both existing Windows ML task definitions matched their previous exports. The private launcher and receipts are under `models/boosted-native-outcomes-20261126`. Automatic capture of future boosted forecasts remains a separate outstanding operation.
