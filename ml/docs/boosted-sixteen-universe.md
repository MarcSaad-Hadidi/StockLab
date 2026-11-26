# Boosted portfolio breadth and Alcoa training correction

The eight-stock HGBT with confirmed DCA purchase priority remains the reference. From January 2018 through November 25, 2026, it earns **21.80% annualized** with a **30.07% maximum drawdown**, compared with **13.36%** and **33.72%** for SPY. Rebuilding the learned ranking after the Alcoa correction lowers its return from the previously reported 21.86% to 21.56%. [Breadth protocol](../../docs/superpowers/plans/2026-11-26-boosted-sixteen-execution.md) · [Earlier rank results](boosted-relative-rank.md)

## Eight stocks through November 25, 2026

Each replay starts with $100,000 in cash, executes eligible decisions at the next recorded open, and charges 5 basis points each for fees and slippage. Maximum drawdown is the largest closing-equity decline from a preceding closing-equity high. The final column is a separate $100,000 replay started in January 2024.

| Strategy | CAGR since 2018 | Strategy max drawdown | SPY max drawdown | Ending value | CAGR from fresh 2024 start |
| --- | ---: | ---: | ---: | ---: | ---: |
| SPY total-return proxy | 13.36% | 33.72% | **33.72%** | $305,091.91 | 16.73% |
| HGBT with confirmed DCA priority | **21.80%** | 30.07% | 33.72% | $578,177.24 | 16.51% |
| HGBT with quarter sales and relative-strength priority | 21.64% | 30.08% | 33.72% | $571,338.18 | 16.12% |
| HGBT with corrected learned purchase priority | 21.56% | 31.27% | 33.72% | $568,098.44 | 16.14% |
| HGBT with the retained-score top-half purchase filter | 21.52% | 30.18% | 33.72% | $566,628.13 | 16.67% |
| HGBT with previously reported learned priority, before the AA correction | 21.86% | 30.08% | 33.72% | $580,675.89 | 16.15% |

At 25 basis points each, the DCA priority earns 21.59%, the corrected learned priority earns 21.35%, and SPY earns 13.30% on the full period. The previously reported model and SPY both reach their deepest trough on March 23, 2020, after their February 19 peak. The model's drawdown is 3.64 percentage points smaller. It recovers its pre-drawdown value on June 5, 2020. SPY recovers on August 10, 2020.

In the separate January 2024 start, the retained DCA strategy's maximum drawdown is 19.80%. The matched SPY drawdown is **18.76%**, with its trough on April 8, 2025.

## Matched breadth test through November 11, 2026

The broader panel adds the next eight names on the fixed 2017 median dollar-volume screen. The same account rules allow up to 10 holdings. Verified quote histories cover 2,228 shared sessions. The accounting adds 164 reconciled cash events to the original 246.

| Strategy | CAGR since 2018 | Max drawdown | CAGR with higher costs | CAGR from fresh 2024 start |
| --- | ---: | ---: | ---: | ---: |
| SPY total-return proxy | 14.17% | 33.72% | 14.12% | 19.38% |
| Eight stocks, confirmed DCA priority | **22.53%** | 30.07% | **22.32%** | 18.79% |
| Sixteen stocks, confirmed DCA priority | 19.39% | 32.12% | 18.32% | 15.05% |
| Sixteen stocks, corrected learned priority | 19.86% | 32.12% | 19.12% | 13.57% |
| Sixteen stocks, corrected learned top-half filter | 21.41% | 31.70% | 21.30% | 16.97% |

The top-half filter on 16 names produces 331 buys and 71 quarter-position sales on the full period. Its annualized return is 21.19% when all TTE cash is excluded. A fresh 2021 start favors the expanded DCA rule at 26.68% versus 18.88% for the eight-stock rule and 14.25% for SPY. That broader comparison has a 40.20% drawdown. The fixed eight-stock DCA reference leads the full and fresh 2024 matched runs. All three broader variants lag SPY on the fresh 2024 start.

## Data, fit, and verification

- Alcoa Corporation began regular trading after the November 1, 2016 separation. The quote archive included the old issuer under the reused AA symbol. The new Alcoa training membership and feature histories start with the standalone company. Its first valid decision row is June 1, 2018. This also revises shared daily ranks in the training panel when Alcoa's membership or return changes. The five-stock directional training population excludes AA. The nine rerun directional estimators reproduce 17,824 archived scores exactly. [Alcoa separation announcement](https://news.alcoa.com/press-releases/press-release-details/2016/Alcoa-Corporation-Launches-as-an-Independent-Industry-Leader-in-Bauxite-Alumina-and-Aluminum-Products/default.aspx)
- Dividend adjustments correct omitted or misstated Eaton entries. The TTE ledger follows the US distribution dates and uses issuer USD amounts after the ordinary-share conversion. Earlier TTE amounts are gross vendor USD estimates. One CNC session with zero quoted volume can value an existing position. Orders on that opening expire unfilled. [Eaton August 2018 distribution](https://www.eaton.com/kr/ko-kr/company/news-insights/news-releases/2018/eaton-declares-quarterly-dividend-payable-august-17--2018.html) · [Eaton May 2020 distribution](https://www.eaton.com/us/en-us/company/news-insights/news-releases/2020/Eaton-declares-quarterly-dividend-payable-May-29-2020.html) · [TotalEnergies dividend calendar](https://totalenergies.com/investors/shares-and-dividends/dividends)
- Nine refit ordinal estimators use strict annual label-maturity cutoffs. Independent verification reproduces 35,544 ordinal scores and 373,750 ledger and benchmark values from 34 accounts. The separate November 25 recheck uses the corrected estimators on the original eight stocks and reproduces the archived SPY curve for all three matched windows. The provider-available universe omits delisted names. Each strategy uses a historical research sample.

The replay summary SHA-256 is `4f7ac585143e9828b2c8c4254eceb7df99fa33af86deb6c25138941af91b0259`. The independent verification receipt SHA-256 is `910698bdf9ed604de35048d465729f192139c161e8f671a56c8df0e81c5d84d1`. The November 25 recheck summary SHA-256 is `6b0bc6d94d294712cb39099e0a03f384003bfd0f039a676f37a24a7816ba2b70`.
