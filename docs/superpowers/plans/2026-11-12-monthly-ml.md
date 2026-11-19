# Monthly ML Refit Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for the fitting/forecast and runner tasks, followed by a fresh independent review. This specification is frozen before new strategy returns.

**Goal:** Test whether monthly learning improves the active objective: >=20% annual CAGR, SPY outperformance, actual ML sales and lower maximum drawdown over long history, before ML commissioning.

**Architecture:** Refit both existing raw RF20 entry and dedicated price-downside20 heads each observed month. Preserve every other model, feature, target, confidence, cash and risk assumption. Extend the existing paired runner with one opt-in `--monthly` mode rather than another accounting implementation or parameter-search framework.

**Tech Stack:** Existing Python/pandas/scikit-learn/pytest; no dependencies or external data.

**Spec:** Active user objective; prior annual downside protocol/report in `2026-11-12-downside-ml.md` and `ml/docs/long-horizon-ml.md`.

## Evidence and fixed design

- Previous turn made progress: implemented and rejected an annual downside head, with verified source/ledger evidence. That result does not complete the objective. Latest source base5be06a8; RFv7 remains production.
- Saved annual downside audits show last training-feature dates393-397 days before the last prediction in2021-2025. In2024, a model whose latest feature date is2023-11-30 predicts through2024-12-31. The20-session target is much shorter than that staleness. Monthly is ONE fixed cadence hypothesis, not a daily/weekly/monthly grid or a guaranteed improvement.
- Fit frequency is exactly `annual` (unchanged default) or opt-in `monthly`. At each prediction month's FIRST OBSERVED SESSION, use at most four prior calendar years, both classes, strictly earlier feature dates and strictly earlier full target-known dates. Upside horizon ends at t+21 open; downside at t+20 close. Never share one head's purge with the other. First-of-month holidays and the partial November2026 month use actual observations.
- Same entry RF300/depth8/minleaf20/sqrt/seed42/njobs1; same dedicated downside RF and four trailing SPY context features. No calibration, class weighting, resampling, thresholds, feature selection, horizon or universe changes. Monthly fits use only known outcomes; latest unlabeled rows still infer.
- Control remains rawRF20/trend200; dual remains original entry approval AND downside<=.35 for BUY, downside>=.65 overrides with SELL, original model-approved SELL otherwise. No optional hard trailing/SPY/cooldown rules. Existing20% entry exposure,10% order,10positions,no shorts/leverage and actual next-open fills remain unchanged.
- Refit BOTH heads monthly from the validated snapshot; cached annual entry forecasts are provenance/context only, never monthly decision scores. Selection36 monthly fits/head; later71/head through November2026,107/head total if admitted. Control-only admission must not fit later downside models unnecessarily.
- Same five surviving stocks/2010+feature warm-up, actual176cash events in2018+, fixed $100,000/no deposits,5+5bps costs,25+25bps selected sensitivity, no taxes/interest/final liquidation. Price-risk labels intentionally exclude unavailable old cash dividends; account returns include actual available cash events.
- Preserve default annual predictions AND audit/schema identities exactly. Extend only offline fitting/forecast/runner code; no live model/task/config/database/runtime/API/UI changes. Existing source-hash guards remain intact.

## Selection and evaluation before new returns

1. Copy/hash exact protocol, executable sources, source snapshot/entry evidence and environment before fitting/simulation; refuse output overwrites. Use monthly protocol identifier and mark annual cached scores as contextual, not used for monthly decisions.
2. First fit both heads ONLY2018-01-01–2020-12-31. Save all monthly scores, fit boundaries/positive rates and both decision sets before calculating selection returns. Forecast Brier reference maps each score to ITS MONTH's causal fitted training prevalence. Missing/duplicate monthly references fail closed; do not carry annual or prior-month rates. Exclude labels whose known date ends after2020-12-31.
3. Admission now requires BOTH candidate arms to meet selection>=20%CAGR, above SPY, DD<original32.681374% ceiling and actual executed ML sales. Dual additionally requires CAGR>=monthly control, strictly lower DD than monthly control, actual dedicated-head sales, AUC>.5 and Brier better than its causal MONTHLY prevalence reference. The old9.77% protected-incumbent return comparison is not sufficient and must not substitute for20%.
4. Save/hash all admissions and chosen arm BEFORE any later model fit/returns. Prefer dual only if it meets its incremental gates; otherwise independently eligible control. If neither qualifies, record rejection and do not compute later/full strategy returns. No post-test candidate/threshold/cadence reselection.
5. If dual admitted, fit both heads2021-2026, evaluate paired monthly control/dual plus selected25+25bps sensitivity on later and full independent accounts. If only control admitted, fit only later entry head, retain its selection scores, evaluate control only; do not fit later risk head or use old annual scores. Save all first/last training/target/prediction boundaries.
6. Report CAGR,SPY,DD,actual sales by head, yearly returns/eligibility/exposure,longest all-cash stretch and monthly forecast quality. Annual results are frozen context, not another selectable optimized arm. Head forecast success or selection20% alone is not strategy validation.
7. Overall pass remains>=20%CAGR,SPY outperformance,genuine model sales on BOTH2018-2026full and2021-2026later; full DD<original32.6814%,later DD<same-period SPY. Existing adaptive-history/five-survivor/price-risk/prospective limitations remain explicit. A passing research comparison still requires compatible artifacts,audited promotion and tested prospective paper/restart operation before claiming ML finished.

## Scope / interfaces

Task1 owns `ml/src/stocklab_ml/swing_research.py`, `ml/src/stocklab_ml/downside_research.py`, `ml/tests/test_monthly_refits.py`, and `forecast_report` plus its focused tests in `ml/examples/downside_research.py` / `ml/tests/test_downside_replay.py`. Add keyword-only `refit_frequency="annual"` supporting exactly annual/monthly to shared/public fitting methods and downside.predict. Forecast report receives the same explicit frequency; defaults must retain prior behavior. It must map audits by actual month, never merely year. Coordinate edits to the shared runner file with Task2.

Task2 owns the REST of `ml/examples/downside_research.py` / `ml/tests/test_monthly_replay.py`: opt-in--monthly, both-head fit/persistence, stronger monthly admission, protocol/source archives, rejection/control-only/dual paths. Reuse unchanged account evaluator and source validation. No second model/evaluator framework, artifact contracts or extra config files.

Root owns protocol, private single-run evidence, final report/README, review/testing/publication. Review focus: separate known-date purges per month/head; holidays/partial months; future-label perturbations; no stale reference rates; both heads actually refitted; no later fitting on rejection; exact default annual parity; actual execution/dividend/reason accounting. No data deletion, new provider calls or user approval needed for this authorized offline work.

- [x] Observe failing monthly boundary/purge/default/reference tests; implement minimal optional cadence and pass relevant checks.
- [x] Observe failing runner admission/order/provenance tests; implement monthly mode and pass focused checks.
- [x] Execute once, freeze admissions before later fitting, and assess exact target without post-test adjustments.
- [x] Independently review source/data/ledger/report, run final checks and secret scan, commit/push and update draft PR290. Keep whole goal active unless every real requirement is verified.

## Observed outcome

The72selection fits complete. Control19.697381%CAGR/DD33.089897%,49BUY1SELL; dual18.742787%/26.815871%,43BUY8SELL(all8downside). SPY13.830164%CAGR. Control fails20% andDDceiling; dual fails20% andpairedreturn improvement. Both admissions false, selectednull, results{}; no later/full output or later fitting. No post-result changes to cadence or thresholds. Selection hash27542bc265341b4ef12bbf681f2c5be679baf8254e7556280e6b7d4deb4d3fa6.

Risk forecast AUC.5446784201/Brier.2296220461/reference.2323991489 on3680knownselectionlabels. Meaninvestment91.9875%/81.0767%, longestallcash1/57observedsessions. Monthly training-feature maximum age64days(up)/63(risk), versus398/397annual, without a performance qualification.

484relevant tests pass,139research. Independent review verifies source/protocol/input archives, exact36up+36risktraining sets/counts/feature/known boundaries/prevalence, causal reference statistics, decision joins, both daily fee/dividend/cap/cash/equity ledgers and absence of later artifacts. All exported ledger values match at float precision. No Critical/Important findings. Production RFv7/native tasks/config/DB remain unchanged; target and full ML commissioning remain incomplete.

Implementation1323387 is published to origin/codex/ml-local-completion. PR290 body/head verified, still open/draft/unmerged; latest develop8e9e177 unchanged. Exact nine-public-file staging/configured-secret scan/diff checks passed, and final report/reproduction/commissioning wording reviewed against source. This bounded cadence test is complete; the overall ML goal stays active and incomplete.
