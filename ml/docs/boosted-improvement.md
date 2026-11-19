# Boosted training-domain and continuous-return research

The user requested continued improvement toward more than 20% annual compound growth, SPY outperformance, effective sales and lower drawdown. Three separately fixed diagnostics follow the previous DCA comparison: original-five upside training, a continuous-return model, then stronger minimum-leaf regularization. These add to the [earlier research](long-horizon-ml.md); they are not the whole search history. These are adaptive historical experiments, not promoted models or untouched validation.

**Outcome:** original-five binary training remains the strongest boosted candidate in this batch, at **18.6259% CAGR / 24.7658% maximum drawdown**, versus the preceding broader reference's 16.1739% / 31.9147% and SPY's 14.1669% / 33.7196%. Neither subsequent strategy improves it. The >20% compound-growth objective remains unmet on both full and fresh-2021 accounts. Production remains RFv7.

The [following horizon, exit, SPY-context and cadence studies](boosted-exit-controls.md) preserve this batch unchanged. Five-session exit confirmation increases full CAGR to19.1415% while worsening drawdown to29.6675%; direct SPY inputs lower drawdown to23.0973% while reducing CAGR to17.9741% and failing fresh-account SPY outperformance. Monthly refitting falls to16.6715%/33.8450%. Entry-quarter exits earn18.9329%/30.9147% full and19.3892%/14.0444% fresh2021. These disclosed outcomes do not fulfill the joint objective.

## Original-five upside training

Change only the upside training population from the broader corpus to AAPL/JNJ/JPM/MSFT/XOM. Keep the same 28 features, executable 20-session label, annual four-year known-date purges, date weighting, fixed HGBT parameters, .65/.35 thresholds, trend confirmations and account rules. The [protocol](../../docs/superpowers/plans/2026-11-12-boosted-upside-domain.md) was committed before the nine new fits. Prior experiments and rejected admissions remain unchanged.

All accounts below start with **100,000 USD**, have no later deposits and cover **2018-01-01 through 2026-11-11**. Stock fills use 5 bps fee plus 5 bps adverse slippage per side and the same 176 genuine gross dividends. The 20% symbol and 10% purchase limits apply at execution; appreciation can subsequently increase a holding's weight. SPY is the existing adjusted ETF total-return proxy, not the exact S&P 500 Total Return Index. 2026 is partial.

|Account|CAGR|Maximum drawdown|Ending equity|Buys / sells|
|---|---:|---:|---:|---:|
|Original-five upside candidate|18.6259%|24.7658%|454,173.23 USD|223 / 29|
|Unchanged broader boosted reference|16.1739%|31.9147%|377,444.36 USD|88 / 8|
|SPY proxy|14.1669%|33.7196%|323,443.40 USD|One initial purchase|

The candidate improves full-period CAGR by 2.4520 percentage points and reduces maximum drawdown by 7.1490 points versus the broader reference. It still misses 20%. Its fixed higher-cost account, using the SAME forecasts with 25+25 bps execution costs, earns 18.0422% CAGR with 24.8852% drawdown and 434,749.92 USD ending equity; it executes 221 buys and 29 sales.

The original reference's mean investment was only 46.13% in 2024 and 41.42% in 2025. The candidate raises these to 79.48% and 72.53%, with 88.00% mean investment over the full interval. This describes the joint entry/exit result. A training-domain change affects both buys and sales; more executed sales alone do not establish a causal exit benefit.

### Fresh capital from 2021

The same forecasts, sliced without extra fitting, also drive a separate account starting with 100,000 USD on 2021-01-01.

|Account|CAGR|Maximum drawdown|Ending equity|Buys / sells|
|---|---:|---:|---:|---:|
|Original-five candidate|17.5778%|14.6771%|258,252.42 USD|135 / 19|
|Broader reference|14.9254%|13.0059%|225,935.34 USD|67 / 8|
|SPY proxy|14.2527%|24.4997%|218,296.20 USD|One initial purchase|

The candidate's later return improves, but its fresh-account drawdown is higher than the broader reference's, while remaining below SPY. Both candidate account starts miss 20%; neither is substituted for the other.

### Annual returns and forecast quality

|Year|Original-five candidate|Broader reference|SPY|
|---|---:|---:|---:|
|2018|-3.85%|-3.48%|-5.02%|
|2019|36.27%|43.17%|31.21%|
|2020|29.37%|27.83%|18.32%|
|2021|41.36%|35.66%|28.72%|
|2022|-0.33%|-13.40%|-18.18%|
|2023|19.61%|32.04%|26.17%|
|2024|11.37%|8.47%|24.88%|
|2025|17.99%|10.85%|17.71%|
|2026 through November 11|21.01%|14.54%|12.29%|

Calendar returns include the preceding year-end gap. Their arithmetic average is not used to claim compound growth. The candidate still substantially underperforms SPY in 2024 and does not outperform the broader reference every year.

On observed full-period outcomes, upside AUC improves from .47860 to .51954, but candidate Brier .28525 is worse than both its actual five-history causal fitting prior (.24584) and the unchanged broader prior (.25065). Later AUC is .52879 and Brier .27793, also worse than its .24759 own and .25213 broader priors. Original training prevalence and broader-reference prevalence are retained separately. The positive account result does not establish calibrated probabilities.

### Independent verification

Eight synthetic driver checks pass. A separate auditor performs no model fits or producer simulations. It verifies all nine exact original-five fit populations, four-year purges, date weights, all nine broader priors, 28 features, 3,269 macro rows and 36 unchanged reference evidence files. It reconstructs every decision, next-open fill, cash/dividend balance, position, benchmark, annual return and account metric.

There are **32,344 numeric ledger comparisons with maximum error 0.0**: 12,178 full, 12,170 higher-cost and 7,996 fresh-account checks. Executed sales have genuine model scores <=.35; purchases have scores >=.65. The full forecast SHA256 is `f53007c0ad1de79ba3fe306c31a30c3299c9dd2454f6b9702c7fbf3040e68630`. Private evidence remains under `models/boosted-upside-domain-20261112`; prefit plan commit is `abe1777` and the frozen private driver SHA begins `88ecb5d8`.

## Continuous-return study

The [separate fixed protocol](../../docs/superpowers/plans/2026-11-12-boosted-return-regression.md) tests a signed cost-net 20-session price-return regressor, with explicitly different score units and a cost-derived decision band. It preserves the original-five binary and broader reference studies unchanged. This changes both the learning objective and score policy; it is not an isolated loss-function effect.

The target uses actual next-open to open[t+21] price returns multiplied by the fixed baseline sale/purchase friction ratio. It excludes dividends from the training target because old events are unavailable; the account still includes all 176 actual study-period cash events. BUY requires predicted net return >0 plus the existing positive MA50 trend; SELL requires score below twice the baseline friction loss plus negative MA200/momentum20. Equality is HOLD. Scores are signed returns, never probabilities. Baseline target and signal boundaries remain fixed when execution costs rise.

|Regression account|CAGR|Maximum drawdown|Ending equity|Buys / sells|
|---|---:|---:|---:|---:|
|Full 2018–2026|13.1082%|23.9505%|297,818.47 USD|301 / 49|
|Full, 25+25 bps costs|12.1424%|24.4792%|276,028.91 USD|302 / 49|
|Fresh 2021 capital|16.2777%|13.7273%|241,964.07 USD|175 / 31|

This strategy has more transactions but lower returns than the 18.6259% binary candidate. It also underperforms SPY over the full interval. Both 20% flags fail; the small drawdown benefit does not rescue the result.

Full forecast RMSE is .08215 versus .07222 for the causal annual training-mean forecast and .07311 for zero net return. Later RMSE is .07706 versus .06660/.06773; MAE is also worse than both baselines. These are return units, not Brier scores or calibrated confidence. There are 11,035 full and 7,255 later observed evaluation outcomes; overlapping labels are dependent.

Eight synthetic checks and the independent audit pass. The reviewer rebuilds 16,345 raw-open target rows (16,240 observed) and verifies all nine continuous fitting populations, weights, means/distributions, score decisions, both immutable reference trees and all three account ledgers. **33,400 numeric ledger comparisons have maximum error 0.0**. No model fits or producer simulations are called by the auditor. The full forecast SHA256 is `efb730c1d80f132ac5b1df10124856d4dc0c969b6f1734e52deddee220cb5289`; private evidence is under `models/boosted-return-regression-20261112`, with prefit plan commit `6c6a97e` and driver SHA beginning `bde0a08b`.

## Minimum-leaf regularization

The [fixed third protocol](../../docs/superpowers/plans/2026-11-12-boosted-leaf-regularization.md) changes only classifier `min_samples_leaf` from 20 to 100. All original-five binary features, targets, weights, thresholds, trend rules and account constraints remain fixed. The complete installed effective parameter maps differ only in that one field; no shared constructor is monkeypatched or source guard relaxed.

|Leaf100 account|CAGR|Maximum drawdown|Ending equity|Buys / sells|
|---|---:|---:|---:|---:|
|Full 2018–2026|16.2370%|25.9927%|379,264.32 USD|236 / 28|
|Full, 25+25 bps costs|15.6092%|26.1546%|361,496.10 USD|240 / 28|
|Fresh 2021 capital|16.3628%|14.9462%|243,004.44 USD|153 / 20|

Full return and drawdown both deteriorate versus the original-five leaf20 candidate. Mean investment rises to 91.32%, so extra participation alone does not solve performance. Full Brier .29018 is also worse than leaf20's .28525 and both unchanged causal priors; later Brier .28178 is worse than .27793. Full/later AUC are .51141/.52984. The full 95th-percentile raw score remains .93907: increasing leaf size does not establish reliable confidence.

Eight synthetic checks pass. Independent review verifies the full effective parameter maps, exact equality of all nine fitting/prior dictionaries to the original-five reference apart from recipe, actual binary labels, all score tails and annual signal counts, 190 frozen reference files and all three complete ledgers. **32,539 numeric ledger comparisons have maximum error 0.0**, with no audit fits or producer simulations. The full forecast SHA256 is `099a839f48db93080c673db8535f42c012bc36b4a8b250bdee329cc8791d5a6c`; private evidence is under `models/boosted-leaf-regularization-20261112`, with prefit plan commit `c5063f5` and driver SHA beginning `df4907c7`.

Across the three diagnostics, all 24 synthetic checks and **98,283 independent ledger comparisons** pass. These checks establish the specified historical calculation, not future performance or calibrated forecast reliability. All unfavorable outcomes remain recorded; no fourth trial is added to this batch.

## Limits and operating status

Every period has already been inspected during earlier research. These chronological model-causal tests remain researcher-adaptive; no new name or protocol restores an untouched holdout. Five correlated survivors, retrospective training availability, current adjusted-price/index vintages and repeated trials limit generalization. Costs exclude taxes, cash interest and hypothetical final liquidation. Prospective timestamped paper observations remain necessary.

The six bound research/feature core modules and existing compiled panel remain unchanged. This study does not alter production RFv7, active artifacts, inference, API, native schedules, database or broker state. `research_qualified=False` and `promotion_authorized=False`; neither 20% future growth nor completed ML commissioning is implied.
