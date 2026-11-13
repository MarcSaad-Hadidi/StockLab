# Fixed explicit SPY-context upside representation diagnostic

**Goal:** Continue the user's full >20% long annual compound-growth, better-sales/lower-drawdown and functional ML objective. Neither successful accounting nor this offline experiment completes compatible artifact/inference/paper commissioning.

## One representation change, not a parameter search

The retained original-five upside20 model earns18.6259% CAGR /24.7658% maximum drawdown. The63-session experiment failed at18.1581% /32.9671%; five-session SELL confirmation trades higher19.1415% return for worse29.6675% DD. Preserve both experiments and their paired no-SELL controls. No horizon, confirmation-length, probability-cutoff or learner grid is added here.

Append ALL FOUR already implemented causal `downside.CONTEXT_FEATURES` to the original upside28-column prefix: `spy_momentum_20`, `spy_momentum_63`, `spy_close_ma200`, `spy_volatility_20`. The feature list is EXACTLY `SWING_FEATURES + market.FEATURES + downside.CONTEXT_FEATURES`,32 unique finite columns. Keep the old28 order as a prefix; do not use the differently ordered downside-feature list plus VIX.

These fields were used by earlier risk heads, not the completed upside trials. They are not wholly new information: stock momentum minus relative momentum already reconstructs the two SPY momentum fields. Explicit coordinates may help axis-aligned trees; SPY200-day trend and realized volatility20 add broad-market statistics absent from that identity. This is one fixed representation hypothesis, not a guarantee of calibration or20%.

Build the frame with existing `downside.make_frame(base, stocks, spy)` then `market.attach`. Its future downside labels/known dates are NEVER model features, fitting labels, filtering conditions or priors. The upside target remains `target_20` and `target_date_20`, with actual open[t+1]→open[t+21] price return>.002. Assert all original columns/keys/labels/known dates and all old28 feature values are exactly unchanged; unknown trailing21-session outcomes remain forecastable. Existing checks show16,345 rows and all nine eligibility populations remain identical; verify again before fitting.

## Frozen learner and evidence

One model variant, exactly nine annual causal fits2018–2026, original AAPL/JNJ/JPM/MSFT/XOM histories only, four calendar years and strict full known-date purge. Same equal total weight per date, raw HGBT200/.05/15 leaves/L2=1/minleaf20/early_stopping=False/seed42/OMP1 and all installed effective defaults. No calibration, source/feature-core/recipes/panel mutation, new dependency, provider request, new assets or exposure increase.

Call the existing fitting core with recipe`boosted_original_market_swing20_spy_context`, kind`boosted`, target20/date20, the EXACT32 features, `training_frame=frame32`, `date_weighted=True`. Verify all nine actual fit dictionaries/populations/weights/own priors equal the cached28 reference apart from recipe, and retain its unchanged broader20 annual prior as a second diagnostic. All Brier/AUC comparisons now refer to the SAME20-session event; report own and broader causal baselines, never future test prevalence. No scoring of unused downside labels.

Financial references remain immutable: original28 immediate-exit three accounts under`boosted-upside-domain-20261112/run-v1`, forecast`f53007c0ad1de79ba3fe306c31a30c3299c9dd2454f6b9702c7fbf3040e68630`, independently passed audit`80677e99b456ca8765c74770382d0181b7344c565450f4e39a8ea96c3a981eef`; matching original28 no-SELL accounts under`boosted-horizon63-20261112/run-v1`, audit`d9972a7e3fa4f2870f9dff17736c8e17ff638faf8c940310690946d77a8da3d9`. Their enabled accounts are not refitted or replayed. The five-session policy's unfavorable risk evidence, audit`8b9c122eba2be8792c095d77affbda69c11871c6aabe3b99137df3977967c482`, is preserved.

## Exactly six fixed new accounts

Retain the ORIGINAL immediate trend200 decisions: BUY p>=.65/positiveMA50; SELL p<=.35/negativeMA200/negative momentum20. Do NOT combine with the five-session confirmation trial. For each new32-score account, also remove only SELL rows and preserve every BUY key/score/allocation/metadata exactly; quantities can diverge later through holdings/cash/caps. Within-model exit-permission effects are interpretable; changing predictors also changes entries, so cross-model results are not isolated sale alpha.

|Account pair|Start|Fee+slippage|SELL variants|
|---|---|---|---|
|full-spy32 / full-spy32-nosell|2018-01-01|5+5bps|enabled / disabled|
|full-spy32-higher-costs / full-spy32-nosell-higher-costs|2018-01-01|25+25bps|enabled / disabled|
|later-spy32 / later-spy32-nosell|2021-01-01|5+5bps|enabled / disabled|

All six run unconditionally on the same sessions through2026-11-11;2026 is partial. Initial100,000 USD, no later deposits, same176 actual gross dividends and delayed payment cash,8dp quantities,10% purchase/20% symbol limits at execution, observed next-open fills. No shorts/leverage/taxes/interest/hypothetical final liquidation. SPY remains the adjusted ETF total-return proxy. Higher-cost accounts use the SAME model scores and decision thresholds.

Report CAGR, DD, wealth, fees, activity, exposure, annual returns and matched exit effects versus no-SELL. Compare the enabled32 candidate to both frozen28 enabled and no-SELL controls without promoting a zero-sale control. Component flags include full >20%, >SPY/>18.625926%, strictly lower DD than the current24.765767% reference, genuine sales; fresh2021>20%/paired outperformance/DD versus SPY/sales are separate. Also retain the original broader16.173890%/31.914731% reference as historical context, not a replacement for current comparison. No post-result composite, new admission or relabeling of earlier failed gates.

## Freeze and independent verification

Private files under`ml/models/boosted-spy-context-20261112`; output refuses overwrite. Commit/archive this plan, driver/helpers, current imported sources, exact32 feature list and frame parity, both immutable reference trees/audits, original inputs/calendar/market/panel provenance and environment BEFORE any fitting. Six bound core sources and existing training panel remain unchanged. The panel is lineage/reference-prior evidence, not a changed training corpus.

Synthetic checks must reject reordered/duplicate/extra/target columns, changed old28 values or target populations, missing/nonfinite context/scores, leaked known dates and altered BUY rows under SELL suppression. Verify trailing SPY formulas with the existing builder, no future-risk-label filtering, full forecast coverage including unknown tails and exact nine fit years. Reuse fitting, account, positions and independent Fraction ledger helpers; no public framework or source-guard bypass.

An independent auditor performs zero fitting or producer simulations, verifies old28 bit parity and all four new causal fields, all nine fit sets/priors, probability-to-order mapping, complete SIX ledgers/positions/dividends/fees/caps/SPY/annual returns and all paired comparison arithmetic. Publish every result. All history remains researcher-adaptive with five correlated survivors, dependent outcomes and current adjusted-price/index vintages. `classification='adaptive_diagnostic'`, `research_qualified=False`, `promotion_authorized=False`; no runtime change or future20% guarantee.

- [x] Prepare/review the minimal private representation driver and pass synthetic checks; freeze commitments.
- [x] Execute exactly nine fits and six fixed accounts, preserving every reference.
- [ ] Independently audit and publish all outcomes, exit effects and commissioning limitations.

## Post-study outcome

The prefit protocol committed as`4fc4429` remains archived unchanged. Nine fits and six accounts completed once. Eight synthetic checks and63,435 independent numeric ledger comparisons pass with zero ledger error; audit SHA`c839b38dc380e597564907ebff92440df34d7213919762a4f1c542e3c63ff06a`. Full earns17.9741% CAGR /23.0973% drawdown, reducing risk while losing return versus original28. Fresh2021 earns13.8524%/14.1838% and underperforms SPY. Neither20% flag passes and no model is promoted. The [complete outcome report](../../../ml/docs/boosted-exit-controls.md) records every paired comparison and weak forecast scores.
