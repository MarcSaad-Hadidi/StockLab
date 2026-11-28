# Return-first allocation for the boosted portfolio

The eight-stock variant reaches **21.00% annualized from January 2018 through November 11, 2026**, with **26.99% maximum drawdown** and **293 buys and 38 sales**. The same-date S&P 500 proxy earns **14.17%** with **33.72% maximum drawdown**. Both accounts start with USD 100,000 and finish at USD 541,428.99 and USD 323,443.40, respectively. The new variant earns **20.41%** with 25 basis points each for fees and slippage per side. Fresh capital from January 2021 earns **15.83%**, compared with **14.25%** for its matched SPY proxy. The full-period 20% return threshold passes for this candidate. [Frozen return-first study](../../docs/superpowers/plans/2026-11-26-boosted-return-first.md)

## Trading rule tested

The combined variant uses the same eight-symbol HGBT signals and relative-strength sale filter as the audited earlier eight-stock portfolio. When multiple buys share an opening, the previous close's 252-session return relative to SPY determines their priority. Sells retain their previous order and slots. Stronger stocks may reach a 30% post-cost purchase exposure cap. The other names retain the 20% cap. The 10% per-purchase limit, pooled cash, actual next opens and dividend accounting still apply. The backtest uses no leverage. This is a fixed allocation change to a paper portfolio using the original fitted classifier.

## Cost and start-window checks

All configurations use the same market dates within their window. Standard costs are 5 basis points each for fees and slippage per side. The later-start cases below begin with fresh capital from January 1, 2021. The original five-stock reference earned 18.6259% annualized with 24.7658% drawdown. The earlier eight-stock reference earned 17.1473% with 29.1208% drawdown.

| Allocation | Start and cost | Return per year | Maximum drawdown | Ending equity USD |
|---|---|---:|---:|---:|
| Five, BUY priority | Full | 18.6354% | 24.7613% | 454,494.03 |
| Five, BUY priority | Full with higher costs | 18.0520% | 24.8814% | 435,071.88 |
| Five, BUY priority | Fresh 2021 | 17.5747% | 14.6784% | 258,213.28 |
| Five, purchase caps | Full | 18.8327% | 32.0312% | 461,233.75 |
| Five, purchase caps | Full with higher costs | 18.2125% | 32.1412% | 440,339.95 |
| Five, purchase caps | Fresh 2021 | 17.4334% | 17.1970% | 256,399.81 |
| Five, both changes | Full | 18.5923% | 32.1476% | 453,034.37 |
| Five, both changes | Full with higher costs | 17.9849% | 32.2425% | 432,885.22 |
| Five, both changes | Fresh 2021 | 17.5236% | 17.2895% | 257,556.03 |
| **Eight, both changes** | **Full** | **21.0023%** | **26.9939%** | **541,428.99** |
| Eight, both changes | Full with higher costs | 20.4068% | 27.0492% | 518,271.88 |
| Eight, both changes | Fresh 2021 | 15.8270% | 25.5309% | 236,520.66 |

The winning variant adds 3.855 percentage points of annualized return to the earlier eight-stock strategy and 2.376 points to the five-stock reference. Its full-period drawdown is 2.23 points higher than the five-stock reference. Annual returns included **57.90% in 2019**, **42.47% in 2021** and **4.67% in 2025**. SPY returned **31.21%**, **28.72%** and **17.71%** in those years. The differences across years and account starts matter for the next evaluation.

Three additional starts use the frozen eight-stock policy and standard costs through the same November 2026 end. Their independent replay verifies 32,516 numerical values. The five available start dates each beat their corresponding SPY benchmark. Only the full January 2018 start clears 20%.

| Additional fresh-capital start | Return per year | Matched SPY |
|---|---:|---:|
| 2019 | 19.8161% | 17.0914% |
| 2020 | 16.5159% | 14.8090% |
| 2022 | 15.8266% | 11.5040% |

The separate confirmed-repurchase trial preserves each earlier partial-sale policy. After two stable closes it may restore shares from an actual partial sale. On five stocks, it earns 17.9736% full, 17.3323% at higher costs and 17.7435% on fresh 2021 capital, with drawdowns of 23.1676%, 23.3116% and 14.6392%. It executes 14, 13 and four repurchases. On eight it earns 17.1192%, 16.4631% and 16.6318% on those windows with four, four and zero repurchases. Its drawdowns are 33.6329%, 33.6749% and 24.5802%. The allocation variant ranks first in this return phase. [Frozen repurchase protocol](../../docs/superpowers/plans/2026-11-26-boosted-confirmed-reentry.md)

Independent replay checks **235,788 numerical ledger values across 18 accounts** and **288 bound input and output file entries**. The maximum absolute difference is `1.39e-16`. Both producers reuse the archived score streams and make zero fits and zero provider calls. The first audit required a correction to include new repurchases in its metadata join. The final audit reuses all original simulation outputs and has SHA-256 `297f7746d54fd3c4775384fa290bdf83513ad3263a67b0e7109cfc55b9ae0c85`.

## First forward observation of the eight-stock policy

The verified Archived HGBT `v000001` reproduces all **651** additional-stock feature rows and probabilities from the 2026 archived walk-forward reference. It also reproduces the original five November 25 scores. The real eight-stock diagnostic captured at **00:54:24 on November 26, Toronto time** precedes its expected November 27 entry opening. Three authorized Fallback requests supply ten genuine closed daily bars each for BBY, ORLY and PSX. The shared SPY and VIX data come from the earlier hash-bound observation. The recorded decision contains a **BUY signal for BBY** and **seven HOLD signals**. Its status is `PENDING` for the later return outcome. [NYSE market hours](https://www.nyse.com/trade/hours-calendars)

The independent forward-observation check validates all eight decision rules, the shared original scores, the timing and 150 appended OHLCV values. The receipt SHA-256 is `764696646c5c5793a817dbec7cc92a55513ec2a92bc5d005093b8701dc0886a5`. The independent check has SHA-256 `ee5200bcc423e1230c3bf0c8a336e181ade35517836d0684a94fd27e0d54ab9d`. The mode is research diagnostic and execution eligibility is false. The automated paper cycle continues on its registered RF configuration. Recurring eight-stock collection and outcome validation remain open.
