# Fixed annual28 entry-quarter exit diagnostic

The full objective remains >20% long-period compound annual growth, effective sales, lower drawdown and functional local ML. The preceding turn made verified progress: four audited diagnostics and a genuine registered HGBT28 snapshot, with exact2026 prediction parity and green CI. None meets the financial objective. Keep the18.625926% CAGR /24.765767% drawdown annual reference and all failed/frontier outcomes; no promotion or native change.

## One fixed exit policy, no new model fits

Use the complete frozen annual original-five28-feature20-session forecast stream, SHA`f53007c0ad1de79ba3fe306c31a30c3299c9dd2454f6b9702c7fbf3040e68630`, passed audit`80677e99b456ca8765c74770382d0181b7344c565450f4e39a8ea96c3a981eef`. Preserve every BUY date/symbol/raw score/allocation and the .65/.35 thresholds/trend rules. No new model, calibration, fit, score, asset or parameter selection.

Complete SELL eligibility is p<=.35 AND close_ma200<0 AND momentum20<0. For each symbol, the first observed cached row seeds eligibility state without inventing a previous false. Thereafter schedule a SELL only on an observed false-to-true transition. Process all11140 rows continuously from2018 through2026-11-11 before fresh2021 slicing; do not reset at a year/refit boundary. Include all105 target-unknown forecast tails. Missing/duplicate/nonfinite score/context coverage fails; no forward filling. A valid false rearms the next entry.

Consume each entry at scheduling even when no position exists or the next-opening quarter rounds to zero. At the next actual observed opening, sell `floor8(actual then-held quantity /4)` once, retain the remainder and its proportional fee-inclusive cost basis. The remainder is at least75%, slightly more after rounding. No repeated daily quarters during one eligibility episode, forced full exit, dust liquidation, short position or final hypothetical liquidation. A zero/unheld quantity incurs no fee. Final-session events remain explicitly unexecuted when no next observed open exists.

This is an episode-entry plus sale-size strategy. Do not attribute a difference solely to25% size, call it the original monthly DCA rule, or claim individual-sale alpha. The user's earlier25% STRONG_TRIM preference motivates the size; its predicate here remains the model's own exact SELL condition.

## Preserve account economics

The existing funded DCA account cannot be reused wholesale: its per-symbol buckets, monthly purchase sizing and SELL-first ordering differ. Use a small private bounded adaptation of frozen `research.simulate`, preserving pooled cash, actual opens, normal symbol execution order, BUY sizing/caps, calendars and ex-date/payment handling. Replace only full SELL quantity with the quarter/remainder update. Reuse the existing DCA Fraction floor8/proportional-basis equations where appropriate; basis bookkeeping cannot drive sizing or cash.

Keep EXACT nine parent trade columns and six equity columns. Store partial quantities, released/remaining basis, net realized P&L, remaining holdings and order statuses in separate accounting/orders/positions evidence. Reconcile initial capital + net realized P&L + unrealized P&L + gross earned dividends to equity, without double-counting fees already included in basis/net proceeds. Existing account/exposure/calendar-return helpers may be reused; no monkeypatching `research.simulate` or old producer code.

All accounts use100000 USD initial cash/no later contributions, original AAPL/JNJ/JPM/MSFT/XOM, split-adjusted actual openings,176 genuine gross dividends with ex-date entitlement before opening orders and spendability strictly after pay date,8dp quantities,10% purchase/20% symbol caps at execution. Appreciation may exceed the purchase cap. No shorts/leverage/taxes/interest. Same frozen dates through2026-11-11,2026 partial.

Run exactly three new accounts: `full-quarter`2018 start5+5bps fee/slippage; `full-quarter-higher-costs` same scores25+25bps; `later-quarter` fresh2021 start5+5bps. Copy matching SPY files and all six reference accounts from the passed annual immediate and horizon no-SELL studies (audit`d9972a7e3fa4f2870f9dff17736c8e17ff638faf8c940310690946d77a8da3d9`); never refit/replay a control or benchmark. Full/later score and quality files remain byte-identical.

Publish all three results/24 annual-return rows, activity, fees, realized/unrealized P&L, dividends, continuous exposure and six comparisons versus matched immediate/no-SELL. Differences are candidate minus reference CAGR/wealth; drawdown reduction is reference DD minus candidate DD. Report strict full CAGR>.20, >SPY/>18.625926%, DD<24.765767% and genuine held-position sales; fresh>20%/paired returns/DD versus SPY/sales separately, including its comparison to the old fresh account. Do not reuse a >=20 legacy flag as strict qualification. No shorter/no-SELL/riskier result substitutes for the joint objective.

## Freeze and verification

Private folder`ml/models/boosted-quarter-exit-20261112`: minimal driver, bounded replay helper, synthetic checks and reference inventory. Root owns exclusive `execute-quarter.py`; output/invocation refuses overwrite. Commit/archive this plan, final helper/driver/source hashes, actual raw input/calendar/dividend/environment identities, full frame/eligibility stream, exact adapted-source diff, both frozen reference trees/audits and later failed-study/export proofs BEFORE replay. All51 old source paths and the actual RFv7/config remain unchanged. Concurrent C#/TypeScript catalog compatibility does not alter Python research sources.

Meaningful synthetic checks cover one entry per true episode; false rearming; unheld/dust consumption; cross-symbol/year/refit/fresh continuity; fill-time quarter/floor8/proportional basis; ex-date entitlement/delayed cash; unchanged pooled-cash caps/symbol ordering; terminal unexecuted events; BUY identity/unknown tails and missing/nonfinite rejection. These use synthetic accounts only.

An independent auditor performs ZERO model fits, producer simulations or reference replays. Independently reconstruct all11140 eligibility/entry rows, exact partial quantities with integer/Fraction arithmetic, every cash/position/basis/dividend/fee/cap/open/equity/SPY/annual ledger and all six effect records. Verify source/invocation/prefit commitments and raw-score traceability. More retained exposure can worsen crashes; fewer sales or higher CAGR alone cannot prove better exits. All history is adaptive, with correlated survivors/current vintages/dependent labels. `research_qualified=False`, `promotion_authorized=False`; no future20% guarantee.

- [ ] Prepare/review private helpers and pass synthetic checks; freeze commitments.
- [ ] Execute exactly three candidate accounts with zero model fits and unchanged controls.
- [ ] Independently audit and publish every outcome and remaining limitation.
