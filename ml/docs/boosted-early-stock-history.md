# Boosted AI Trader from 1993 through 2026

The directional boosted portfolio earned **21.22% annualized** from the first observed SPY opening on January 29, 1993 through November 25, 2026. SPY earned **10.55%**. Maximum drawdowns were **52.74%** and **55.19%**, respectively. Starting the same stock strategy with fresh capital in January 2000 produced **17.94%** with a **62.57%** drawdown. The long account clears the return target, while fresh starts expose material differences in risk and performance. [Frozen research protocol](../../docs/superpowers/plans/2026-11-26-boosted-early-stock-history.md)

## Fresh starts and matched SPY

All accounts start with $100,000 and end on November 25, 2026. Annualized returns are compound growth rates. The standard fee and slippage are 5 basis points each per side. The higher-cost runs use 25 basis points each. Stock orders use actual following opens and charge both costs. The ledger records outstanding issuer dividends on their ex-date and makes the cash available after the verified payable date. SPY is an adjusted-price proxy for dividends reinvested, with the same opening cost assumption.

| First session | Directional CAGR | Maximum drawdown | SPY CAGR | SPY maximum drawdown | Fills |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1993-01-29 | **21.22%** | 52.74% | 10.55% | 55.19% | 1,137 |
| 1993-01-29, higher costs | **21.06%** | 52.78% | 10.54% | 55.19% | 1,128 |
| 2000-01-03 | 17.94% | 62.57% | 7.90% | 55.19% | 982 |
| 2000-01-03, higher costs | 17.78% | 62.71% | 7.88% | 55.19% | 987 |
| 2008-01-02 | 17.68% | 39.62% | 10.74% | 52.45% | 746 |
| 2018-01-02 | 19.22% | 31.05% | 13.36% | 33.72% | 392 |
| 2024-01-02 | 14.22% | 18.25% | 16.73% | 18.76% | 121 |

The 1993 account finished at $67,183,110.07 versus $2,974,511.84 for SPY. It took 899 buys and 238 quarter-position sales. The 2000 account fell from $124,845 on March 23 to $46,724 on December 20, 2000. Following a fresh start in 2024, this stock-only configuration invested 33.49% of equity on average in its first 21 sessions.

## Learned purchase priority and monthly sales

The four policy comparisons share the same next-open price targets. Learned priority adds a separately trained 63-session stock rank for choosing simultaneous buys within a DCA band. The monthly alternative adds a 25% sale when a confirmed DCA signal enters `STRONG_TRIM`. It shares the 20-session sell spacing with the daily signal and executes on the following opening. The combined alternative uses both changes.

| Fresh start | Directional CAGR | Learned priority CAGR | Monthly trim CAGR | Combined CAGR | Buy-only CAGR |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1993 | 21.22% | 21.32% | 20.79% | 20.21% | 18.15% |
| 1993, higher costs | 21.06% | 21.15% | 20.52% | 19.96% | |
| 2000 | 17.94% | 17.69% | 16.05% | 15.58% | |
| 2000, higher costs | 17.78% | 17.52% | 15.81% | 15.35% | |
| 2008 | 17.68% | 17.37% | 17.67% | 17.17% | |
| 2018 | 19.22% | 18.68% | 19.55% | 18.57% | |
| 2024 | 14.22% | 13.99% | 14.68% | 14.42% | 16.25% |

The learned priority earns 0.10 percentage point more per year in the 1993 account and lifts its drawdown from 52.74% to 54.06%. The directional result leads the learned model at the other four standard-cost starts. The monthly trim improves the fresh 2018 and 2024 returns but reduces the fresh 2000 result by 1.89 points per year. Without sales, the 1993 drawdown rises to 59.42%. The 2024 buy-only account beats each of the four sale-enabled strategies and still trails SPY.

In the 1993 directional account, immediate proceeds exceed the net 20th-following-open value of the same shares on 87 out of 238 matched sales. The 21.22% account-level return captures how the cash from sales was redeployed. The matched-share test measures sale timing on its own.

## Trend filter and passive stock comparison

The 200-session filter requires the stock to close above its own long moving average before a buy. It reduces the 1993 drawdown to 50.11% and reduces the full-period return to 19.76%. It also reduces the 2000 CAGR by 1.63 percentage points, with essentially the same drawdown. The directional model remains the long-history research reference.

The passive comparison keeps an equal initial allocation for each of the eight stocks and holds its shares after the first eligible opening. Reserves for delayed stocks stay in cash until their first purchase. Each sleeve reinvests its own shareholder distributions after payment. The passive strategy is a control for the effect of choosing the same stocks and does not use the model scores. It can become concentrated over time. AAPL accounts for 44.72% of the passive portfolio at the end of the 1993 run. [Follow-up replay protocol](../../docs/superpowers/plans/2026-11-26-boosted-early-stock-trend.md)

| Fresh start | Trend filter CAGR | Maximum drawdown | Passive stocks CAGR | Maximum drawdown |
| --- | ---: | ---: | ---: | ---: |
| 1993 | 19.76% | 50.11% | 17.43% | 50.42% |
| 1993, higher costs | 19.59% | 50.40% | 17.41% | 50.42% |
| 2000 | 16.32% | 62.56% | 17.80% | 44.04% |
| 2000, higher costs | 16.16% | 62.73% | 17.78% | 44.03% |
| 2008 | 16.65% | 42.79% | 16.95% | 36.71% |
| 2018 | 19.14% | 31.04% | 18.24% | 36.64% |
| 2024 | 14.84% | 18.25% | 20.80% | 18.02% |

The directional account earns more than SPY in all five disjoint periods when it carries its positions from 1993. Its return is above 20% in two periods. The passive account leads the directional model in the two most recent periods.

| Continuous account period | Directional CAGR | Trend filter CAGR | Passive stocks CAGR | SPY CAGR |
| --- | ---: | ---: | ---: | ---: |
| 1993–1999 | 39.98% | 39.69% | 31.01% | 21.32% |
| 2000–2007 | 15.58% | 10.72% | 8.11% | 1.56% |
| 2008–2017 | 15.22% | 14.72% | 12.73% | 8.45% |
| 2018–2023 | 20.94% | 20.59% | 23.15% | 11.96% |
| 2024–2026 | 17.43% | 17.77% | 18.56% | 16.45% |

The extra audit reconciles 255,053 values across seven trend ledgers and 348,292 across seven passive ledgers. The prospective paper model remains RFv7. This retrospective result is research evidence, with universe selection and fresh-account stability still open.

## Data and verification

The account holds only AAPL, BBY, JNJ, JPM, MSFT, ORLY, PSX and XOM. Five have model forecasts by January 29, 1993. ORLY enters the forecast universe in December 1994, the post-merger JPM in August 2002 and the standalone PSX in December 2013. Eligibility requires sufficient actual history. SPY never enters features, labels, order priority, capital or holdings. The stock sample consists of eight survivors chosen today. Results may benefit from that universe selection, and different fresh-start accounts share many market years. [State Street SPY](https://www.ssga.com/us/en/individual/etfs/state-street-spdr-sp-500-etf-trust-spy) · [JPMorgan Chase annual filing](https://www.sec.gov/Archives/edgar/data/19617/000095012303002985/y83354e10vk.htm) · [Phillips 66 distribution notice](https://www.sec.gov/Archives/edgar/data/1534701/000119312512200916/d341711dex991.htm)

The market input contains 70,112 stock bars, 690 reconciled cash distributions and 8,515 benchmark sessions. Both 200-iteration histogram gradient boosting models train annually on a rolling four-year window. Each outcome matures strictly before the first forecast of its fit year. The directional target requires a gain of more than 0.2% across 20 sessions from the next open. The other model predicts the relative rank of 63-session returns among the available individual stocks. Dividends are credited in the replay ledger and fill prices use split-only stock histories.

Independent verification reproduced 120,022 scores from 68 fitted models. It checked 1,093,589 values across 30 ledgers, including stock and date eligibility, fills, fees, caps, dividend entitlements and matched benchmark equity. The original protocol and data are pinned by hash. The exact replay and audit artifacts are retained locally in `ml/models/boosted-early-market-20261126/`.

| Receipt | SHA-256 |
| --- | --- |
| Stock and benchmark input manifest | `b4a8e10c8407ad6021c8b9f875568a54640fe61a0d142dc03229d06586c0a3e8` |
| Model fit summary | `df964cd7c7bc9bbc0f9d58d8743aacb01498790dded7417585d3032a0089884f` |
| Original policy replays | `536124e9f4f37df819029bc235ad0e5aae501da880ab7b0b1baffae3ebe7b5a9` |
| Original independent audit | `9c0a784dd1518ea37414377db23111337d9fc1d075524d8e499d5e2dcb6abdec` |
| Trend and passive replays with trend audit | `7844856d3b871c9857462a0e6a27d5b8ff68fabd7cad0d9e14ff9590d40465d1` |
| Independent passive audit | `4305ca27f91a5429e98b2c6068a100affd0bf18dda07b4e5b78280c05d693b92` |
