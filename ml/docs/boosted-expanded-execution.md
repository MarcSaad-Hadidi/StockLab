# Boosted model on eight stocks

On a shared $100,000 account from January 2018 through November 11, 2026, the eight-stock model with a relative-strength sale filter earned **17.15% annualized with 29.12% maximum drawdown**. Adding the filter improved the eight-stock strategy by 2.03 percentage points of annual return and reduced its drawdown by 6.72 points. The retained five-stock strategy still leads on both measures at **18.63% annualized and 24.77% drawdown**. The target remains above 20%. [Frozen experiment protocol](../../docs/superpowers/plans/2026-11-25-boosted-expanded-execution.md)

## Data and forecasts

ORLY, BBY and PSX were already selected by the frozen 2017 liquidity screen. All three have split-adjusted OHLCV and positive reported volume on the same 2,228 evaluation sessions. The new ledger has 35 verified cash dividends each for BBY and PSX. Issuer payment dates and amounts agree with the published dated series. The chart event feed independently matches every ex date and amount. BBY's June 2025 record date fell on a holiday, and both dated series put its ex date on the preceding session. The ORLY cash history is empty in the chart feed and consistent with its 2025 annual report and the 2026 quarterly filing. Its June 2025 15-for-1 split is already reflected in the price series. [Best Buy dividend record](https://investors.bestbuy.com/stock-info/dividends/default.aspx) · [Phillips 66 dividend record](https://investor.phillips66.com/stock-information/default.aspx?section=dividend) · [NYSE ex-date policy](https://www.nyse.com/trade/ex-date-dividends) · [O’Reilly annual filing](https://www.sec.gov/Archives/edgar/data/898173/000089817326000009/orly-20251231x10k.htm) · [O’Reilly quarterly filing](https://www.sec.gov/Archives/edgar/data/898173/000089817326000053/orly-20260930x10q.htm)

Nine annual 28-feature HGBT fits use the same 44,320 purged training rows from the original five symbols. The extra symbols participate only in evaluation. All 11,140 original predictions match the retained scores exactly. There are 6,684 additional scores, of which 63 lack the eventual 20-session outcome. Across the 6,621 observed additional outcomes, AUC is .53810 and Brier loss is .29208 versus .25102 for the annual training-prior forecast. Observed discrimination is modest and probability performance remains worse than the simple prior.

## Portfolio results

The original rules govern the eight-stock immediate arm. The relative arm also requires the stock's trailing 20-session close return to be strictly below the same-period SPY return before selling. Both arms and the sale-disabled comparison share the same entry intentions. Trades use the next observed opens and the existing caps. The expanded ledger contains 246 verified cash dividend events. The benchmark is the existing SPY total-return proxy. Costs are 5 basis points per side for fees and another 5 basis points for adverse slippage unless specified. All accounts start with the same capital and have zero deposits after inception.

| Account | Annualized return | Maximum drawdown | Ending equity USD | Buys / sells |
|---|---:|---:|---:|---:|
| Retained five, full | 18.6259% | 24.7658% | 454,173.23 | 223 / 29 |
| SPY, full | 14.1669% | 33.7196% | 323,443.40 | One initial buy |
| Eight immediate, full | 15.1165% | 35.8410% | 348,073.41 | 295 / 40 |
| Eight relative, full | 17.1473% | 29.1208% | 406,404.46 | 307 / 40 |
| Eight without sales, full | 19.2699% | 33.5698% | 476,490.09 | 273 / 0 |
| Eight immediate, full, 25 + 25 bps | 14.4593% | 35.8871% | 330,856.38 | 300 / 39 |
| Eight relative, full, 25 + 25 bps | 16.4947% | 29.1652% | 386,778.93 | 302 / 40 |
| Eight without sales, full, 25 + 25 bps | 19.2123% | 33.5705% | 474,454.44 | 274 / 0 |
| Eight immediate, fresh 2021 | 15.8609% | 24.1820% | 236,926.97 | 199 / 30 |
| Eight relative, fresh 2021 | 16.1087% | 24.1632% | 239,910.71 | 207 / 29 |
| Eight without sales, fresh 2021 | 21.1223% | 19.1607% | 307,340.62 | 188 / 0 |

All three additional stocks trade in each comparison. On the full account the relative filter removes 34 of 346 sell intents and changes subsequent cash and holdings. The same total count of 40 executed sales can conceal different positions and sale dates. The relative arm ends $58,331.05 ahead of the eight-stock immediate arm and $70,085.63 behind its same-entry sale-disabled arm. Its maximum drawdown improves by 4.45 percentage points versus the latter. The fresh sale-disabled account clears 20% on the shorter window with zero sales. Both full sale-enabled accounts and their higher-cost counterparts miss 20% and have greater drawdown than the five-stock reference.

The worst drawdown in each full account reaches its trough on March 23, 2020, from different preceding peaks. PSX accounts for $14,344.69 of the eight-stock immediate account's $50,504.48 peak-to-trough loss and $6,153.52 of the relative account's $44,035.73 loss. These contributions reconcile closing holdings, net trade cash and earned dividends to the equity changes. They describe the held exposure in each arm across its own drawdown dates.

The initial invocation fit nine models, saved the scores, then stopped before the first replay because the prices were ordered by date before symbol. The replay sorts identical rows in the source contract's symbol/date order and reuses those exact scores. It completes all nine accounts without additional fits. The original invocation and both sets of manifests remain archived under the ignored `ml/models/boosted-expanded-20261125` research directory.

The independent file audit verifies all 17,824 forecast keys, 168 unavailable outcomes, 163 bound files and 99,687 numerical ledger comparisons. The maximum absolute ledger difference is `4.66e-10` within the existing tolerance. The immediate and relative arms pass six of 16 and nine of 19 historical gates, respectively. The auditor makes zero fit and zero experiment producer calls. Its receipt SHA-256 is `86b8211c600b133b426ee3b8dda57a442ba7b14f73939527f67cd971113c49da`.

The liquidity filter uses preperiod data in a retrospective provider-available sample. The eight stocks are survivors, and the evaluation years have informed multiple previous experiments. Wider market eligibility, historical terminal proceeds and prospective trade results remain unverified. The retained five-stock reference remains the historical candidate.
