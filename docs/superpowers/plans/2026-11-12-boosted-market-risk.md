# Boosted model with lagged option implied market risk

**User objective:** Resume ML improvement, retain the boosted learner, exceed20%CAGR and SPY on long/later periods with genuine sales and lower drawdown. Local paper only; no live-model/config/DB/task changes or paid resources.

## Evidence and single hypothesis

The fixed broader boosted trial is complete and rejected on2018-2020: control20.0130%CAGR/33.2535%DD/1sale exceeds the original32.6814%DD ceiling; dual8.1156%/22.3961%/12sales fails20%/SPY/paired-return and Brier gates. Upside AUC.4966/Brier.2520 trails causal reference.2486; downside AUC.5293/Brier.2424 trails reference.2241. High-risk scores average.808 on350 known rows while the actual event rate is.523. These are overlapping retrospective outcomes, not independent probability validation. More activity and a more complex learner have not supplied reliable signals.

Test ONE additional information hypothesis: add four fixed lagged VIX/VIX3M market features to BOTH existing boosted heads. Options-implied market information differs from trailing stock/SPY prices. Do not tune lags/features/parameters/thresholds using new portfolio returns. Keep the existing immutable111-identity/231667-row training panel and six feature-source hashes; do not rebuild/reacquire stocks. Both heads refit because their input columns change. No source calls are needed: two official CSVs and bound identity/quality evidence already exist under models/market-risk-20261112.

## Source availability before fitting

Exact canonical public files were linked from official Cboe history/dashboard HTML: VIX_History.csv and VIX3M_History.csv at cdn-api.cboe.com/api/global/us_indices/daily_prices/. Preserve raw bytes and acquisition/quality/scope hashes. VIX raw SHA325e7d361a452cfeb94152f6d39ee4db28a8c0838357936b329137b61a063fc2; VIX3M SHA8e7bc7943edf71afd29113f3c87c3a66b6e1a802cae990ecc26fe8884ac1b22c. Both cover all4241 frozen SPY sessions. Retain33 VIX extra non-SPY dates in source audit; exclude them BEFORE session-lag calculations. No stock calendar relaxation or price filling.

The [Cboe methodology](https://cdn.cboe.com/api/global/us_indices/governance/Volatility_Index_Methodology_Selected_SPX_Target_Expected_Volatility_Term_Indices.pdf) specifies VIX3M's93-day maturity, an October2013 launch and normal regular-session dissemination through16:15ET. The [VIX methodology](https://cdn.cboe.com/api/global/us_indices/governance/Volatility_Index_Methodology_Cboe_Volatility_Index.pdf) describes30-day option-implied volatility. The [index policy](https://cdn.cboe.com/resources/indices/governance/Cboe_Index_Policies_Practices.pdf) permits historical EOD corrections. Current files therefore do not certify original vintages or exact historical first-publication times.

Mask both series before2013-11-01: conservatively the first full month after the stated VIX3M launch month. Prelaunch values cannot seed features. Align CLOSE only to the validated full SPY session calendar, with unique ordered dates and positive finite quotes; require both sources on every calendar date. Raw OHLC order problems before2010 remain recorded but cannot enter this postlaunch close-only calculation. Require valid OHLC within2010-through-cutoff, exact source schemas and bound hashes; no repairs.

## Fixed four features and causality

For prediction session t let L(t) be the actual quote from SPY session t-2; all five-session changes also use SPY sessions, never extra index-only dates.

- vix_level = VIX_close[t-2]/100.
- vix_change5 = VIX_close[t-2]/VIX_close[t-7]-1.
- vix_term_ratio = VIX_close[t-2]/VIX3M_close[t-2]-1.
- vix_term_change5 = term_ratio[t-2]-term_ratio[t-7].

Retain observation_date and lookback_date as audit columns ONLY, not model inputs. No NaN/Inf feature rows. Drop only the original evaluation-feature prefix before all four market features are known; assert that no date>=2014-01-02 is removed, so every eligible annual training and2018+prediction key remains unchanged. Every added-panel row must retain full coverage. This lag is conservative research timing, not proof against retrospective corrections; prospective operation would require actual retrieval timestamps before decisions and retained daily vintages.

## Minimal code scope

- NEW stocklab_ml/market_risk_research.py with source/hash/path/schema/calendar validation, four fixed features and attach helper; focused synthetic causality/identity/tamper tests. Existing six bound modules remain byte-identical; no dependencies.
- SAME examples/downside_research.py gains --market-risk-dir valid only with --boosted and --training-panel, incompatible with monthly. Invalid flags/data fail before output creation. Load/attach to up/down/combined frames; up uses24+4 columns, down28+4 through the existing private core, recipe names boosted_market_swing20 and boosted_market_price_downside20. Defaults preserve scores/audits/metadata.
- Protocol broader-boosted-market-risk-20261112-v1; copy this plan, bind/archive new market module and existing sources/environment/OMP_NUM_THREADS=1 before fits. Save/hash market-risk-features.csv with observation dates before fits. Inputs/summary add market_risk commitment only in this mode. Preserve full stock/dividend/panel/source commitments and output overwrite refusal.
- Root owns plan, private experiment, report/README/PR290. Source-module agent and runner agent have disjoint files; independent reviewer checks source/temporal/prefix/fit/ledger evidence. No new live artifact/inference/promotion interface.

## Unchanged experiment and gates

Same HGBT200/.05/15leaves/l2=1/earlystopFalse/seed42, four-year ANNUAL head-specific known-date purges, date weights, targets/thresholds/trend/veto/SELL priority/caps, costs and176 dividends. Selection2018-2020 first; each arm needs>=20%CAGR,SPY outperformance,DD<original32.681374% and actual ML sales. Dual additionally needs return>=its control,DD<its control,dedicated sales,AUC>.5 and Brierbettercausalweightedreference. Record both heads' diagnostics and participation, including rejected results.

Persist/hash admission BEFORE later2021-2026 fits/returns. Both rejected:no later/full models or results. Control-only:later upside only. Dual:paired full/later accounts and selected25+25bps sensitivity. Overall pass still needs>=20%/SPY/sales fullANDlater, fullDD<original and laterDD<SPY. No post-test substitution, gate relaxation or deployment from historical success. Evaluation remains the same five survivors; added training remains retrospective current-survivor data. All previous adaptive experiments and source-vintage limits remain visible.

- [x] Implement/test/review source features and opt-in runner; preserve bound sources and old defaults.
- [x] Freeze all source/input/code/environment commitments and run the single gated study.
- [x] Independently audit features/fit sets/forecast/ledger/admission; publish results and update draftPR290.

Completed outcome: control20.8998%CAGR/31.9147%DD/zero sales; dual13.4933%/22.1165%/15 downside sales. Both rejected,selectednull,results empty,no later/full fits or returns. Risk AUC.52845/Brier.24160 versus unchanged reference.22408. Independent audit verifies3,269 market rows,six exact fit sets and8,025 numeric ledger comparisons with zero error. See the public long-horizon report; source commit629a07d's four CI jobs pass,including1,996 Python tests.
