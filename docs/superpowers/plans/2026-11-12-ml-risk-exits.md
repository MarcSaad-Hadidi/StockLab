# ML Risk Exits Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for the shared simulator and delegate the independent replay runner; obtain a fresh final review.

**Goal:** Test whether explicit protective sales can improve the frozen ML candidate's CAGR and drawdown, without weakening confidence or exposure limits.

**Architecture:** Extend the existing offline shared-cash simulator with two fixed restrictive policies. Reuse the exact frozen calibrated-20-session forecasts, verified price snapshot and real dividend events; preserve the default simulator and production RFv7. This document is also the pre-results experiment specification.

**Tech Stack:** Existing Python, pandas, Fraction accounting and pytest; no new dependencies.

**Spec:** Latest user request to improve sales, exceed 20% annual CAGR, beat SPY and reduce maximum drawdown; prior long-horizon study in `ml/docs/long-horizon-ml.md`.

## Global constraints and frozen protocol

- No production model/config/task/DB changes or paid API calls. Research only until validated promotion is justified.
- Existing BUY/SELL model confidence remains .65; existing trend200 confirmation, next-open fills, 20% entry exposure / 10% order / 10 positions remain unchanged. Protective exits carry a reason, not fabricated ML confidence.
- Baseline is frozen `rf_calibrated20-trend200`, cash dividends included, from `long-horizon-20261111/run-v4` and `dividend-dataset`.
- Exactly two new recipes: `trailing15`, `trailing15_spy200`. No parameter grid or choice based on later test returns.
- A newly filled position starts a signal index and peak at its observed entry open. Entry-day index becomes the close; subsequent held-day returns compound `(close + ex_date_dividend)/previous_close`. Further buys retain the peak. This index only triggers risk and is never a fill, cash or valuation price.
- A confirmed close at or below 85% of peak schedules full liquidation at the next observed open. Gaps receive the actual opening price and usual friction. No intraday stop-price assumptions.
- `trailing15_spy200` additionally uses SPY provider total-return proxy closes versus SMA200. Three consecutive strictly below confirms bearish; three strictly above confirms bullish. Equality resets streaks and keeps prior state. Warm up on all observed pre-start data, require a valid 200-session context at start, and apply confirmed state at the next open. Initial state before any confirmation is bullish.
- Bearish state blocks BUYs and schedules full liquidation. Reason priority: `spy_regime`, then `trailing_stop`, then ordinary `decision`. Risk sells run before ordinary orders and suppress same-symbol BUYs at that open.
- Every executed risk sale starts a 20-observed-session cooldown: sale at k blocks opens k+1 through k+20; first eligible open is k+21. Ordinary model sales have no cooldown. Final-day signals remain unexecuted.
- Ex-date entitlement occurs before open sales; payment becomes spendable strictly after pay date. Existing dividend and cash reconciliation remains mandatory.
- Selection uses 2018-01-01 through 2020-12-31, with flat initial capital and no deposits. Choose the greatest CAGR of the two protective policies satisfying strictly lower DD than the same-period baseline and at least one protective sale. If neither qualifies, keep baseline. Write and hash selection before any new-policy 2021-01-01 through 2026-11-11 returns. Test baseline plus only the selected policy; also report full 2018-2026 context and 25+25bps sensitivity of the selection.
- Target passes only with CAGR >=20%, CAGR above SPY, maximum DD strictly below the original same-period candidate, and genuine protective sales on BOTH full and later-test periods. Selection is not success; a candidate may reduce DD while sacrificing CAGR.
- Entire history was already inspected during earlier adaptive research. This split prevents new exit selection on its later returns, but is NOT a pristine holdout or a guarantee. Same five surviving stocks, no point-in-time universe, taxes or final liquidation.

## Scope and review focus

Modify `ml/src/stocklab_ml/research.py` only for optional risk exits; its existing three example callers retain default behavior. Add `ml/tests/test_research_risk_exits.py`. Extract the existing validated replay-input loader in `ml/examples/dividend_replay.py` for reuse by `ml/examples/risk_exit_replay.py`; update replay tests if needed. Publish evidence in `ml/docs/long-horizon-ml.md` and links in `ml/README.md`. Private exports remain ignored under `ml/models/risk-exits-20261112`.

Review next-open causality, cooldown off-by-one, dividend-aware stops/entitlement, simultaneous model orders, exact default parity and invalid SPY context. None of these changes destroys data or alters live callers.

## Task 1: Shared simulator

- [x] Write failing tests for gap-down fills, cooldown boundary/re-entry peak reset, dividend detachment, risk-sale entitlement, three-close regime, future invariance, default parity and invalid policy/context.
- [x] Observe failures, then add `simulate(..., risk_policy: str | None = None, spy: pd.DataFrame | None = None)` with exactly the frozen policies; optional trade `reason` column only in risk mode.
- [x] Run the new tests plus existing research tests; final adjacent verification remains below.

## Task 2: Frozen replay and report

- [x] Reuse validated price/dividend/prediction loading, bind code/input hashes and write experiment inputs before returns. Runner refuses existing output directories.
- [x] Select on 2018-2020, persist selection then evaluate only selected versus baseline on later/full periods and higher costs. Test deterministic selection and rejection of changed frozen inputs.
- [x] Independently reconcile output and review patch; resolve findings and run final focused checks.
- [x] Record exact results, limitations and whether the target passed, then commit/push to existing PR #290 without promoting an unqualified model.

## Observed result and one pre-results entry-model ablation

The original protocol is preserved byte-for-byte at `ml/models/risk-exits-20261112/run-v1/protocol-plan.md` with source snapshots. Selection froze `trailing15_spy200`: 8.1474% CAGR/16.8810% DD versus original 21.2314%/32.6814% in 2018-2020. Full replay yields 6.4274%/16.8810%, 25 genuine protective sales, versus original 19.4043%/32.6814%. Later reset yields 2.7479%/3.0048% versus original 3.5202%/5.0041%. No objective pass or promotion.

Exact annual model reconstruction reproduces frozen probabilities. No selected forecast is <=.35 across 11,140 rows. More seriously, there are no eligible BUYs in 2021-2025: 2021-2024 never reach .65; 2025's 13 score hits fail the unchanged trend filter. Calibration reverses raw ranking in 2019/2021/2023/2024. Holding old 2018-2020 positions explains the apparent full-period strength; fresh deployment does not share it.

Before computing any new strategy returns, authorize exactly ONE ablation: existing `rf_swing20` regularized RF with `trend200` and already-frozen `trailing15_spy200`. No new model family, feature, threshold, leverage, stop value or dependency. This removes calibration AND includes the latest eligible training year in the base fit; disclose both changes. Raw RF directional scores are not established calibrated probabilities.

- Add `ml/examples/entry_signal_replay.py` and focused ordering/gate tests. Reuse validated replay loading and extract the existing risk runner's evaluator for reuse, rather than duplicate accounting. Bind previous risk summary/selection hashes, original source and code hashes before returns; refuse overwrites.
- Fit/predict only 2018-2020 first using existing annual purged `walk_forward_predict(..., 'rf_swing20', ...)`, fixed original hyperparameters/features. Evaluate with verified cash dividends and fixed protections.
- Freeze eligibility before later fits/returns: selection CAGR must exceed 8.147425% (the exact protected-incumbent selection report), DD must be strictly below the original 32.681374%, and protective sells must be positive. Use exact saved report values, not these rounded text values.
- If ineligible, save rejection, do not compute later/full candidate returns. If eligible, persist/hash selection, then fit only annual 2021-2026 candidate predictions; combine identical saved earlier predictions for full context. Evaluate later/full against BOTH original unprotected and protected incumbents, plus 25+25bps candidate sensitivity. No post-test reselection.
- Report signal eligibility by year and forecast diagnostics separately from returns. `all_objectives_met` still requires >=20% CAGR, SPY outperformance, lower DD than same-period original, and real protective sales on BOTH later and full periods. All data remains adaptively inspected history, no pristine holdout claim.
- [x] Observe failing gate/ordering tests, implement shared evaluator and single-ablation runner, then pass focused checks.
- [x] Run once; record admission/rejection and exact metrics. Independent final review and verification before commit/PR update.

## Final verification

The single ablation passes selection at 9.7730% CAGR/15.1500% DD. Its later result is 9.6625% CAGR/9.3782% DD (114 buys/25 sells); full result is 10.2292% CAGR/15.1500% DD (181 buys/40 sells, 39 protective). Full higher-cost result is 9.4155%/15.7101%. Both periods underperform SPY and fail 20%; the later drawdown also exceeds the mostly-cash original reset. No model is promoted and the overall goal remains incomplete.

427 research/adjacent tests pass, including 82 research cases. Two initial atomic NTFS export tests failed inside the sandbox; both and the final entire selected suite pass outside it with actual hard-link publication. Independent review verifies exact default parity against prepatch code, byte-identical full baseline CSVs, all source/code/selection/protocol hashes, nine purged annual boundaries, yearly entry diagnostics and every selected daily accounting balance within $1.2e-10. Source/protocol copies preserve both experiment versions before status updates. No dependencies, production configuration, native tasks, live database or broker orders changed. Latest develop re-fetched unchanged at8e9e177.

Implementation `f307d33` is published to `origin/codex/ml-local-completion`. PR #290's title/body match the final scope and measured failures; it remains open/draft and unmerged. Exact ten-file staging and configured-secret scan passed. This bounded experiment is complete; the active overall ML goal is not.
