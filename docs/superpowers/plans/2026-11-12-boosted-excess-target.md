# Original-five boosted excess-target diagnostic plan

**Goal:** Advance the user's >20% long CAGR, SPY outperformance, effective sales, lower drawdown and complete local ML objective. This one experiment changes the economic event, without retuning policy or activating a model.

**Architecture:** Reuse the unchanged annual fitting/feature/accounting core on `target_excess_20`. Original-five/28/date-weighted/raw/trend200 is a single fixed contrast with the retained absolute-target reference. All old source closures, outputs and admissions remain immutable.

**Choice:** The absolute upside model is weakly ranked; original-five isotonic lowers Brier but misses all prior baselines, increases full drawdown and underperforms SPY from2021. A relative-return event addresses the requested benchmark objective directly. This is NOT an untouched/new event: `boosted_excess20` with24 features already failed on2015–2017(4.8638% CAGR/7.1422% DD). Only the exact28-feature/original-five/full2018–2026 contrast is untested. Do not repeat the failed original-five downside head or add a threshold/calibration/grid fallback after results.

## Fixed method and budget

- Same five stocks/SPY, complete observed calendar and raw sources through2026-11-11(2026 partial), original28 bits, annual actual boundaries2018–2026, four-year windows, strict `target_date_20 < first_prediction`, normalized inverse-date-count weighting and unchanged HGBT200/.05/15leaves/L2=1/seed42/no early stop/OMP1.
- The existing event is `(stock_open[t+21]/stock_open[t+1]-1) - (SPY_open[t+21]/SPY_open[t+1]-1) > .002`. The genuine known date is observed t+21; retain105 unknown tails. All fit row/date keys remain identical to the original absolute-target population. Labels/priors/learners change, not feature cells or calendar.
- Exactly nine actual HGBT fits, zero calibration fits and six stock-account simulations: enabled/no-SELL × full5+5bps/full25+25bps/fresh2021 5+5bps. Copy SPY/reference files without replay. Instrument actual calls with preserved method signatures, refuse excess calls before execution and require exact budget completion.
- Same100,000 USD initial funding/no deposits, actual next opens, pooled cash,10% purchase/20% symbol purchase caps,8-decimal quantities,176 verified gross dividends and original basis/fees/slippage/receivable timing. No leverage, shorts, taxes, cash interest or invented final liquidation.
- Same .65 BUY/.35 SELL cutoffs and immediate trend200 policy. Low p means a low estimated probability of beating SPY by MORE THAN20bps: class0 also includes positive excess returns up to .002. A SELL therefore predicts neither necessarily negative excess nor a negative absolute return/crash. No forced orders or post-result changes to exposure, execution order or policy.

## Event semantics and evidence

Primary forecasts are named `probability_outperform_spy`, not probability of absolute price rising. Every forecast/quality/audit/report explicitly identifies the excess event. Any temporary adapter into legacy helper column `probability_up` must be lossless, confined to the helper call and explicitly recorded; published forecast/execution sidecars must identify their relative-event score. Do not disguise this candidate as the retained `hgb_swing20_market28` snapshot or invoke native activation for it.

Report full/later/nine annual AUC/Brier on known EXCESS outcomes against the NEW original-five excess prior computed before each fit. The old absolute-up prior/Brier is a DIFFERENT event and is not a forecast-quality gate. Financial reference returns remain valid matched accounts. Preserve own weighted/unweighted priors, populations, all11140 scores/105 unknown tails, six complete ledgers/orders/position/basis/dividend/execution-reason sidecars,48 financial annual rows and nine paired effects. Preserve every BUY intent in same-score enabled/no-SELL controls.

Report strict full/later CAGR>20%, matched SPY and retained-reference outperformance, full DD<24.76576733338%, later DD<14.6771115714% and matched SPY, actual model sales and higher-cost sensitivity separately. AUC>.5/Brier<own-event prior is a separate diagnostic; never replace a failed financial condition with a favorable metric. All inspected history is adaptive, not a globally untouched holdout. Five selected survivors/current vintages/overlapping labels remain limits. The event uses split-adjusted stock PRICE and all-adjusted SPY proxy, not exact cash-dividend-inclusive relative total return.

Freeze/hash this plan, new private driver/helpers/self-check/wrapper,53 old research paths,84 snapshot bindings, reference trees/117+245 files, input manifests, environment and actual source commit BEFORE fitting. Use fresh ignored `ml/models/boosted-excess-target-20261112/run-v1`, refuse overwrite, retain exclusive root invocation9/0/6 and protected RFv7 registry/two inspected configs before/after. Do not alter an old proof to accommodate a new result. No provider calls or paid services.

## Checks and completion record

- [ ] Review the private driver and pass small synthetic checks: causal boundaries/known date/label reconstruction, unchanged feature/fit keys, new-event priors, missing/duplicate/nonfinite scores, relative-score adapter truthfulness and exact budget/policy semantics.
- [ ] Commit the preregistration, then root executes exactly one fixed9/0/6 study; no automatic retry/restart of an unobserved process.
- [ ] Independently reconstruct labels/priors/populations/quality and every financial ledger/annual/effect without fitting or calling producer/reference simulators. Publish all favorable and unfavorable outcomes in the draft PR.

Always retain `research_qualified=False`, `promotion_authorized=False` until separate qualification. No native selection/config/database/schedule/broker/DCA Site change. Keep these preregistration bytes unchanged after execution; completion belongs in the results report. This diagnostic cannot by itself finish the full ML goal.
