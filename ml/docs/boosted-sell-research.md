# Two further tests of boosted sell decisions

The retained boosted strategy still earns **18.63% annualized with a 24.77% maximum drawdown** through November 11, 2026. SPY, used as an adjusted total-return proxy for the S&P 500, earns **14.17% with a 33.72% drawdown** on the corresponding baseline-cost dates. Neither new sell test clears the joint return and drawdown goal. Earlier forecasts and results remain archived.

All accounts use the same five stocks, USD 100,000 initial capital, 176 verified gross dividends, execution on observed next opens and the existing purchase caps. Both experiments preserve the exact 2,344 full-period BUY intentions and cached raw entry scores. Actual buys can differ because sales change cash and holdings. Fee and slippage are each 5 basis points unless specified. The full period begins January 1, 2018 and 2026 is partial. The 2021 accounts start with fresh capital on January 1 of that year.

| Account | Annualized return | Maximum drawdown | Ending equity USD | Buys / sells |
|---|---:|---:|---:|---:|
| Retained full | 18.6259% | 24.7658% | 454,173.23 | 223 / 29 |
| SPY full | 14.1669% | 33.7196% | 323,443.40 | One initial buy |
| Earlier sells, full | 15.3172% | 20.1154% | 353,486.26 | 330 / 66 |
| Earlier sells, full, 25 + 25 bps | 14.0043% | 20.3337% | 319,385.35 | 330 / 66 |
| Earlier sells, fresh 2021 | 14.3454% | 14.9805% | 219,336.71 | 200 / 45 |
| Payoff-weighted sells, full | 17.7169% | 25.6366% | 424,250.28 | 216 / 29 |
| Payoff-weighted sells, full, 25 + 25 bps | 17.0696% | 25.7701% | 404,023.43 | 219 / 29 |
| Payoff-weighted sells, fresh 2021 | 16.9926% | 13.6219% | 250,811.87 | 129 / 19 |

## Earlier sells using the existing score

Removing only the 200-session trend condition still requires raw score ≤ .35 and negative 20-session momentum. It yields 406 SELL intentions instead of 189, with 217 additions. The initial protocol's descriptive count of two before March 20, 2020 applies to the window starting January 1, 2020. From the beginning of the evaluation in 2018, the correct count is 58. The audit preserves that distinction and the initial checker stop. The count has no role in orders or admission. The original plan remains frozen. [Preregistered sell-only contrast](../../docs/superpowers/plans/2026-11-13-boosted-early-exit.md).

The full account makes 66 sales, 45 of which were enabled by the new predicate. Drawdown drops 4.65 percentage points versus the retained account, but compound growth drops 3.31 points. The fresh account's drawdown rises slightly versus its retained counterpart. All three finish below the immediate and no-SELL controls in wealth. The independent file-only audit validates all three accounts, 24 annual rows, six paired effects and 17 criteria, of which seven pass. There are **153,981 numerical comparisons**, with a maximum absolute difference of `2.91e-11` within the unchanged tolerance. The audit verifies the 53 prior source paths, 84 snapshot bindings, 362 reference files and 27 protected files. The producer uses zero fits, zero calibrations and three account simulations. The auditor makes zero fit, loader or producer calls. Its private report SHA is `be0b69a8ea46acdb41aff5d72b5491124cd010a079f57bc2274f0f332ba89b7a`.

## Payoff weighting for the sell model

The new sell model keeps the same binary 20-session return event, four-year purged annual splits, 28 features and estimator settings. Larger absolute price moves get more training weight. Each year clips absolute distance from the 20-basis-point boundary at its own training-only 90th percentile, then normalizes date weights. The sell rule compares this bounded score to .35 and keeps the retained trend guard. Raw scores keep all original BUY intents. [Preregistered sell head](../../docs/superpowers/plans/2026-11-25-boosted-payoff-weighted-exits.md), [scikit-learn sample weighting](https://scikit-learn.org/stable/modules/generated/sklearn.ensemble.HistGradientBoostingClassifier.html).

Nine HGBT fits use the original 44,320 training rows across the nine splits. The 11,140 predictions retain 105 outcomes unavailable at the end of the period. There are 209 SELL intents and zero collisions with retained BUYs. The same-score BUY-only controls reuse their existing ledgers. At base costs the full strategy gains less than the retained model and its drawdown is 0.87 percentage point higher. The fresh drawdown improves by 1.06 points, but its return also falls. All three annualized returns fall short of 20% and the baseline-cost comparisons lose wealth versus the paired BUY-only accounts.

| Payoff-weighted Brier loss | Full | Fresh 2021 |
|---|---:|---:|
| New sell score | .30347 | .29327 |
| Cached original score under the same evaluation weights | .28019 | .27420 |
| Causal weighted prior | .23823 | .23927 |

The score performs worse than both forecast comparators. Eight of 19 criteria pass. The independent file audit checks all fit rows, thresholds, captured decision scores, three accounts, 24 annual rows and six paired comparisons. It checks **143,664 ledger values** with maximum absolute difference `5.82e-11` under the existing tolerance. It verifies 362 frozen references and 24 exact copies. A checker-only fix corrected the raw dividend-source path; its initial trace is retained. The private audit SHA is `9ebf3c546b22d8f357b6d50053ed80587d71da5155960472e48678acf7dbc207`.

These five stocks survived the full evaluation period, and the same history has already informed prior strategy choices. The prospective evidence and automatic boosted execution remain separate from this retrospective result.
