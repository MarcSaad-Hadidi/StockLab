# Fixed monthly boosted28 cadence diagnostic

**Goal:** Continue the full >20% long annual compound-growth, better exits/lower drawdown and functional ML objective. Offline diagnostics and compatible-model plumbing are complementary work, not substitutes for the full objective. Active runtime remains RFv7.

## One cadence change

The retained annual original-five boosted28 model earns18.6259% CAGR /24.7658% DD. The63-session target, five-session exit confirmation and explicitSPY32 representation did not meet the joint goal. Preserve all outcomes:63full18.1581% /32.9671%, confirmation19.1415% /29.6675%, SPY32full17.9741% /23.0973% with fresh13.8524% below SPY. Do not tune those variants after seeing results.

Test ONLY annual→monthly fitting of the original28-feature20-session learner. Long cash/reentry gaps motivate testing recency; they do not prove monthly alpha. The earlier RF monthly study already failed on2018–2020: control19.6974% /33.0899% DD,49 buys/one sale; dual18.7428% /26.8159%,43 buys/eight sales; both rejected with no later results. It used different learner/features/risk-head context, but means freshness is not a new universal solution.

Use the existing `_walk_forward_predict` with recipe`boosted_original_market_swing20_monthly`, kind`boosted`, `target_20`/`target_date_20`, EXACT `SWING_FEATURES + market.FEATURES` original28 columns, `refit_frequency='monthly'`, `training_frame=features28`, `date_weighted=True`. No SPY32 inputs, persistence5, new risk head, calibration, thresholds, assets or parameter grid. Same HGBT200/.05/15leaves/L2=1/minleaf20/early_stopping=False/seed42/OMP1 and installed defaults. No shared source/panel/dependency/provider change.

## Exact chronology and January control

One complete core call fits EXACTLY107 observed prediction months2018-01 through2026-11. Each boundary is its first actual SPY session. Use source.date>=first−DateOffset(years4), source.date<first, target_date20<first and a finite known binary label. Both classes and exactly the five original identities are required; inverse eligible-date counts normalized to mean1 are the weights. A generic21-row gap is invalid with five assets. Forecast the full11,140 date×symbol rows, including105 target-unknown tails; only diagnostic scoring censors unknown outcomes.

January's source populations, labels,21-session known-date bounds, weights, actual fitting priors and28 inputs must match the frozen annual model for all nine years. Before ANY account replay, compare the915 January forecast rows to frozen annual rows at identical keys: predeclared rtol0/atol1e-14, record maximum difference and bit-exact count, and require EXACT BUY/SELL/HOLD parity with unchanged trend context. Do not round/snap scores or change tolerance/thresholds if this fails. January account fills/wealth need not match after prior monthly paths change holdings/cash. All107 are new candidate fits; original annual reference files/accounts are never recomputed or mutated.

## Priors and diagnostic scope

The primary Brier reference is EACH actual monthly original-five purged fitting prevalence, keyed byYYYY-MM, including the November model whose future outcomes are unknown. Keep frozen BROADER ANNUAL20-session prevalence keyed by year as a separate explicitly annual/stale-within-year causal same-event comparator. Never map monthly priors by year, reuse downside-target reports or substitute a favorable reference. Same20-event outcome keys make raw candidate/annual AUC and Brier comparable; report own-prior performance and do not claim calibration from model fitting alone.

Record a complete107-row month table: forecast/known-outcome counts; model/own-prior/annual-reference Brier; candidate versus frozen annual BUY/SELL intents and full-account exposure. Months with zero known outcomes retain rows with null loss metrics. No hand-picked late2024 table, extra strategy or extra fits. Cadence changes recency, moving training-window endpoints and empirical priors together; describe this combined effect accurately.

## Six fixed accounts and immutable references

Same100,000 USD initial cash without later contributions, five original stocks, sessions2018-01-01..2026-11-11 and fresh2021-01-01..2026-11-11, with2026 partial. Same actual opens,176 gross dividends/delayed cash,8dp sizing,10% purchase/20% symbol limits at execution, no shorts/leverage/taxes/interest/final liquidation. Keep immediate trend200 BUY p>=.65/positiveMA50 and SELL p<=.35/negativeMA200/momentum20; monthly fitting does not create monthly-only orders or account resets.

Run six accounts unconditionally: `full-monthly` /`full-monthly-nosell` at5+5bps; `full-monthly-higher-costs` /`full-monthly-nosell-higher-costs` at25+25bps on SAME scores/thresholds; `later-monthly` /`later-monthly-nosell` fresh2021 at5+5bps. Within-model no-SELL removes only SELL intents and preserves every BUY row. Later executed shares/cash can differ through holdings/caps; these controls are never zero-sale candidate replacements.

Copy original annual28 immediate three accounts (forecastf53007c0ad1de79ba3fe306c31a30c3299c9dd2454f6b9702c7fbf3040e68630; audit80677e99b456ca8765c74770382d0181b7344c565450f4e39a8ea96c3a981eef) and matching annual28 no-SELL accounts from the horizon study (auditd9972a7e3fa4f2870f9dff17736c8e17ff638faf8c940310690946d77a8da3d9). No reference fitting or enabled account replay. Preserve later failed-study proofs8b9c122eba2be8792c095d77affbda69c11871c6aabe3b99137df3977967c482 andc839b38dc380e597564907ebff92440df34d7213919762a4f1c542e3c63ff06a.

Report all six account metrics/annual returns and nine effects: three within-monthly exit-permission comparisons and six enabled-monthly-versus-old enabled/no-SELL references. Cadence changes entries too; cross-model differences are not isolated sale alpha. Component flags remain full>20%, >sameSPY/>18.625926%, DD<24.765767%, genuine sales; fresh>20%/paired outperformance/DD versus SPY/sales separately. No short-period, zero-sale or riskier result silently substitutes for the requested end state.

## Freeze and verification

Private driver under`ml/models/boosted-monthly-upside-20261112`; exclusive output refuses overwrite. Commit/archive this plan, driver/helpers, current imported unchanged sources, original28 data/feature/target parity, actual input/calendar/market/panel provenance, both independently passed reference trees and environment BEFORE fitting. Six bound core files and every old study remain unchanged.

Synthetic tests cover107-month boundary validation, per-month own priors versus annual reference, true known-date purges rather than row gaps, future exclusion, January comparison tolerance/action invariants, missing/duplicate/nonfinite score/context rejection, exact within-model BUY preservation and zero-known-outcome monthly rows. Independent audit performs ZERO model fits or producer simulations, verifies all107 exact causal fit populations/weights/priors, January parity, complete107-month diagnostics, all six ledgers/positions/dividend timing/fees/caps/SPY/annual returns and nine effect formulas.

All history is researcher-adaptive, with five correlated survivors, overlapping labels/fits and current adjusted-price/index vintages. Frequent refits can increase variance, boundary jumps, churn and cost, and do not guarantee forecasting skill or20%. `classification='adaptive_diagnostic'`, `research_qualified=False`, `promotion_authorized=False`. No runtime activation; newly fitted snapshots cannot borrow recipe-level validation as their own heldout outcomes. Genuine artifact/registry/inference/catalog support and truthful pending forward evidence remain separate full-ML commissioning work.

- [x] Prepare/review the minimal monthly driver and pass synthetic checks; freeze commitments.
- [x] Execute exactly107 causal candidate fits, verify January invariants, then all six fixed accounts.
- [x] Independently audit and publish every result, prior, month diagnostic and exit effect.

## Post-study outcome

The prefit protocol committed as`1942c53` remains archived unchanged. All107 fits and six accounts completed once. Eight synthetic checks and63,830 independent numeric ledger comparisons pass with zero ledger error; audit SHA`b8393bd58c059e64e75e0ff5094869101309cdfcb56bb99a263462cf4db7a28e`. All915 January scores are bit-identical with zero error and exact action parity before replay. Full monthly earns16.6715% CAGR /33.8450% drawdown; fresh2021 earns16.3994%/15.1208%. Both are worse than annual28 on return and drawdown. Full enabled DD also exceeds SPY. All107 month diagnostics remain, including November's40 forecasts/zero known outcomes/null losses. The [complete report](../../../ml/docs/boosted-exit-controls.md) preserves every result. No20% pass or promotion; retain the annual reference.
