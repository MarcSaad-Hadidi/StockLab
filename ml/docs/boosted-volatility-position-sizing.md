# Volatility-aware position sizing and partial sales

On the five-stock reference, volatility-triggered partial sales reduce maximum drawdown from **24.77% to 23.17%** and lower annualized return from **18.63% to 18.00%**. A stricter purchase cap alone adds 0.0056 percentage point of annualized return. The eight-stock combined application weakens both measures over the full period. None of the four variants clears the joint target. [Preregistered experiment](../../docs/superpowers/plans/2026-11-26-boosted-volatility-position-sizing.md)

The fixed HGBT forecasts and original trade signals are reused exactly. The risk rule compares 20-session realized volatility with its own prior one-year median. It also checks whether the stock trails SPY over 20 sessions. One arm applies the resulting smaller cap to new purchases in stressed names. Another sells up to one quarter of an overweight holding on entry into a low-score, negative-momentum stress episode. The existing full model sale takes precedence at the same opening. A third arm applies both rules and the portability check uses that same arm on the verified eight-stock account. Additional sales use the original bearish score threshold, with the capped amount solved after costs at the next opening.

## Matched portfolio results

Every replay starts with $100,000, uses actual next-session opens, and includes the existing cash dividends and transaction charges. The full window starts January 1, 2018. The shorter comparison starts a fresh account on January 1, 2021. Both end on November 11, 2026. Standard costs are 5 basis points each for fees and slippage per side. Each row labeled higher costs uses 25 basis points each.

| Account | Annualized return | Maximum drawdown | Ending equity USD | Buys / full sales / trims |
|---|---:|---:|---:|---:|
| Retained five, full | 18.6259% | 24.7658% | 454,173.23 | 223 / 29 / 0 |
| SPY, full | 14.1669% | 33.7196% | 323,443.40 | One initial buy |
| Five purchase cap, full | 18.6315% | 24.7658% | 454,362.65 | 225 / 29 / 0 |
| Five partial sales, full | 17.9999% | 23.1674% | 433,373.98 | 238 / 29 / 14 |
| Five combined, full | 17.9912% | 23.1676% | 433,088.47 | 228 / 29 / 14 |
| Five purchase cap, full higher costs | 18.0463% | 24.8852% | 434,884.23 | 219 / 29 / 0 |
| Five partial sales, full higher costs | 17.3649% | 23.3114% | 413,141.85 | 239 / 29 / 14 |
| Five combined, full higher costs | 17.3570% | 23.3116% | 412,894.51 | 232 / 29 / 14 |
| Five purchase cap, fresh 2021 | 17.5877% | 14.6498% | 258,379.55 | 132 / 19 / 0 |
| Five partial sales, fresh 2021 | 17.7528% | 14.6392% | 260,513.15 | 141 / 19 / 6 |
| Five combined, fresh 2021 | 17.7640% | 14.6143% | 260,657.58 | 137 / 19 / 6 |
| Eight combined, full | 16.8159% | 34.1974% | 396,332.56 | 317 / 40 / 18 |
| Eight combined, full higher costs | 16.1767% | 34.1972% | 377,524.36 | 300 / 40 / 18 |
| Eight combined, fresh 2021 | 16.6318% | 24.5802% | 246,313.57 | 209 / 29 / 8 |

The five-stock partial sale arm ends $20,799.25 below the retained strategy and cuts the maximum drawdown by 1.5983 percentage points. On fresh capital the combined arm improves both measures from the retained strategy's 17.5778% and 14.6771%. The older eight-stock relative-sale reference earns 17.1473% with 29.1208% drawdown over the full window. Its combined volatility arm increases drawdown by 5.0765 points. The risk rule's effects depend on the holdings it trims and subsequent opportunities to buy.

A paired diagnostic keeps the trimmed share counts fixed and compares actual net sale proceeds with holding those shares for 20 sessions, including earned dividends and the same exit costs. Of 13 five-stock trims with a known endpoint, nine have higher immediate proceeds. Those 13 trims produce $4,970.24 more in aggregate than the 20-session hold comparison. The actual account performance above includes the later uses of cash and the change to the remaining positions.

The producer executes twelve accounts and makes zero fit or provider calls. The independent audit matches the risk conditions, next-open order sequence, partial share quantities, position proceeds, dividend timing and same-date benchmark. It checks **154,980 ledger values** with zero maximum absolute difference and binds 122 files. The largest independently recomputed risk-feature difference is `1.69e-15`. All three five-stock variants pass 9 of 16 historical gates and the eight-stock variant passes 6 of 16. The first audit reached all twelve account checks and stopped while serializing the gate totals. Both attempts are recorded. Converting those totals to plain JSON types resolves the receipt issue and reuses every completed account. The final audit SHA-256 is `d3f55d842833e584769420b95cbd272c719175c7e992f7506fb63118f7ff9828`.

The retained five-stock rule remains the historical reference. A future qualification still requires stronger performance and an expanding sequence of prospective observations.
