# Fixed boosted 63-session horizon and exit-permission diagnostic

**Goal:** Continue the active user goal: >20% annual compound growth over long history, SPY outperformance, economically better sales and lower maximum drawdown; finish compatible ML commissioning only when supported. The preceding goal turn made verified progress to 18.6259% CAGR /24.7658% DD. Neither that result nor green accounting tests complete the goal.

## Decision and prior research

One new learner variant: change the original-five boosted classifier's prediction horizon from20 to63 sessions. Its monthly context and63/126/252-session momentum features make the duration plausible, but no evidence guarantees improvement. The old RF calibrated63 recipe was already tried on2015–2017:14.6797% CAGR /12.4093% DD,40 buys/zero sales, rejected by the old risk ceiling. This is another adaptive trial, not an entirely untried horizon or fresh holdout. The previous three-diagnostic batch remains closed and unchanged: domain18.6259%, regression13.1082%, leaf10016.2370%.

The continuous-return alternative changed objective and policy and was worse; larger leaves also failed. Keep those results. No parameter/threshold grid, new asset winners, exposure increase, leverage or hidden selection is added here.

## Exactly one model change

- Same AAPL/JNJ/JPM/MSFT/XOM histories,28 trailing features, fixed HGBT200/.05/15 leaves/L2=1/leaf20/early_stopping=False/seed42/OMP1, annual fits, four calendar years and equal total weight per eligible date.
- Use existing `target_63` and `target_date_63`: `open[t+64]/open[t+1]-1 > .002`. Validate the genuine64-observed-session known-date offset and the return/date/label pair. Require `target_date_63 < actual first_prediction`; retain the unknown trailing64-session outcomes for forecasting, not scoring.
- Exactly nine annual fits2018–2026 using `_walk_forward_predict(..., 'boosted_original_market_swing63', ..., 'boosted', 'target_63', 'target_date_63', FEATURES, 'probability_up', training_frame=features, date_weighted=True)`. No shared core, recipes registry, feature hash, panel or dependency changes.
- The score forecasts the63-session price-return event. It does not forecast20-session direction, downside severity or expected return, and does not impose63-session holding periods. Keep raw .65 BUY/.35 SELL and trend200 confirmations; daily model-approved orders may exit sooner.
- AUC/Brier use observed63-session labels and the ACTUAL annual, fully purged original-five63-session date-weighted prevalence. Never compare against/relabel20-session priors or call raw20/63 Brier differences calibration improvement. The compiled broader panel has no63-session target and supplies no new fitting rows or63 prior.

## Fixed accounts and explicit exit controls

All accounts start with100,000 USD without later external flows, on the same validated sessions2018-01-01 through2026-11-11.2026 is partial. Same genuine split-adjusted stock opens,176 actual gross dividends, delayed spendable payment cash,8dp shares,10% purchase/20% symbol limits at execution, no shorts/leverage/taxes/interest/final liquidation. Model baseline costs are5bps fee+5bps adverse slippage; higher-cost accounts use25+25 with the SAME forecasts and signal thresholds. SPY is the same adjusted ETF total-return proxy.

Copy the audited20-session reference (forecast SHA`f53007c0ad1de79ba3fe306c31a30c3299c9dd2454f6b9702c7fbf3040e68630`, independent proof SHA`80677e99b456ca8765c74770382d0181b7344c565450f4e39a8ea96c3a981eef`) and its three account results unchanged. Never refit/replay those model-enabled reference accounts or relabel their event. Old broader reference and every old rejection remain unchanged.

Run these NINE new account replays unconditionally, with no new fits beyond the nine63-session fits:

|Label|Forecast|SELL permission|Start|Fee+slippage bps|
|---|---|---|---|---|
|full-h63|new63|enabled|2018-01-01|5+5|
|full-h63-nosell|same63|disabled|2018-01-01|5+5|
|full-h63-higher-costs|same63|enabled|2018-01-01|25+25|
|full-h63-nosell-higher-costs|same63|disabled|2018-01-01|25+25|
|later-h63|same63 slice|enabled|2021-01-01|5+5|
|later-h63-nosell|same63 slice|disabled|2021-01-01|5+5|
|full-h20-nosell|cached20|disabled|2018-01-01|5+5|
|full-h20-nosell-higher-costs|cached20|disabled|2018-01-01|25+25|
|later-h20-nosell|cached20 slice|disabled|2021-01-01|5+5|

SELL-disabled controls remove only SELL decisions, preserve identical BUY date/symbol/score/allocation rows and use the SAME simulation/account rules. Subsequent purchase quantities legitimately diverge because cash/caps/holdings change. This measures the portfolio effect of permitting exits conditional on those forecasts, not isolated sale alpha. These no-SELL accounts are controls, never promoted strategies or substitutes for the user's genuine-sales requirement. No passive-basket benchmark or post-sale counterfactual is mixed into this trial.

Report enabled-minus-disabled CAGR, ending wealth, DD reduction, fees and exposure for each horizon/start/cost. Compare the20 and63 exit-policy effects without claiming target changes isolate individual sale quality. More transactions alone cannot pass the sales objective. Primary candidate conditions remain >20% full CAGR, >SPY and frozen20 return, full DD below frozen20, and genuine sales; report fresh2021>20/paired outperformance/DD versus SPY/sales independently. Exit-effect results and forecast reliability are separate evidence, never hidden by one aggregate flag.

## Freeze and verify

Private files under`ml/models/boosted-horizon63-20261112`; output must be new and refuse overwrite. Before fitting, commit/archive this plan, driver/helpers, imported sources, actual input/calendar/market and environment commitments, frozen20 forecasts/fit/account/input/audit evidence. Require its independent audit to pass with exact input/summary/forecast bindings. The six bound core source files stay unchanged. Existing loaders/account/positions/Fraction auditor helpers must be reused rather than introducing another public framework.

Synthetic checks: genuine64-session labels/censoring, strict known-date purge including equality and future labels, no target columns in features, nine exact fit dates/original-five populations/date weights/63 priors, missing/duplicate/nonfinite probabilities, unchanged threshold/trend decisions, and SELL suppression preserving every BUY unchanged. Independently verify all source/invocation commitments, nine raw fit populations and priors, probability-to-order traces and NINE complete cash/dividend/position/trade/SPY/annual ledgers without fitting or calling the producer simulator. Audit the exit-effect arithmetic separately.

All inspected2018–2026 history remains researcher-adaptive. Longer purging discards more recent training outcomes; overlapping63-session labels are highly dependent, fewer effective observations and higher positive prevalence may suppress sales. A positive63-session endpoint can still contain severe interim loss. Five surviving assets and current adjusted-price/index vintages limit generalization. `classification='adaptive_diagnostic'`, `research_qualified=False`, `promotion_authorized=False`; no future20% guarantee or runtime change.

- [x] Prepare/review the minimal private driver and pass synthetic checks; freeze all commitments before fitting.
- [x] Execute exactly nine causal63-session fits and all nine fixed accounts; preserve the cached20 reference.
- [x] Independently audit all accounts, priors and exit effects; publish every result in the existing draft PR.

## Post-study outcome

The prefit protocol committed as`97bb00b` remains archived unchanged in private evidence. Nine fits and nine accounts completed once. Eight synthetic checks and95,282 independent numeric ledger comparisons pass with zero ledger error; audit SHA`d9972a7e3fa4f2870f9dff17736c8e17ff638faf8c940310690946d77a8da3d9`. New63 full earns18.1581% CAGR /32.9671% drawdown, worse than the retained20 reference on both measures. Full no-SELL controls remain below20%; the fresh20 no-SELL20.4581% result is a shorter, zero-sale control. No promotion. The [complete outcome report](../../../ml/docs/boosted-exit-controls.md) preserves every account and effect.
