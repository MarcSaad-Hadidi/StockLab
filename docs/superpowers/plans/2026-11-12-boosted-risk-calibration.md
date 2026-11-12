# Risk-only isotonic calibration of the boosted market model

**Objective:** exceed20% CAGR and SPY over long/later history, execute genuine model sales and lower maximum drawdown. Local paper research only. Production remains RFv7; no task/config/database/broker changes.

## Evidence and one fixed hypothesis

The completed market-information trial is rejected on2018-2020. Raw boosted control earns20.8998% CAGR with31.9147% maximum drawdown but makes no sales. Its dual earns13.4933% with22.1165% drawdown and15 downside sales, below20%,SPY13.8302% and its control. Risk AUC.52845/Brier.24160 trails the causal-reference Brier.22408. High-risk known rows average.8103 probability against.5493 actual event frequency. Independent review verifies all six fit sets and8,025 ledger comparisons with zero numerical error. No later/full models or returns were calculated.

Test ONE risk-only probability calibration hypothesis. Preserve raw upside unchanged and calibrate the dedicated boosted downside head using a separate chronological holdout and weighted isotonic regression. No policy-only change can repair failing Brier with unchanged forecasts. No threshold, feature, window, estimator or calibration-method grid; no fallback after observing results.

## Causal fitting and fixed reference

For each annual prediction boundary T, form the same eligible four-year downside population: feature date>=T-4years and<T, binary label known strictly beforeT. Capture its normalized inverse-date-count weighted prevalence BEFORE splitting as forecast_reference_positive_rate. This is the benchmark population, not fitted-subset prevalence. Preserve the existing selection references exactly: .33470445076754696,.39717792220058273,.39559852562478065. Evaluation remains unweighted Brier/AUC on the same matched known outcomes with existing end-date censoring; calibration prevalence cannot replace the reference.

Reserve the latest one year by feature date for calibration. Let C be its actual first observed session. Base rows must have feature date<C AND label-known date<C. Calibration labels must remain known strictly beforeT. Require both classes in both partitions before fitting. Recompute normalized inverse-date-count weights separately after each partition's purges. Fit the unchanged HistGradientBoostingClassifier once on base rows:200 iterations,.05 learning rate,15 maximum leaves,L2=1,early_stopping=False,seed42,OMP_NUM_THREADS=1. Then fit installed CalibratedClassifierCV(FrozenEstimator(base),method="isotonic") on calibration rows with their own weights. Never refit base on combined history.

Audit base and calibration rows/dates/identities, first/last feature and known dates, weighted/unweighted positive rates, method, and full four-year reference population. training_positive_rate continues to describe the actual base fit. New reference metadata appears only for the new private calibrated_boosted kind. Existing RF, raw boosted, sigmoid-calibrated RF, public recipe sets and default audit/score contracts remain unchanged.

Read-only preflight found2018/2019/2020 base counts34155/48321/50716 over736/735/734 dates; calibration16341/16401/16769 over231/231/232 dates. Both classes exist in each partition. These are correlated overlapping20-session labels, not thousands of independent calibration observations. This trial changes both the base-fit subset and output mapping; it does not isolate calibration alone. Isotonic is nondecreasing but can introduce ties, endpoint extremes, overfitting or no qualifying sales. Those outcomes must stand. No prospective probability or return guarantee follows.

## Minimal code, lineage and tests

- Extend the existing private swing fitting core with calibrated_boosted; reuse installed scikit-learn calibration and existing split pattern. No dependency or standalone framework.
- Existing downside runner gains --calibrate-risk, valid only with annual --boosted --market-risk-dir --training-panel. Upside remains boosted_market_swing20 with28 columns and raw full-four-year fitting. Downside is isotonic_boosted_market_price_downside20 with32 columns. Existing features, labels, score meanings and policy remain unchanged.
- New protocol broader-boosted-isotonic-market-risk-20261112-v1. Bind/archive this plan, code, sources, panel, market features, environment and process-local OMP1 before fits. Newmode inputs/summary explicitly state risk_calibration="isotonic".
- Whole-file swing source commitment changes. Preserve the old panel/compiler. Recompile a NEW panel-isotonic from the same frozen256 raw sources using a private compiler copy, bind that actual compiler plus the original required compiler, and enforce current six source hashes. Require training.csv byte equality with the original SHA80ad61f4c4bd56bfa328ac20cd7fa2dbb8d8f991d2a807187752748f2447e3ec. No data acquisition, source changes, exclusion changes or manifest rebinding in place.
- Require new upside selection CSV bytes to match the parent SHA f48dacd2c31e121a30a1e2a19776dc50df213226e3f80205029c7634396686d1. Its control must remain20.8998%/31.9147%/zero sales; do not substitute a newly calibrated upside.
- Synthetic tests cover strict split/label boundaries, future-label invariance, separate weights, class failures, actual base fit count, finite monotone outputs, full-population reference, default behavior, invalid CLI combinations before output creation, risk-only routing, reference fallback and unchanged admissions. Independent review checks the compiled panel, all fit audits/forecasts, score-to-order logic, exact cash/dividend/position ledgers and no-later rule without refitting models.

## Frozen experiment and admission

Keep annual four-year windows, all24/28 original plus four market features,20-session labels,BUY>=.65,SELL<=.35,risk SELL>=.65,veto<=.35,trend conditions,SELL priority,position/cash constraints,176 real dividends and5+5bps costs per side. No forced activity, leverage, shorting or final hypothetical liquidation.

Selection2018-2020 runs first. Each arm needs>=20%CAGR,SPY outperformance,DD<original32.681374% and actual ML sales. Dual additionally needs CAGR>=its raw control,DD<its control,dedicated sales,AUC>.5 and Brier strictly below the unchanged causal reference. Record both heads' diagnostics and participation even on rejection. Persist/hash admissions BEFORE any later fits/returns. Both rejected means no later/full artifacts. Control-only permits later upside only; admitted dual permits paired full/later and selected25+25bps sensitivity. Overall qualification still needs>=20%/SPY/sales on fullANDlater accounts, fullDD<original and laterDD<SPY. Do not relax a gate.

Evaluation remains five selected survivors, broader training is retrospectively available/current-survivor data, and Cboe history may contain revisions. Earlier adaptive experiments reuse selection years; the later period is not a pristine globally untouched holdout. Historical success alone would not commission the native inference/artifact/promotion/execution interfaces.

- [ ] Implement and verify shared core and opt-in runner; independently review causal splits and source lineage.
- [ ] Freeze inputs/code/environment, compile byte-identical new panel and execute the single gated study.
- [ ] Independently audit and publish the observed result, including failure; update draftPR290.
