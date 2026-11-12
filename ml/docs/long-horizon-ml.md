# Longer-history ML evaluation

**Latest dedicated-downside ML result:** 17.1013% compound annual growth,22.5372% maximum drawdown and17 simulated sales over2018-2026. Its paired raw-RF control earns18.8761% with29.5981% drawdown and3 sales. The downside head reduces risk but loses its selection-period return advantage; its later2021-2026 result underperforms both the control and SPY. **The 20% CAGR objective remains unmet; no research candidate is promoted.** See the dedicated-head section below.

The earlier calibrated recipe's19.4043% aggregate result also remains unsuitable for commissioning: it permits no new purchases in2021-2025, and a fresh2021 reset earns only3.52% CAGR. Fixed protective exits and the later raw-entry ablation trade substantial return for lower drawdown. These are different research comparisons, not returns already earned by deployed RFv7.

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

The following exit and entry diagnostics supersede treating that aggregate result as evidence of deployment readiness. This experiment does not mark the ML performance objective complete.

## Protective exits and fresh-capital diagnosis (November 12)

The [risk-exit protocol](../../docs/superpowers/plans/2026-11-12-ml-risk-exits.md) freezes two additional restrictive recipes before their return calculations. Both retain original .65/.35 model thresholds, trend confirmation, fees and purchase limits. They add an independent risk-sale reason rather than inventing a model SELL confidence:

- `trailing15`: a confirmed close 15% below the held position's peak schedules full liquidation at the actual next observed open. The held-price signal compounds ex-dividend cash into returns, so dividend detachment alone cannot trigger a false loss. Initial peak is the entry open; additional buys retain it. Opening gaps can exceed the nominal threshold.
- `trailing15_spy200`: additionally block purchases and exit positions after SPY has three closes below its 200-session moving average; three above restore eligibility. Confirmations use pre-start history and affect the next open. Both recipes wait 20 observed opens after every executed risk sale before re-entry.

Selection uses 2018-2020, choosing the higher CAGR among protective variants with lower drawdown than the original same-period candidate and actual risk sales. This constrained comparison does **not** require an increase in CAGR. It is frozen before computing the chosen variant's later 2021-2026 returns. Previously inspected history remains adaptive research, not a pristine holdout. Unselected later-policy returns are not calculated.

|Selection 2018-2020|CAGR|Maximum drawdown|Buys / sells|
|---|---:|---:|---:|
|Original calibrated20/trend200|21.2314%|32.6814%|62 /0|
|Trailing15|6.4460%|25.2595%|131 /14|
|Trailing15 + SPY200, frozen selection|8.1474%|16.8810%|108 /14|

|Period and independent account|CAGR|Maximum drawdown|Buys / sells|
|---|---:|---:|---:|
|Full 2018-2026 original|19.4043%|32.6814%|75 /0|
|Full selected protection|6.4274%|16.8810%|144 /25|
|Full selected, 25+25bps costs|5.9092%|17.7534%|140 /25|
|Later reset 2021-2026 original|3.5202%|5.0041%|23 /0|
|Later reset selected protection|2.7479%|3.0048%|36 /6|
|Later reset selected, 25+25bps costs|2.5284%|3.0068%|37 /6|

SPY CAGR is 14.1669% for the full interval and 14.2527% for the later reset. The later account starts with fresh $100,000; it does not inherit earlier shares. Its drawdown cannot be compared directly to a continuous account holding pre-2021 positions. The overlay creates 25 real simulated sales (14 trailing /11 regime); 12 sales have positive realized price profit after costs, excluding dividend income. Nevertheless, it sacrifices too much return and is **not promoted**.

The original candidate's lack of sales is structural: none of its 11,140 frozen forecasts reaches `p_up<=.35`. Exact reconstruction of all annual models reproduces saved scores without difference. In 2020, every score is >=.65, even during the crash; the sigmoid can only map raw RF scores into approximately .679-.710 that year. More critically, the original candidate permits **no purchases at all in 2021-2025**: 2021-2024 never reach .65; the 13 JNJ hits in 2025 fail the unchanged trend filter. The fresh-capital account remains entirely cash until 2026. Its aggregate 19.4% result relies on the appreciation of positions acquired earlier and does not establish robust entry behavior for a new deployment.

Annual calibration reverses raw score ranking in 2019/2021/2023/2024. Over available historical labels, pooled raw/calibrated ROC AUC is .5169/.4698, and later-period AUC .5312/.4956. These raw diagnostics describe the **same older core estimators before their sigmoid**, not a new tradable strategy. Reserving the latest year exclusively for calibration also leaves the core estimator about 13 months behind current feature dates. Overlapping 20-session outcomes and shared market exposure are correlated; these descriptive metrics are not independent significance tests or sufficient calibration evidence. See the [scikit-learn calibration discussion](https://scikit-learn.org/stable/modules/calibration.html) for the distinction between fitted calibration and reliable future probabilities.

Private commitments, source snapshots, original hashed protocol, chosen-policy hash, predictions and complete equity/trade curves are preserved under `models/risk-exits-20261112/run-v1`. Full default baseline exports are byte-identical to the earlier dividend replay. A fresh reviewer independently reconciled every daily cash/holdings/dividend/equity balance for both selected accounts within $1.2e-10, including actual fills, fees and entry limits. No live operation changed.

Reproduce this fixed exit comparison from `ml`, with the prior price/dividend/forecast snapshots present and `PYTHONPATH=src`:

```powershell
.\.venv\Scripts\python.exe examples/risk_exit_replay.py --study-dir models/long-horizon-20261111/run-v4 --data-dir models/long-horizon-20261111/dataset --dividend-dir models/long-horizon-20261111/dividend-dataset --replay-dir models/long-horizon-20261111/dividend-run-v2 --out models/risk-exits-20261112/new-risk-study
```

## Single entry-signal ablation

Before its calculation, one additional hypothesis was recorded in the same protocol: reuse existing `rf_swing20` with the frozen `trend200` confirmation and `trailing15_spy200` protection. This removes the sigmoid **and** includes the latest eligible year in the base fit, so it is not a pure calibration-transform comparison. Hyperparameters, features, thresholds, risk limits, data and cash timing stay fixed. There is no new model family or parameter search.

The new runner first fits only 2018-2020. Admission requires higher CAGR than the protected incumbent's exact saved selection result, lower DD than the original unprotected selection result, and genuine protective sales. It passes at **9.7730% CAGR/15.1500% DD**, versus protected 8.1474%/16.8810%. Selection and its hash are saved before later fits or returns. Failure would have stopped the later calculation. This admission gate is an experimental comparison, not production approval or the 20% performance criterion.

|Raw RF20 + fixed protective exits|CAGR|SPY CAGR|Maximum drawdown|Buys / sells|
|---|---:|---:|---:|---:|
|Full 2018-2026|10.2292%|14.1669%|15.1500%|181 /40|
|Full, 25+25bps costs|9.4155%|14.1154%|15.7101%|186 /40|
|Later fresh-capital 2021-2026|9.6625%|14.2527%|9.3782%|114 /25|
|Later, 25+25bps costs|8.8704%|14.1748%|10.7206%|116 /25|

The full account's 40 sales comprise 39 protective exits and one model-approved sale. Eligible BUY observations return in every later year:204/289/128/48/25/135 for 2021-2026. These are signal observations, not promises to purchase: actual execution still checks cash, entry caps, regime and cooldown. The five-year cash lock is removed. The earlier pooled raw-AUC diagnostic used the old, older core fits; it must not be attributed to this more recent refitted recipe.

Compared with the protected calibrated candidate, this ablation improves full CAGR from 6.43% to 10.23% and DD from 16.88% to 15.15%. Compared with the original unprotected candidate it sacrifices return (19.40% to 10.23%) for lower DD (32.68% to 15.15%). It still underperforms SPY and fails 20%. Its later DD also exceeds the mostly-cash original reset's 5.00%. **Neither later nor full performance passes all objectives. No research candidate is promoted.** Raw directional scores remain unvalidated as probabilities, and model-approved recurring sales remain weak.

```powershell
.\.venv\Scripts\python.exe examples/entry_signal_replay.py --study-dir models/long-horizon-20261111/run-v4 --data-dir models/long-horizon-20261111/dataset --dividend-dir models/long-horizon-20261111/dividend-dataset --risk-dir models/risk-exits-20261112/new-risk-study --out models/risk-exits-20261112/new-entry-study
```

Generate `new-risk-study` with the preceding command first: the entry ablation intentionally requires the same current core-source hashes as its risk baseline. The later fitting-core extraction preserves numerical behavior but changes source identity, so current code correctly rejects old `run-v1` as that ablation's baseline. Use archived commit9691da4 sources for an exact original replay; do not remove the source guard. The dedicated-head runner below instead explicitly verifies and consumes archived old entry evidence.

The retained entry evidence is `models/risk-exits-20261112/entry-run-v1`: original source/code/protocol hashes, exact prior reports, selection commitment, nine purged annual fit audits, per-year score/eligibility counts, frozen predictions and costed cash/equity/trades. Later/full accounts start independently with $100,000 and no deposits. Across the previous 12 variants, these two risk policies and one entry ablation are **15 adaptively developed variants**, not independent evidence. This work closes the missing protective-sale simulation and reveals the entry defect; it does not finish the ML performance/deployment objective. Further model validation needs a stronger entry/exit signal and a broader defensible universe, followed by compatible artifacts, audited paper promotion and prospective operation.

## Dedicated downside ML head

The [separate frozen protocol](../../docs/superpowers/plans/2026-11-12-downside-ml.md) tests one combined hypothesis: a dedicated20-session price-risk target, direct trailing SPY context and two-head decisions. Control and challenger use identical previously frozen recent raw RF20 forecasts. Both omit the optional hard trailing/SPY regime/cooldown policies, so the paired comparison measures the learned head rather than another layer of hard exits. Confidence floors, entry limits, actual next opens, fees and real cash dividends remain unchanged.

The dedicated label asks whether `min(close[t+1],...,close[t+20])/open[t+1]-1 <= -.05`, known only at `date[t+20]`. Unknown future paths remain NaN, never negative labels. This excludes the entry overnight gap and measures split-adjusted **price risk**, not dividend-inclusive wealth loss: complete historical cash events before2018 are unavailable. Four direct SPY features are trailing20/63-session momentum,close/SMA200-1 and20-session return volatility. The regularized RF, four-year annual training and full known-date purge reuse existing fitting code without changing original upside recipes, predictions or audits.

Downside score>=.65 overrides other signals with a next-open SELL. BUY needs the original upside/trend approval plus downside<=.35; intermediate risk holds existing positions and blocks new purchases. No cooldown timer or hard stop is added. Raw scores are kept separate; they are not demonstrated calibrated probabilities or a joint probability of profitable trading. Executed sales are joined back to saved actual model reasons and scores.

The earlier recent entry head's pooled AUC is only.5078/Brier.2555. Among1,320 eligible BUY observations its average score is.7195, but its realized20-session endpoint hit rate is.5515. These are retrospective, correlated outcomes, not portfolio returns. A positive endpoint can still include a5% interim decline; this motivated testing an independent path-risk target rather than interpreting `1-p_up` as loss risk.

Selection2018-2020 favors the dual model at24.0452% CAGR/22.5372% DD/8 sales, versus control22.0407%/29.5981%/1 sale. It passes the predeclared incremental return/DD/sales gates and forecast gate (AUC.5505,Brier.2251 versus a causal annual training-prevalence baseline.2347). Both arms' admissions are persisted and hashed before later fitting or returns. The Brier reference uses only known training labels before each prediction year; selection labels extending beyond2020 are excluded.

|Independent account / arm|CAGR|Maximum drawdown|Buys / sells|Dedicated-head sales|
|---|---:|---:|---:|---:|
|Full2018-2026 control|18.8761%|29.5981%|115 /3|0|
|Full2018-2026 selected dual|17.1013%|22.5372%|132 /17|15|
|Full dual,25+25bps costs|16.7122%|22.5684%|133 /17|15|
|Later2021-2026 fresh control|19.4525%|17.0526%|59 /2|0|
|Later2021-2026 fresh dual|13.6672%|13.5635%|64 /8|7|
|Later dual,25+25bps costs|13.3867%|13.6299%|63 /8|7|

SPY CAGR remains14.1669% full/14.2527% later. The full dual account beats SPY with lower DD, but not20%; its later reset underperforms SPY. Full drawdown is lower than the paired control and original32.6814% candidate, but higher than the earlier fixed-protection ablation's15.1500% drawdown. This is a return/risk trade-off, not a uniform improvement across every strategy.

The downside head does not generalize: later AUC drops to.4827, and Brier.2299 is worse than causal-reference.2137. Full AUC is.5024/Brier.2299 versus reference.2198. Mean invested equity is78.10% full/76.30% later; the longest entirely cash-only stretches are147/148 observed sessions, so entry-veto participation remains a concern. The selected model is **rejected for commissioning**, and the control also fails20%. No arm is silently reselected after the test and no threshold is loosened to pass.

Unlike the earlier mostly-cash reset comparison, this protocol explicitly compares later drawdown against same-period SPY. It retains the full-period original32.6814% ceiling and20%/SPY/sales objectives on both intervals. This changed risk reference is documented before calculation; it does not change the failed return result.

```powershell
.\.venv\Scripts\python.exe examples/downside_research.py --study-dir models/long-horizon-20261111/run-v4 --data-dir models/long-horizon-20261111/dataset --dividend-dir models/long-horizon-20261111/dividend-dataset --entry-dir models/risk-exits-20261112/entry-run-v1 --out models/downside-20261112/new-study
```

`models/downside-20261112/run-v1` preserves exact input/protocol/source copies, cached-entry provenance, causal forecast references, nine new annual risk fits, decisions, executed-model reasons, participation and every equity/trade/benchmark curve. This adds a dual-model hypothesis and paired control to already adaptive historical research, not untouched prospective evidence. The simulator, live inference/artifact contracts, RFv7, native tasks and databases are unchanged.

Final relevant verification:458 tests pass, including113 research cases. Independent review verifies exact old public predictions/audits, label-window brute-force checks, all source/selection/protocol hashes, nine cached entry and nine downside fits with exact training rows/prevalence, causal forecast scores, decision priority and six complete daily cash/dividend/equity ledgers. All exported ledger values match at float precision. No Critical/Important finding; this verifies implementation, not return predictability.

## Broader universe feasibility

Two bounded fallback-key reference requests on2026-11-12 return16,462 current US-common-stock `(symbol,mic_code)` identities and28,131 including delisted identifiers:11,669 extra, with none of the active identities missing. Reference plan metadata advertises Basic for16,461/28,130 respectively; this is **not verified historical price coverage**. No stock prices were acquired or added to this experiment. The [official stocks catalog](https://twelvedata.com/docs/asset-catalogs/stocks-list) documents the catalog options and access metadata.

Every returned ISIN/CUSIP is the literal placeholder `request_access_via_add_ons`, not a usable identity. Among the11,669 extra rows, only3,856 have a nonempty FIGI matching the expected format;7,813 lack any such non-placeholder identifier. Format validation does not establish historical identity continuity. Neither snapshot contains historical eligibility dates, ticker-change lifetimes or terminal proceeds. These gaps cannot be filled by treating an earliest provider timestamp as an IPO date, interpolating prices or dropping failed symbols silently.

The DCA project's6,586-instrument catalog and ETF constituent priorities are dated October2026, so they support prospective scanning but cannot be backcast as2010 membership. The larger current/delisted reference snapshot is a concrete starting point for broader data work, not certification of a survivorship-free historical universe. Original raw responses, acquisition parameters, source hashes and identifier audit are preserved privately in `models/downside-20261112/catalog-probe`; no paid resource or additional credential was used.
