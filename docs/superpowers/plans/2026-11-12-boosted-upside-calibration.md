# Original-five upside calibration implementation plan

> **For agentic workers:** Use the existing native implementation/review workflow. Research execution is root-owned and occurs only after the protocol, sources and synthetic checks are frozen.

**Goal:** Improve the boosted model toward >20% long-period CAGR, SPY outperformance, useful sales and lower maximum drawdown; complete local ML commissioning remains part of the user's objective.

**Architecture:** One fixed upside-only isotonic calibration diagnostic using the existing `swing._walk_forward_predict` calibrated_boosted branch. No old public fitting, feature, policy or accounting source is edited. Production RFv7 stays selected.

**Tech stack:** Existing Python environment, pandas, scikit-learn and frozen local evidence; no dependencies, provider calls or paid services.

**Spec:** The user's delegated continuing ML objective and the audited annual28 original-five reference in `ml/docs/boosted-improvement.md` / `ml/docs/boosted-exit-controls.md`.

## Choice and fixed scope

The retained annual28 model earns18.6259% CAGR with24.7658% maximum drawdown, but its full Brier .28525 trails its causal original-five prior .24584. The previous isotonic experiment calibrated a DIFFERENT downside head on broader training; it did not calibrate this original-five upside learner. No financial or calibration improvement is assumed from that distinction.

Use the already implemented weighted isotonic branch rather than adding sigmoid code, changing thresholds, or repeating failed cadence/horizon/exit variants. Calibration changes BOTH the base fitting subset and the score mapping; this is not an isolated mapping effect. Isotonic may overfit correlated daily labels, create ties/extremes, eliminate activity or lose return. All outcomes stand; no method/threshold retry in this batch.

Keep original five identities,28 ordered features, open[t+1] to open[t+21] price-up target >.002, annual boundaries2018–2026, fixed HGBT200/.05/15leaves/L2=1/seed42/early_stopping=False, OMP1 and date weighting. Reference sources/forecasts remain byte-identical, including `f53007c0ad1de79ba3fe306c31a30c3299c9dd2454f6b9702c7fbf3040e68630`.

At each actual first session T, form the original four-year population with feature date<T and target-known date<T. Record its weighted/unweighted priors BEFORE splitting. Reserve the latest one calendar year by feature date for calibration; C is its actual first observed session. Base rows require feature date<C AND target-known date<C. Calibration labels remain known<T. Require both classes in each partition, recompute normalized inverse-date-count weights separately, fit the base exactly once and calibrate the frozen estimator exactly once using the existing branch. Never refit on combined history.

## Execution budget and accounting

Exactly nine new base HGBT fits plus nine frozen-estimator isotonic calibrations; zero reference fits. Execute SIX fixed accounts: calibrated immediate trend200 SELL enabled/disabled, each for full2018–2026 at5+5bps, full at25+25bps, and fresh100,000 USD from2021 at5+5bps. No calibration/model change between accounts. The disabled arm is a policy control and cannot substitute for genuine-sales qualification.

All accounts use the same cached sessions through2026-11-11 (2026 partial),100,000 USD initial cash/no deposits,176 verified gross dividend events and original pooled-cash/symbol-order/10% purchase/20% per-symbol limits. Preserve actual next opens,8-decimal quantities, cost basis, fees/slippage, dividend entitlement/receivables/payment timing, no leverage/shorts/taxes/interest/final liquidation. Keep raw .65/.35 thresholds and original trend confirmations. Do not overwrite old admissions, studies, sources or proofs.

Archive/hash plan, private driver/helpers/self-check, source closure, input manifests, original reference trees and environment BEFORE fitting. Use fresh ignored `ml/models/boosted-upside-calibration-20261112/run-v1`, refuse overwrite and retain one exclusive root invocation with9-fit/6-account budget and protected operational-registry/config before/after hashes. Do not restart an unobserved process without checking its actual handle.

## Diagnostics and independent checks

Export every forecast including unknown tails, all nine full/base/calibration populations and date-weight metadata, full/later/annual AUC/Brier and counts. Compare Brier separately with full-four-year original-five prior, base-fit prior, calibration prior and unchanged broader annual prior. The new calibration sample's prevalence never silently replaces the full eligible population reference. Store all six daily/trade/order/position/dividend/score-reason ledgers, annual returns, genuine sale counts and paired no-SELL effects. Report entry and exit changes jointly when comparing models; same-score controls isolate exit permission at account-policy level only.

Report strict full/later CAGR>20%, matched SPY/reference outperformance, full drawdown<24.76576733338%, later drawdown<matched SPY and genuine model sales separately. Keep `research_qualified=False`, `promotion_authorized=False`; adaptive historical success is not promotion or prospective qualification. All prior examined years, five selected survivors, current adjusted-price/index vintages and repeated research remain disclosed.

Independent review must reconstruct split/known-date/weight/priors and all six ledgers/metrics without model fits, producer simulations or reference-account replays. Synthetic checks must cover inclusive four-year start, both strict purges, unknown tails, missing/duplicate/nonfinite scores, class failures, normalized date weights, exactly one base fit per period and unchanged policy semantics.

## Tasks

- [ ] Prepare reviewed private driver/self-check; verify all old source/reference bindings and commit this preregistration before fits.
- [ ] Execute the single9-fit/6-account study with immutable invocation/source/environment evidence.
- [ ] Independently audit every account and publish favorable/unfavorable results in the existing draft PR.

The preregistration bytes stay unchanged after execution. Completion belongs in the public results report, not in this bound protocol.
