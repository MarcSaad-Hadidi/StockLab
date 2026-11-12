# Longer-history ML evaluation

The selected research recipe reaches **19.4043% compound annual growth versus 14.1669% for the SPY proxy**, after simulated trading costs and with actual stock cash dividends, from 2018-01-01 through 2026-11-11. **The predefined 20% CAGR target remains unmet.** This is an improvement over the comparable one-day RF recipe, not evidence that the deployed RFv7 has already earned these returns. The candidate is not promoted or deployed.

## Results on the common long period

All accounts start with $100,000, have no deposits/withdrawals/leverage, and share exactly the same observed valuation dates. The interval is 3236 calendar days (nearly 9 years). Costs are 5 bps proportional fee plus 5 bps adverse slippage per executed side. Dividends are gross cash income; taxes/withholding and interest are excluded. Open holdings are marked at the last close without hypothetical final liquidation costs.

|Recipe|CAGR|Maximum drawdown|Buys / sells|
|---|---:|---:|---:|
|One-day RF recipe, annual walk-forward fits, cash dividends|10.4261%|33.8844%|516 /171|
|Selected calibrated 20-session RF + trend confirmation, cash dividends|19.4043%|32.6814%|75 /0|
|Selected recipe,25 bps fee +25 bps slippage per side|19.3445%|32.6873%|76 /0|
|SPY dividend-adjusted buy-and-hold proxy, baseline costs|14.1669%|33.7196%|One initial purchase|

Selected total return is 381.2665% (about $481,267 ending equity), with $31,549 gross dividends earned/paid and no final unpaid receivable. These are simulated historical results, not live trades or realized income in the user's portfolio. No annual loss floor or future return is guaranteed.

The selected strategy executed **zero sales** and largely invested its initial reserve early. Its small cost sensitivity reflects low turnover; these results do not validate effective recurring exits or active portfolio rotation. A maximum drawdown of 32.68% remains substantial. The 20% symbol limit applies at purchase; market appreciation may later make a holding exceed that weight.

|Calendar period|Selected account return|SPY proxy return|
|---|---:|---:|
|2018|-2.81%|-5.02%|
|2019|43.56%|31.21%|
|2020|27.65%|18.32%|
|2021|36.36%|28.72%|
|2022|-13.00%|-18.18%|
|2023|30.88%|26.17%|
|2024|19.88%|24.88%|
|2025|17.92%|17.71%|
|2026 through November 11 only|23.12%|12.29%|

Year returns include the gap from the prior year's last valuation. The 2026 row is partial and is not treated as a full-year return. A simple arithmetic average of the eight complete 2018–2025 annual returns is 20.05%; that is a different statistic from compound growth and is **not** used to pass the predefined 20% CAGR gate.

## What changed

The original target predicts the next session's close relative to today's close. A next-open order cannot capture the intervening overnight gap. New, separately named research targets enter at `open[t+1]` and exit at `open[t+1+h]`, for horizons 5/20/63 sessions. Class 1 requires forward price return>.002, the assumed baseline round-trip friction. An excess 20-session variant instead labels return relative to matching SPY adjusted opens. These definitions never replace the existing `target_up_1d` artifact contract.

Features combine normalized daily price/volume, confirmed monthly DCA range/RSI, trailing 20/63/126/252-session momentum,50/200-session trends,63-session volatility,252-session drawdown and relative SPY momentum. All inputs are trailing; incomplete monthly candles and stale monthly context cannot enter a prediction. Latest rows without known future targets are retained for inference.

Annual fits use up to four previous calendar years and purge the entire target horizon before prediction. Calibrated recipes reserve the latest year of eligible pre-prediction feature history for sigmoid calibration. Base-fit target dates precede the first calibration session; calibration target dates precede the first prediction session. Both subsets need two classes. Calibration uses a fitted frozen estimator, not shuffled cross-validation. See the official [scikit-learn calibration guidance](https://scikit-learn.org/stable/modules/calibration.html).

Fitting a calibration procedure does not establish reliable probabilities in future regimes. The score forecasts the defined horizon event; it is not a guaranteed probability that a completed strategy trade will profit. Directional thresholds remain .65/.35. Symbol exposure 20%, purchase allocation 10%, ten positions and no shorts/leverage remain unchanged.

## Selection and adaptive exploration

The [execution plan](../../docs/superpowers/plans/2026-11-11-long-horizon-ml.md) records the original protocol and every later hypothesis **before its calculation**. Selection uses 2015–2017. A challenger needs higher CAGR than the incumbent and validation drawdown no greater than the original baseline plus .002 (0.2 percentage point). No risk ceiling was relaxed to reach the return target.

1. Six fixed initial recipes: original RF; regularizedRF 5/20-session targets; boosted 20-session target; Logistic 20-session target; boosted excess 20-session target. All challengers fail the original return/risk gate. RF 5-session improves selection CAGR from 5.46% to 15.02% but increases drawdown from 7.78% to 13.73%.
2. Three fixed causal confirmation policies applied to RF 5-session validation predictions. Positive 50-session trend for buys, and negative 200-session trend plus negative 20-session momentum for model-approved sales, passes at 8.30% CAGR/4.50% drawdown. Its long price-only result is 11.40% CAGR/14.82% drawdown, with 23 buys/zero sells and cash-only 2018–2019.
3. Two fixed chronological sigmoid-calibration recipes, both using that same confirmation. Calibrated 5-session produces zero accepted validation orders. Calibrated 20-session wins validation at 10.4947% CAGR/4.7079% drawdown and is frozen before its long evaluation. The long price-only result is 17.9541% CAGR/33.0872% drawdown,24 buys/zero sells, versus SPY 14.1669%.
4. One additional quarterly 63-session hypothesis wins more validation return (14.6797%) but has 12.4093% drawdown, above the original approximately 7.98% ceiling. It is rejected; its final-period returns are not calculated. The retained 20-session prediction files match the preceding run byte-for-byte.

These stages are **adaptive historical research**, not a pristine untouched strategy-selection holdout. Some later market periods were inspected in earlier work. Although no target leaks across an annual fit/calibration boundary, repeated hypothesis development and five surviving symbols limit confidence in generalization. These are 12 recipes/policy variants, not 12 independent statistical experiments. New prospective paper evidence is still required.

## Cash dividends and data integrity

The original study excluded stock dividends while SPY included its provider dividend adjustment. The selected predictions and policy were frozen before correcting this asymmetry. Baseline and selected accounts were then replayed with the same real cash events, with **no refit or reselection**. Signals remain identical; executable purchases/quantities change because actual dividend cash becomes available (24price-only purchases become 75 at baseline costs).

- 176 cash events cover 2018–November 2026:35each for AAPL/JNJ/MSFT/XOM and 36for JPM. Ex-dates, payment dates and amounts come from issuer-rendered tables for [JNJ](https://www.investor.jnj.com/stock-info/dividend-history/default.aspx), [JPM](https://jpmorganchaseco.gcs-web.com/ir/shareholder-information/dividend-history/) and [XOM](https://investor.exxonmobil.com/stock-info/dividends), plus Nasdaq's [AAPL](https://www.nasdaq.com/market-activity/stocks/aapl/dividend-history) and [MSFT](https://www.nasdaq.com/market-activity/stocks/msft/dividend-history) cash histories.
- All 35Apple amounts match its [issuer payment history](https://investor.apple.com/dividend-history/). Pre 2020-08-31 amounts divide by 4 to match the existing split-adjusted share basis. Original source amounts are retained; the adjustment is not inferred from rounding of price ratios.
- Entitlement uses holdings **before** ex-date orders: an ex-date seller keeps the receivable, an ex-date buyer receives none. The receivable is portfolio equity immediately, but cash is spendable only at the first observed session **strictly after** actual payment date. It cannot fund purchases earlier. This conservative timing also handles weekend payment dates.
- Source hashes, complete quarterly counts, observed ex-date calendars and duplicate ex/payment-date checks pass. An ambiguous duplicated XOM payment in 2017 is explicitly retained as an out-of-scope source issue; neither row enters the 2018+cash dataset. No dividend dates or yields are invented.
- The fallback key's observed limit is 8 credits/minute. Twelve Data's [dividend endpoint](https://twelvedata.com/docs/fundamentals/dividends) costs 20 credits/symbol and requires Grow+, so it was not queried and no paid resource was created. Read-only public issuer access supplied the missing events. No new third-party account was created.

SPY is an [S&P 500-tracking ETF](https://www.ssga.com/us/en/individual/etfs/state-street-spdr-sp-500-etf-trust-spy), not the exact S&P 500 Total Return Index. Its Twelve Data `adjust=all` open/close ratios provide the buy-and-hold total-return proxy. Stock executions use genuine observed split-adjusted opens plus explicit cash events. Both now include dividend income, but SPY's implicit reinvestment convention differs from the stock strategy's actual delayed cash availability. Taxes, precise broker payment posting time, ETF tracking differences and corporate actions beyond supported splits/cash dividends remain limitations.

## Reproduce and operating status

Earlier history was acquired with 11paced fallback-key requests. Genuine old/new overlaps match within 1e-5 absolute price precision and exact volumes, without rescaling or interpolation. The five stocks and SPY share 4241 actual dates from 2010-01-04 through 2026-11-11; native monthly files have 203 rows per stock. Monthly warm-up makes common eligible model rows start in 2011. No point-in-time membership/delisted-security universe is supplied.

From `ml`, using the existing environment and `PYTHONPATH=src`:

```powershell
.\.venv\Scripts\python.exe examples/swing_research.py --data-dir models/long-horizon-20261111/dataset --out models/long-horizon-20261111/new-study
.\.venv\Scripts\python.exe examples/dividend_replay.py --study-dir models/long-horizon-20261111/new-study --data-dir models/long-horizon-20261111/dataset --dividend-dir models/long-horizon-20261111/dividend-dataset --out models/long-horizon-20261111/new-dividend-replay
```

Output directories must be new. The input files require their validated manifests. Private sources, cash events, selected predictions, complete accounting curves, orders, source/code hashes, fit boundaries, selection commitments and environment versions are retained under that local research folder; market exports and credentials are not committed. The run records Python 3.12.14, scikit-learn 1.9.1, NumPy 2.5.3, pandas 3.0.6 and the git base plus source hashes. Frozen replay commitments are saved before its first simulation.

Focused/adjacent verification:255 tests pass, including 54 research cases. A reviewer independently reconstructed every daily dividend/cash/equity balance from signed executed quantities and observed events, and verified both calibration boundaries, source hashes and Apple's split basis.

**Production remains RFv7.** This study does not alter the native worker, API runtime, registry, personal portfolio or schedules. New horizon/feature/calibration artifacts are intentionally separate from legacy one-day artifacts. Deployment would additionally require explicit compatible artifact/inference contracts, audited promotion, tested paper execution/restart and prospective observation. The historical 20% CAGR criterion has not passed, and recurring live dividend ingestion is not commissioned by this offline source snapshot.

The next substantive work is portfolio selection/exit validation and a broader defensible instrument universe, rather than another threshold sweep on these same five stocks. This experiment does not mark the ML performance objective complete.
