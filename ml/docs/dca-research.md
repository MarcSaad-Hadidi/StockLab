# DCA / S&P 500 research — 11 November 2026

The target is **at least 20% compound annual growth (CAGR), exceeding an S&P 500 proxy after costs**. This experiment does **not** establish that the deployed ML model achieves that target. It adds an offline, reproducible evaluation; it does not change live inference, risk limits, the registry or promotion rules.

## Results and decision

Fixed-capital paper accounts start with $100,000, without further contributions. Prices are genuine cached Twelve Data exports. Returns include simulated 5 bps proportional fees plus 5 bps adverse slippage on every purchase and sale; open positions are marked at the final close, without hypothetical liquidation. Taxes and interest are excluded.

| Recipe |2025-01-01 through 2026-11-11 CAGR|Maximum drawdown|Executed orders|
|---|---:|---:|---:|
|Annual walk-forward seven-feature Random Forest|18.18%|11.82%|164 (127 buys, 37 sells)|
|Regular monthly DCA, adapted to the same risk caps|20.94%|4.81%|62 buys|
|User's monthly range/RSI DCA, adapted to the same risk caps|22.98%|5.47%|55 buys|
|SPY provider-adjusted buy-and-hold proxy|15.78%|—|One initial purchase|

The DCA rule clears the numerical target on this 679-day interval. Its total return is 46.89%, not 22.98%; 22.98% is the annualized compound rate. These orders are **backtest orders**, not actual executions in the live portfolio. DCA is a buy-only allocation rule, not an ML model or a calibrated probability forecast. Most gains remain unrealized.

The wider-history check prevents treating the favorable interval as a durable result:

|Fixed rule test|Range/RSI DCA CAGR|SPY proxy CAGR|Target met?|
|---|---:|---:|---|
|2023-01-01 through 2024-12-31|14.60%|25.16%|No|
|2023-01-01 through 2026-11-11, one continuous account|18.90%|20.74%|No|
|2025-01-01 through 2026-11-11, 25 bps fee +25 bps slippage per side|22.72%|15.53%|Yes, on this interval only|

The longer continuous DCA account has 17.56% maximum drawdown. Its cash is largely invested during the first year; a buy-only fixed-capital strategy eventually has little cash available for new purchases. It should not be presented as perpetual active trading.

**Decision:** retain the active production RFv7. None of the enriched ML candidates improved the predeclared selection result. The DCA rules are promising research baselines, but do not consistently outperform SPY and are not deployable through the existing ML-confidence/artifact contract. No promotion or forced purchase is justified by these results.

## Frozen experiment

The protocol was written in [the plan](../../docs/superpowers/plans/2026-11-11-dca-research.md) before model fitting:

- Universe remains AAPL, JNJ, JPM, MSFT, XOM. The same five stocks and eligible feature rows are used by every ML candidate. They are surviving stocks, not a historical point-in-time market universe.
- Select on 2024 only, then freeze the recipe before evaluating 2025–2026-11-11. Each annual fit uses up to four prior calendar years and excludes targets on/after its first prediction day. Monthly warm-up makes the common fit history start 2022-08-01, so this is the RF **recipe**, not a retrospective application of the already-trained RFv7 artifact.
- Fixed candidates: original seven-feature RF; normalized daily + monthly DCA RF; normalized daily + monthly DCA Logistic; normalized daily + monthly DCA HistGradientBoosting. Existing RF/Logistic parameters are reused; boosting uses 200 iterations, learning rate 0.05, 15 leaves, L2=1, seed 42, no early stopping. No parameter search.
- Candidate selection requires higher net CAGR and maximum drawdown no more than 0.002 above the baseline (the user's accepted 0.2 percentage-point research tolerance).
- On 2024, baseline RF CAGR 9.94%/DD 3.09%; enriched RF 7.63%/3.55%; enriched Logistic 5.29%/2.35%; enriched boosting 8.71%/4.30%. All fail to improve the baseline, which is retained. The rejected candidates' final-period returns are not calculated.
- Rule baselines were declared separately. Their favorable final results were not used to override the ML selection gate. The earlier-period and higher-cost checks are additional exploratory stress checks after the primary run, not new optimization/selection periods.
- Prior exploration of these symbols means the historical evaluation is not a pristine, previously unseen prospective test. This period also lacks the full 2022 bear market because monthly indicators need warm-up.

## Reuse of the DCA project

The user's repository was inspected at [DCA-Optimizer commit d0c8f31](https://github.com/MarcSaad-Hadidi/DCA-Optimizer/tree/d0c8f31f486cf16ef21deb1f1b57645a3064383a). Its 38 signal/portfolio/reference tests pass locally. The port uses the pure [range_rsi_v1 signal rules](https://github.com/MarcSaad-Hadidi/DCA-Optimizer/blob/d0c8f31f486cf16ef21deb1f1b57645a3064383a/lib/core/signals.ts): Wilder RSI 14 and persisted profiles 12/24/36 corresponding to actual lookbacks 9/12/19. Profile 24 supplies the allocation multiplier: 4x/3x/2x/1x in the original range bands; RSI>=80 conserves cash.

All 820 available confirmed symbol/profile signals match the original TypeScript engine within 2.14e-14 for RSI/range, with exact profile 24 multipliers. Incomplete November monthly bars are excluded. Monthly indicators become usable strictly **after** calendar month-end, with no low-of-month retrospective fills or future swing anchors.

The adapted funded account uses initial reserve only, a requested monthly base allocation of initial capital /12 /5 per stock, multiplied by the rule. Its first observed daily decision of each month executes at the **next observed open** (a conservative extra session after the original funded-open method). Every order is capped again at execution: 10% of portfolio equity per purchase, 20% symbol exposure including execution friction, 10 positions, no shorts/leverage. Market drift can subsequently increase a holding's weight. ML buys/sells require directional score>=0.65; rule baselines have no fabricated confidence score.

The original `same_close_v1` retrospective reference backtest is not used as evidence of executable performance. Its variable funding and same-close signals are unsuitable for this fixed-capital CAGR comparison.

## Benchmark and data limitations

[SPY seeks to track the S&P 500's price and yield performance](https://www.ssga.com/us/en/individual/etfs/state-street-spdr-sp-500-etf-trust-spy). It is a proxy with tracking differences and fund expenses, not the exact S&P 500 Total Return Index. S&P's [index methodology](https://www.spglobal.com/spdji/en/documents/methodologies/methodology-index-math.pdf) includes reinvested dividends in total-return indices.

The benchmark export requests Twelve Data `adjust=all`, whose daily series actually differs from `adjust=splits`; it is labeled a **provider-adjusted total-return proxy**. Its adjusted historical open/close ratios are used only for a buy-and-hold comparison, never as executable model trade prices. Strategy stock fills/marks use split-adjusted prices and **exclude dividend cash**, while the SPY proxy includes the provider's dividend adjustment. This asymmetry is conservative for the stock strategy, but it is not an exact dividend-consistent accounting comparison. Provider adjustment vintages and dividend methodology were not independently reconstructed.

All accounts use exactly the same observed valuation dates and initial capital. The initial cash-only date is a funding point, not an invented price observation. No interpolation is applied. CAGR is `(final/initial)^(365.2425/calendar_days)-1`; the target gate requires at least 365.2425 days and strict outperformance. A two-month backtest cannot pass that gate.

The local research folder retains raw sources, provider parameters, acquisition timestamps, SHA256 manifests, selection evidence, fitted-date boundaries, predictions, fee-bearing orders and equity curves. No API key, market export, trained artifact or personal portfolio is committed. ML source acquisition used the already-authorized fallback key; the fixed experiment itself uses no network.

## Reproduce offline

From `ml`, with the existing environment and `PYTHONPATH=src`, run:

```powershell
.\.venv\Scripts\python.exe examples/dca_research.py --daily models/portfolio-daily-20261106/data/2026-11-11/cleaned.csv --monthly-dir models/dca-benchmark-20261111/sources --spy models/dca-benchmark-20261111/sources/SPY-1day-all/cleaned.csv --out models/dca-benchmark-20261111/new-run
```

The output directory must be new. Monthly and SPY sources require sibling `manifest.json` files whose symbol/interval/adjustment and cleaned CSV hashes match. The daily source requires its original cycle snapshot manifest, matching lineage and CSV hash; its split-adjusted basis follows the cycle ingestion contract. Truncated monthly histories fail instead of carrying stale indicators into later months. Selection is saved and hashed before any final-period fit. Summary evidence also hashes the executable research source files. To repeat the stress checks, use `research.simulate` with the example's `rule_decisions` on the dates above; the retained local `models/dca-benchmark-20261111/stress.py` performs those exact calls.

Further work toward a defensible 20% target needs longer point-in-time data including difficult market regimes, consistent dividend accounting, a portfolio allocation/exit policy compatible with audited live execution, and a prospective paper validation. Adding more features alone did not improve the tested ML recipe.
