# Boosted purchase selection on eight stocks

Using confirmed monthly DCA zones to order the boosted model's BUYs gives the best full-window result so far. The portfolio earns **22.53% annualized** from January 2018 through November 11, 2026. The quarter-sale reference earns **22.36%** and SPY earns **14.17%**. The new run finishes at $604,909.81 after 319 buys and 59 partial sales. It earns **22.32%** in the higher-cost replay. Its median across the five start dates, 18.88%, is slightly below the reference's 18.92%. [Quarter-sale reference](boosted-staged-sales.md) · [DCA priority and sizing protocol](../../docs/superpowers/plans/2026-11-26-boosted-dca-order-sizing.md)

## Confirmed DCA zones

The test applies the user's existing monthly 2×, 3× and 4× zones only when the boosted model already signals a BUY. It reads the latest confirmed calendar month, orders simultaneous purchases by the zone and then preserves the original relative-strength tie rule. Historical fills still use the next observed opens. The study also tests all-in per-order limits of 10%, 15% and 20% in those zones. The account reaches those limits only if it has the cash and room under the existing post-cost symbol cap. [DCA port and signal semantics](dca-next-open-trim25.md)

| Account start and costs | Quarter-sale reference | DCA priority | DCA sizing | DCA priority and sizing | SPY |
|---|---:|---:|---:|---:|---:|
| 2018 | 22.36% | **22.53%** | 22.36% | **22.53%** | 14.17% |
| 2018, higher costs | 22.16% | **22.32%** | 22.16% | **22.32%** | 14.12% |
| 2019 | 21.70% | 21.72% | 21.70% | 21.72% | 17.09% |
| 2020 | 17.40% | 17.63% | 17.40% | 17.63% | 14.81% |
| 2021 | 18.92% | 18.88% | 18.92% | 18.88% | 14.25% |
| 2022 | 17.59% | 17.48% | 17.59% | 17.48% | 11.50% |

The full-window priority run executes 19, six and one purchase in the 2×, 3× and 4× zones, respectively. It beats SPY in all five standard-cost windows and clears 20% from two. The higher per-purchase limit changes zero fills across six matched buy tapes. The combined and priority-only trade files also match exactly in all six windows. The measured gain comes from changing which eligible buy receives scarce cash first. Full-window drawdown is 30.07%. The quarter-sale reference stays in the local observer while more prospective evidence is gathered.

## Filtering and rotating invested capital

The quarter-sale reference holds under 1% average cash in each of the fresh 2020, 2021 and 2022 accounts. The first experiment therefore removes a BUY when both its 63- and 252-session returns lag SPY. The rotation arm instead keeps the original BUY set and can trim a separate weak holding to fund an eligible new purchase. It spends the actual sale proceeds and makes at most one funded rotation per month. The combined arm tests these rules together. [Capital rotation protocol](../../docs/superpowers/plans/2026-11-26-boosted-capital-rotation.md)

| Account start and costs | Quarter-sale reference | Purchase filter | Capital rotation | Filter and rotation | SPY |
|---|---:|---:|---:|---:|---:|
| 2018 | **22.36%** | 21.81% | 22.07% | 21.86% | 14.17% |
| 2018, higher costs | **22.16%** | 21.61% | 21.80% | 21.60% | 14.12% |
| 2019 | 21.70% | 23.64% | 20.94% | **24.45%** | 17.09% |
| 2020 | **17.40%** | 16.95% | 16.96% | 17.09% | 14.81% |
| 2021 | 18.92% | **19.06%** | 18.56% | 18.93% | 14.25% |
| 2022 | 17.59% | 17.70% | 17.82% | **18.23%** | 11.50% |

Rotation alone executes 24 funded sale-and-purchase pairs over the full window. Its median return is 18.56%, compared with 18.92% for the reference. The purchase filter lifts the median to 19.06% and weakens the full-period result. The combined arm improves the 2019 and 2022 starts and produces a 21.86% full-period result. All three beat SPY over all five standard-cost runs. Each clears 20% from two starts.

## Independent verification

The capital-rotation audit verifies eighteen accounts, 212 files and 221,386 ledger values. Its maximum difference is `1.39e-16`. The DCA audit verifies 1,499 monthly rows, all zone-to-signal joins, eighteen accounts, 197 files and 218,790 ledger values. The maximum indicator difference is `2.84e-14` and maximum ledger difference is `1.67e-16`. The two audit receipts have SHA-256 `deed66cae015f0fc0de0a9c42fa8ce4c74fbbe0c825b42c71ba4f5e4ba718a8b` and `2680d684634d8c3b765136d3e4eda852a3d713e56abebd384d31cdd4e865fe03`, respectively. The tests use zero model refits and zero provider calls.

The backtests reuse a fixed sample of eight surviving stocks and overlapping years. The larger full-window gain from DCA priority leaves the recent-start 20% target open. The local native paper portfolio continues on RFv7.
