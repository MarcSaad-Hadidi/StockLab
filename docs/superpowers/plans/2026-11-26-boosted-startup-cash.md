# Boosted startup cash invested through a temporary SPY position

The fresh January 2024 account earns 6.05% during calendar 2024, against 25.59% for its dated SPY proxy. On average, its equity is 28.53% invested in January, 49.51% in February, 53.50% in March, and 84.32% in April. The same strategy's established account earns 17.03% in calendar 2024. Reconciled attribution identifies both time spent in cash and a different initial stock mix. Test whether a temporary benchmark position improves the funding windows while the original model accumulates stock positions.

## Fixed policies

- Keep the dated eight-stock HGBT buy and quarter-sale signals, DCA priority, forecast weights, costs, next-open execution, 10 stock slots, and all stock-level purchase limits. Use one temporary SPY position on actual USD ETF opens and closes. The fund's issuer distributions earn cash on prior holdings at the US ex-date and become spendable on the first observed session strictly after their payable date. Use the archived total-return SPY curve for the matched performance comparison.
- **Quarter startup** invests leftover cash in SPY after processing model orders during the first 63 observed sessions. Release just enough SPY at the same open to fund an eligible stock order, respecting both transactions' fees, slippage and the post-cost stock cap. Liquidate remaining SPY before processing orders on session 64. Stock selection and the ranking of eligible buys stay fixed.
- **Trend-guarded quarter startup** uses the same temporary position only if SPY's preceding close is strictly above its trailing 200-session price moving average. Calculate the state using only dates through the preceding close. An uninvested opening keeps cash available until the causal condition allows the sleeve again, with session 64 the same deadline.

## Verification and comparison

Use $100,000 at each new funding date. Run both variants from January 2018 at 5 and at 25 basis points each for fees and slippage, and at standard costs from January 2019, 2020, 2021, 2022 and 2024. End every replay on November 25, 2026. Verify full and fresh-2024 control-curve parity when the temporary position is disabled. Report CAGR, maximum drawdown and the matched SPY drawdown, ending value, actual stock sales, ETF costs, and the recent change. Measure sales and drawdown alongside the return priority. A higher calendar-year return alone cannot meet the complete recent-start objective.

Check how fund charges affect stock limits. Reconstruct total wealth and dividend accrual from both trade streams on each session. Bind the source calendar, issuer cash amounts, archived stock input and code before reading portfolio outcomes. Financial results use the same adaptive research history and give no authority to switch the active paper model.
