# Dedicated Downside ML Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for the independent model and runner tasks, with a fresh final review. This document freezes the experiment before new strategy returns.

**Goal:** Improve actual ML exits and risk-adjusted participation toward >=20% annual CAGR and SPY outperformance, without changing confidence thresholds, leverage or purchase limits.

**Architecture:** Compare the existing recent raw RF20/trend200 entry strategy against the same forecasts combined with one dedicated downside RF and direct trailing SPY context. Reuse the unchanged shared cash simulator with `risk_policy=None`; this tests learned exits instead of adding another fixed trailing/regime/cooldown rule. This is a combined target/context/decision hypothesis, not a pure label ablation.

**Tech Stack:** Existing Python/pandas/scikit-learn/pytest, no new dependencies.

**Spec:** User's active ML goal and the evidence in `ml/docs/long-horizon-ml.md` / `ml/models/risk-exits-20261112/progress.md`.

## Frozen design and constraints

- Production remains RFv7. No changes to live artifacts, native tasks, configuration, personal portfolios, database, broker orders or paid services. No new API data needed.
- Evidence motivating this experiment: recent raw RF20 has pooled AUC .5078; eligible BUY mean score .7195 versus realized endpoint hit rate .5515. A positive endpoint can still suffer a 5% intervening loss. These descriptive, overlapping-label diagnostics do not establish tradable edge.
- Exactly two arms: (1) `control`: frozen recent `rf_swing20` scores + existing trend200 confirmation, with no optional hard risk policy; (2) `dual`: identical entry scores plus the dedicated head below. Neither arm uses the previously tested hard15/SPY200/20-open cooldown. Existing .65/.35 model floors, 20% entry exposure,10% purchase allocation, ten positions, no shorts/leverage and next-open actual-price fills remain unchanged.
- Inputs remain the exact five-stock2010+ snapshot and176 verified2018+cash events. Frozen entry predictions come from `risk-exits-20261112/entry-run-v1`, with prior input/selection/source commitments and exact date/symbol/score coverage verified. This avoids retraining the entry head or choosing another recipe.
- Dedicated label: `target_price_downside20[t] = 1` iff `min(close[t+1],...,close[t+20])/open[t+1]-1 <= -.05`; known date is `date[t+20]`. Require all20future closes, otherwise NaN/NaT. Exclude the entry overnight gap. It is split-adjusted PRICE risk, not economic-wealth drawdown: complete pre-2018 dividend history is unavailable. Do not invent earlier dividends.
- Downside features: existing SWING_FEATURES plus SPY20/63-session close momentum, close/SMA200-1, and20-session close-return standard deviation(ddof0). All trailing dimensionless values; no raw price levels or future values.
- Downside estimator: the existing regularized RF recipe,300trees,depth8,min_leaf20,sqrt,seed42,n_jobs1, no class weights/resampling/calibration. Annual fit uses at most four prior calendar years, excludes every label whose known date >=firstprediction. Both training classes required. Reuse extracted private fitting core; preserve original public recipe sets, probability names, default predictions and audits. Dedicated scores are `probability_downside`, never `probability_up` or `1-p_up`.
- Dual decisions: downside score >=.65 schedules SELL regardless of upside/trend. Otherwise original model-approved SELL remains valid. BUY requires original upside>=.65/positiveMA50 AND downside<=.35. Intermediate downside blocks new BUY but leaves holdings alone; no extra timer. SELL overrides BUY, one decision per date/symbol. Save `up_model` / `downside_model` reason and both raw scores beside decisions; execution uses the existing four-column decision contract. Count only actual executed sales after joining audited decision reasons.
- These are raw directional scores, not validated probabilities or a joint probability of profit. The entry veto may recreate cash lock; report annual eligibility, invested exposure and longest cash-only stretch, and do not relax thresholds after seeing returns.
- Same $100,000 independent accounts/no deposits,5+5bps per side, actual cash dividends/receivables, no taxes/interest/final liquidation; selected higher-cost replay25+25bps.

## Selection and evaluation, frozen before calculation

1. Save exact input/code/prior-source hashes and a protocol copy before fitting/simulation. Preserve code snapshots using repository-relative paths. Refuse existing output directories.
2. Fit downside predictions ONLY2018-01-01–2020-12-31 first. Save scores/fit boundaries/training positive rates; save both arms' decisions before selection returns. Compute risk AUC/Brier only for labels whose known date <=2020-12-31. Brier reference is each annual fitted training positive rate known before prediction, never future selection prevalence.
3. Compute paired selection control/dual returns. Admit dual only if CAGR>=control, strictly lower maximum DD than control, positive executed downside-model sales, downside AUC>.5 and Brier strictly better than causal reference. Also require CAGR>the protected raw-RF incumbent's exact saved9.772968% selection CAGR and DD<the original unprotected calibrated candidate's exact32.681374% selection DD. These extra comparisons keep the user objective in view; passing is not production approval.
4. Independently admit control only if selection CAGR>=20%, above same-period SPY, DD<original32.681374%, and positive executed original model sales. A simpler successful control is not discarded merely because the new head fails.
5. Persist/hash both admissions before any later downside fits or strategy returns. If neither passes, record rejection and stop without later/full returns. If dual passes, fit downside2021–2026 and evaluate dual with paired control; if only control passes, evaluate control without later downside fitting. Do not select another arm after final results.
6. Report independent later2021-01-01–2026-11-11 and full2018-01-01–2026-11-11 accounts, annual returns/exposure, buys/sells by actual model reason, full target criterion, forecast diagnostics, and selected-arm25+25bps sensitivity. Preserve all fit/score/decision/equity/trade/benchmark evidence.
7. Goal pass requires >=20% CAGR, SPY outperformance and genuine model sales on BOTH full and later periods; full DD<original32.6814%, later DD<same-period SPY DD. The later risk reference is explicitly SPY rather than the original mostly-cash reset: the latter's low DD measures non-participation and is unsuitable for commissioning a participating portfolio. Report paired-control DD honestly as well; no quiet benchmark switch.
8. All history has already been adaptively inspected. This adds ONE dual-model hypothesis plus its control, not independent untouched validation. Five surviving stocks, correlated overlapping targets, price-risk versus wealth-risk distinction and no prospective evidence remain limitations. No automatic promotion or assertion that this completes ML.

## Scope / interfaces / review focus

Task1 owns `ml/src/stocklab_ml/swing_research.py` (extract private fitting core only), new `ml/src/stocklab_ml/downside_research.py`, and `ml/tests/test_downside_research.py`. Interfaces: `make_frame(features, prices, spy) -> DataFrame`; `predict(frame,start,end) -> (predictions,audits)` where predictions have date/symbol/probability_downside and audits include causal training_positive_rate; `orders(up,down,features) -> DataFrame` with the four simulator columns followed by reason/probability_up/probability_downside. Save no implicit converted risk-as-up score.

Task2 owns `ml/examples/downside_research.py` and `ml/tests/test_downside_replay.py`, reusing snapshot/dividend loaders and the existing costed evaluator without weakening old replay source guards. Read the existing cached recent entry forecasts; validate exact keys/coverage and original commitments. Forecast statistics, execution reasons and participation are analytical outputs, not a second accounting implementation.

Root owns this protocol, final `ml/docs/long-horizon-ml.md` / README update, private acquisition-free evidence, independent review, final verification and existing draft PR290 update. No backend/frontend/config/export contract changes. Risks to users are restricted to offline research; public defaults must remain identical.

Review and test: label window endpoints/missing future/symbol boundaries/gap exclusion; temporal feature invariance; full risk-horizon purge perturbations; two-class validation; exact original prediction/audit parity; conflicting BUY/SELL and intermediate risk; precise forecast-date joins; no later fitting on rejection; causal Brier references; one executed sale per held position; preserved ex-date entitlement/fills/caps in default simulator.

- [x] Observe failing target/feature/purge/decision tests, implement minimal shared-core/head and pass focused checks.
- [x] Observe failing runner admission/ordering/source-binding tests, implement runner and pass focused checks.
- [x] Run once, freeze admissions before later evaluation, and record exact success/failure without post-test parameter changes.
- [x] Independent code/data/report review, final relevant checks, secret scan, commit/push and update existing PR. Keep overall goal active unless every actual requirement is verified.

## Observed outcome and verification

Both arms pass their selection admission; dual wins at24.0452% CAGR/22.5372% DD versus control22.0407%/29.5981%. Both admissions are saved before later risk fitting. Full dual:17.1013% CAGR/22.5372% DD/132buys17sells(15downside); full control18.8761%/29.5981%/115buys3sells. Later dual13.6672%/13.5635%/64buys8sells versus control19.4525%/17.0526%/59buys2sells. SPY14.1669%full/14.2527%later. Dualhighercost16.7122%full/13.3867%later. No goal pass or promotion.

The dedicated forecast fails later generalization: AUC.4827, Brier.2299 versus causal-reference.2137. Full AUC.5024; full Brier.2299 versus.2198reference. Entry veto leaves147/148 entirely-cash observed sessions in the longest full/later stretches. No post-test threshold/policy/candidate changes are made.

458 relevant tests pass, including113research. Fresh review independently verifies sources and archived protocol, exact cached-entry/fitted-risk training rows/counts/prevalence and full horizon purges, causal forecast statistics, admissions before later fits, yearly participation, actual model-sale reasons, and every daily value in six full/later/control/dual/high-cost ledgers. Exported ledgers match exactly at float precision. Public defaults remain unchanged. Final report and publication checks follow.

Separately, two bounded fallback-only catalog requests assess next data feasibility; they do not change this experiment's stock universe. CurrentUScommonstock snapshot16,462identities; inclusive-delisted28,131;11,669extra. ISIN/CUSIP fields are all addon placeholders, not usable identifiers; only3,856extraFIGIs match the expected format, without establishing identity continuity. No historical lifetimes/terminalproceeds are supplied. Private source/hash/identifier audits are retained under `models/downside-20261112/catalog-probe`; no paid resource or prices acquired.

Implementation `f2d3b97` is published to `origin/codex/ml-local-completion`; PR290 title/body/head are verified, open/draft and unmerged. Exact eight-public-file staging, configured-secret scan and diff checks pass; latest develop remains8e9e177. Final report review corrected identifier wording and the earlier entry-replay command to regenerate a source-compatible risk baseline without weakening guards. This bounded experiment is complete; the overall20%/robust-signal/deployment goal remains active and incomplete.
