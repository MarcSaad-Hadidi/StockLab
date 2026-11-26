# Learning to rank the boosted model's eligible purchases

The subsequent standalone-Alcoa correction gives 21.56% annualized for the learned eight-stock priority on this same period. The confirmed eight-stock DCA priority remains the reference at 21.80%. The original experiment and its 21.86% result are recorded below. [Corrected comparison and broader replay](boosted-sixteen-universe.md)

The new learned purchase priority earns **21.86% annualized** from January 2018 through November 25, 2026. The confirmed DCA reference earns **21.80%** and SPY earns **13.36%** on the same dates. The new portfolio finishes at $580,675.89 after 315 buys and 59 quarter-position sales. Its maximum drawdown is 30.08%. The 2021 fresh account rises from **17.84% to 19.77% annualized**, while its drawdown falls from 21.91% to 18.77%. The best result from a fresh 2024 start is **16.67%** with the retained-score filter, compared with **16.73% for SPY**. [Ranking model protocol](../../docs/superpowers/plans/2026-11-26-boosted-relative-rank-training.md) · [Retained score protocol](../../docs/superpowers/plans/2026-11-26-boosted-retained-score-priority.md)

## Learning and forecast results

Both newly fitted models use the same four-year purged annual training sets and 28 inputs. One fits the bounded cross-sectional rank of the twenty-session next-open excess price return. The matched classifier fits the original absolute binary price event. They use 229,328 observed outcomes from 110 learning identities in the archived 111-identity panel. Nine regressors and nine classifiers each score all 17,904 observations on the verified eight-stock portfolio. The stock labels use split-adjusted price returns. The account ledgers include cash distributions. The ordinal output measures the predicted relative order of stocks. [Cross-sectional ranking research](https://arxiv.org/abs/2012.07149)

| Score used to rank the eight stocks | All known pairs ordered correctly | Known pairs since 2024 ordered correctly |
|---|---:|---:|
| Learned ordinal score | 50.53% | 49.80% |
| Matched binary score | 49.76% | 48.32% |
| 252-session relative strength | 51.12% | 49.90% |
| Retained boosted probability | 51.13% | 52.33% |

The forecast comparison uses 62,076 known pairs on 2,217 sessions. The latest subset uses 19,824 pairs on 708 sessions. The realized twenty-session outcomes overlap. The ordinal score improves on the matched binary classifier in both periods. The original boosted score leads the recent comparison. These measurements describe ranking quality at the forecast horizon. The account tests measure purchase competition and retained exposure over a longer period.

## Matched account returns

All runs start with $100,000 and trade against actual next opens. Quarter-position sales, 20% and 30% buy exposure limits, gross dividend events, and the choice of standard or higher costs are shared. The priority variants use their indicated score within a confirmed DCA band and preserve all original eligible BUY requests. The half variants allow new purchases only in the top four of the eight observable stocks. The strength control uses the 252-session relative return. The retained controls use the original HGBT score.

| Start and costs | DCA reference | Learned priority | Learned half | Strength half | Binary priority | Retained priority | Retained half | SPY |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 2018 | 21.80% | **21.86%** | 21.14% | 21.24% | 21.84% | 21.65% | 21.52% | 13.36% |
| 2018, higher costs | 21.59% | **21.65%** | 20.95% | 21.06% | 21.63% | 21.45% | 21.32% | 13.30% |
| 2019 | 20.87% | 20.89% | 19.73% | **22.72%** | 20.83% | 20.56% | 20.06% | 16.14% |
| 2020 | 16.74% | 16.74% | 15.96% | 15.74% | **17.08%** | 16.38% | 16.20% | 13.75% |
| 2021 | 17.84% | **19.77%** | 19.62% | 14.40% | 17.88% | 19.42% | 17.54% | 13.03% |
| 2022 | 16.34% | 17.82% | 14.16% | **17.90%** | 17.35% | 17.03% | 15.81% | 10.09% |
| 2024 | 16.51% | 16.15% | 14.21% | 15.55% | 16.11% | 16.56% | **16.67%** | 16.73% |

The learned priority has the strongest median across the six standard-cost starts at **18.79%**, compared with 17.29% for the DCA reference. It clears 20% from two starts and beats SPY from five. All six alternatives trail SPY from the 2024 start. The full-window and higher-cost improvements are each about 0.06 percentage point. The most recent independent funding window remains the priority for return improvement.

## Audit and prospective observation

An independent verifier reconstructs all 229,328 training labels, checks the annual purges and equal-date weight, loads the eighteen bound estimator files and reproduces their scores. It checks 5,689,392 training and score values and 44,340 ranking metrics. A second oracle verifies 56 account ledgers, including 42 new variants and 14 reference checks, across 621,332 numeric comparisons. The maximum ledger difference is `1.39e-16`. The SHA-256 digests are `b58a514739d5760127e09fb94800fa493c1f4f9568cf6047872eaf7e0c953d8d` for the fit audit and `ef1b524ffe404248049ec81b23882cb406a19974b455fcc10a47760e31a6e456` for the account audit. All calculations use cached provider inputs.

The 2026 models score the already captured November 25 inputs before the November 27 opening. The learned-priority portfolio includes BBY as the sole eligible BUY. The learned-half filter omits that buy. This gives a dated prospective comparison with outcomes pending. The markets in the historical training pool are drawn from provider-available surviving names, and the execution comparisons reuse eight symbols and previously inspected periods. The RFv7 paper portfolio and existing local boosted observer continue to run.
