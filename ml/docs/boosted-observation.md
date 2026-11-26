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
