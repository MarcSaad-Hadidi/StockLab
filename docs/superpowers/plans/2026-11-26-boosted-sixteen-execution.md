# Boosted portfolio breadth replay

Measure whether expanding the choice of stocks improves annualized return, especially with fresh capital from 2024. Retain the eight-stock DCA-priority backtest as the matched control. The 2017 median dollar-volume screen fixes the additions in this order: ETN, NOW, AA, CNC, NTAP, ANET, LHX and TTE. The screen applies to the provider-available population. The experiment cannot measure results for delisted names outside that population.

## Inputs and fit

- Finish on November 11, 2026, the last common verified session for all 16 stocks. Start with the same $100,000. Verify the identity, full session alignment, split-adjusted price basis and cash distributions for each addition. The CNC bar with zero recorded volume on February 26, 2020 can mark an existing position. A next-open order for that stock on that date expires unfilled.
- Use gross USD cash distributions. Reconcile omissions or corrections to published cash and calendar evidence. For pre-conversion TTE where the issuer publishes only EUR, use the market data vendor's gross USD cash estimate on the verified US ex and payment dates. Use declared USD amounts when available and document the ADR conversion. Run a cash-excluded TTE sensitivity as a lower-bound accounting comparison. Report any unresolved coverage before drawing conclusions from that symbol.
- Apply the same nine annual 28-feature HGBT directional fits trained on the original five securities. Use the existing four-year fit window, equal weighting per training day and strict t+21 maturity before the first forecast of each year. Reproduce all archived eight-stock scores exactly. Reuse the verified annual rank estimators for the learned purchase arms.

## Account comparisons

1. **Eight-stock control** keeps the verified DCA zone order and trailing 252-session relative strength tie-break.
2. **Expanded DCA priority** applies the same ordering to the larger pool.
3. **Expanded learned priority** ranks otherwise equivalent BUYs within each DCA band using the annual ordinal HGBT score.
4. **Expanded learned half** also requires a BUY to rank in the top half of the eligible stocks at the decision close.

Preserve the existing next-session open, standard order size, 10 position slots, 20%/30% per-trade exposure caps and quarter sales spaced by at least 20 signal sessions per symbol. Compare January 2018 at 5 and 25 basis points each for fees and slippage, followed by fresh-capital starts in January 2019, 2020, 2021, 2022 and 2024 at standard costs. Evaluate the TTE sensitivity on the full standard-cost window and the fresh 2024 window. Use the same SPY total-return proxy, opening date and costs for every pair. Report CAGR, drawdown, trade counts, ending value and cash paid on each window. Retain all results, verify next-open and dividend accounting and leave model activation to prospective validation.
