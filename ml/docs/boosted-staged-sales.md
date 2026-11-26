# Boosted portfolio with quarter-position sales

The eight-stock portfolio with 25% sales reaches **22.36% annualized** from January 2018 through November 11, 2026. It improves the previous **21.00%** full-period result and the matched SPY benchmark's **14.17%**. The $100,000 account finishes at **$597,870.67** after 325 buys and 59 partial sales. The previous portfolio finishes at $541,428.99 and SPY at $323,443.40. [Frozen staged-sale protocol](../../docs/superpowers/plans/2026-11-26-boosted-staged-sales.md) · [Previous allocation](boosted-return-first.md)

## Matched return and drawdown

The same scores, purchase rules, dividend events and actual next opens feed all accounts. A quarter sale releases up to 25% of the held shares at the next open following an original SELL signal. Selected sale signals for a symbol must be at least 20 trading sessions apart. The trend-confirmed alternative sells the entire position when the stock also trails SPY over 252 sessions and uses a spaced quarter sale otherwise. The buy-only control preserves those same purchase rules and disables sales. Standard fees and slippage are 5 basis points each per side. The higher-cost run uses 25 basis points each.

| Account start and costs | Previous CAGR | Buy-only CAGR | Quarter-sale CAGR | Trend-confirmed CAGR | SPY CAGR | Quarter-sale drawdown |
|---|---:|---:|---:|---:|---:|---:|
| 2018 | 21.00% | 21.47% | **22.36%** | 21.90% | 14.17% | 30.08% |
| 2018, higher costs | 20.41% | 21.41% | **22.16%** | 21.47% | 14.12% | 30.09% |
| 2019 | 19.82% | **22.59%** | 21.70% | 20.82% | 17.09% | 39.80% |
| 2020 | 16.52% | 15.79% | 17.40% | **18.18%** | 14.81% | 33.45% |
| 2021 | 15.83% | **20.14%** | 18.92% | 18.01% | 14.25% | 22.02% |
| 2022 | 15.83% | 16.83% | **17.59%** | 17.38% | 11.50% | 17.76% |

Quarter sales improve all six paired returns and outperform SPY at every cost and start. They lead the sale-enabled alternatives on median standard-cost CAGR at **18.92%** and clear 20% from two starts. The buy-only control clears 20% from three starts and has a **20.14%** median. Its full-period CAGR trails the quarter-sale portfolio by 0.89 percentage point. Both sale-enabled alternatives advance to prospective comparison under the frozen rules. The quarter-sale candidate has the highest full-window return and retains 59 genuine model sales.

Full-period maximum drawdown rises from 26.99% to 30.08% and remains below SPY's 33.72%. The quarter-sale account started in 2019 has a 39.80% drawdown, above the 33.72% matched benchmark. The fresh 2021 drawdown improves from 25.53% to 22.02%. These accounts use a fixed sample of eight surviving stocks and overlapping periods that have informed earlier choices.

## Sale timing and verification

Of 59 partial sales on the full run, 21 have higher immediate net proceeds than holding the same sold shares to the 20th later open. Taken together, the immediate sales return $6,613.66 less than that fixed-share holding comparison. The same calculation across the previous 38 complete sales gives a $18,528.01 shortfall. The account-level comparison includes how cash is redeployed and reflects the different trade sizes and dates.

The independent replay validates 18 accounts, all retained source signals, share rounding, sale spacing, benchmark values, 208 bound files and **215,622 ledger values**, with a maximum difference of `1.67e-16`. It also checks 2,772 paired sale values. No models are refitted and the tests make zero data-provider calls. The audit SHA-256 is `5b81c78ff5b1b12fccb4ce1b5a23780c001edebf119ab4162806e8b90b09f42b` and the summary SHA-256 is `0e6aa4be6ca5b187c71b37c48d7ad39d0143c67e66308d3507fb82efd82cc3c1`. The local automatic paper portfolio continues on its registered RF model while boosted evidence is gathered.
