# Archived startup cash experiment

The AI Trader portfolio is restricted to individual stocks, with SPY used exclusively as a benchmark. This archived experiment allocated portfolio cash to SPY, so its 21.91% full-period and 18.61% fresh-2024 results are excluded from candidate selection. The eight-stock DCA reference is the eligible comparison. [Stock-only portfolio results](boosted-sixteen-universe.md)

Temporarily investing idle cash in SPY lifts the eight-stock boosted DCA portfolio from **16.51% to 18.61% annualized** with a fresh January 2024 start, against **16.73% for SPY**. Maximum drawdown is **19.72%** for the candidate and **18.76%** for SPY. From January 2018, the candidate earns **21.91% annualized** with a **30.07%** drawdown, compared with **13.36%** and **33.72%** for SPY. All accounts end on November 25, 2026. The quarter startup rule uses the existing stock model, eight-symbol universe, DCA purchase priority and quarter-sale rules. [Frozen replay protocol](../../docs/superpowers/plans/2026-11-26-boosted-startup-cash.md) · [Reference portfolio](boosted-sixteen-universe.md)

## Source of the recent shortfall

The verified fresh portfolio had 28.53% of equity invested on average in January 2024, 49.51% in February and 53.50% in March. The first MSFT fill came on May 14. MSFT averaged 5.5% of equity during that account's 2024 calendar year and 30.4% in the account started in 2018. The established account returned 17.03% in 2024. The fresh account earned 6.05% in 2024 before the change and 11.64% in the quarter-startup test. The accounting attribution reconciles 8,901 values across the two unchanged account histories. Fresh capital initially exposed the portfolio to both more cash and a different set of stocks.

## Same-date portfolio and benchmark comparisons

The two startup rules invest cash left after stock orders using actual NYSE Arca SPY openings. A stock order can release just enough of the temporary ETF position to fund the purchase. The rules release any remaining SPY at the 64th opening. The trend variant additionally requires that SPY's preceding close was above its trailing 200-session moving average. All simulations start with $100,000.

| Fresh start | Eight-stock reference CAGR | Quarter startup CAGR | Trend-guarded CAGR | SPY CAGR | Quarter startup max drawdown | SPY max drawdown |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 2018 | 21.80% | 21.91% | 21.91% | 13.36% | 30.07% | 33.72% |
| 2018, higher costs | 21.59% | 21.61% | 21.61% | 13.30% | 30.07% | 33.72% |
| 2019 | 20.87% | 22.08% | 20.88% | 16.14% | 39.82% | 33.72% |
| 2020 | 16.74% | 16.49% | 16.63% | 13.75% | 34.71% | 33.72% |
| 2021 | 17.84% | 17.66% | 17.66% | 13.03% | 21.91% | 24.50% |
| 2022 | 16.34% | 15.94% | 15.94% | 10.09% | 17.64% | 24.50% |
| 2024 | 16.51% | 18.61% | 18.61% | 16.73% | 19.72% | 18.76% |

The January 2024 account ends at $164,021.12, with 116 stock buys, 27 quarter-position sales and 25 ETF fills. The recent annualized improvement is 2.10 percentage points. It clears the date-matched benchmark and is 1.39 points below 20%. The full-window gain with higher fees is 0.02 points. Results from the 2020, 2021 and 2022 starts are weaker than their individual stock-only references. At standard costs both startup rules beat SPY from all six starts.

## Data and verification

The compiler reconciles 2,238 trading sessions and 35 SPY distributions. It uses State Street's payable dates and exact USD cash amounts, including the $1.636431 payment for the December 2021 ex-date confirmed by the issuer's separate securities report. The two adjusted quote histories differ by at most 0.053% in cumulative growth through the shared dates. ETF fills use split-only market prices. [State Street distribution workbook](https://www.ssga.com/library-content/products/fund-data/etfs/us/spdr-etf-historical-distributions.xlsx) · [Issuer securities report](https://www.ssga.com/library-content/products/fund-docs/etfs/apac/jp/Securities_Report_SPY.pdf)

An independent ledger reconstruction checks **145,937** values across all 16 replays, including next-open scheduling, funding costs, the post-cost stock cap, actual share sales and dividend entitlements. Disabling the sleeve reproduces the archived January 2018 and January 2024 equity and trade files exactly. The strategy is a retrospective portfolio change. The forecasts come from the existing HGBT and the variant remains in research.

Replay summary SHA-256 `7265f429b15761935325f75ad2257bf31f6e23527bf5d24d2670a7c74f1eecaf`. Audit receipt SHA-256 `b4db159faed2512d2bb42b3f8b26f547209a7b1df06641d183be40d030fde4cf`.
