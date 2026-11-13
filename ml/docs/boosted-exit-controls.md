# Boosted horizon, exit, market-context and cadence controls

The active goal remains more than20% annual compound growth, economically better sales and lower drawdown, followed by compatible ML commissioning. The preceding [three-trial batch](boosted-improvement.md) improved the retained boosted reference to18.6259% CAGR /24.7658% maximum drawdown. This continuation tests a different prediction horizon and measures the effect of permitting sales. All inspected history remains adaptive, not an untouched holdout.

## Fixed63-session horizon experiment

The [protocol](../../docs/superpowers/plans/2026-11-12-boosted-horizon63.md) changes only the original-five classifier's horizon from20 to63 sessions. It reuses the same28 features, HGBT parameters, four-year annual training windows, date weights, raw .65/.35 thresholds, trend confirmations and accounting. The target is observed open[t+64]/open[t+1]-1>.002; every training endpoint must precede the actual first prediction date. Unknown trailing64-session outcomes remain forecastable but unscored. A63-session forecast does not impose a63-session holding period.

Exactly nine new causal annual fits produce all2018–2026 forecasts. The cached20-session reference is never refitted and its three enabled account ledgers are copied unchanged. Six SELL-disabled controls remove only SELL intents; every original BUY key, score, allocation and metadata value remains identical. Subsequent actual purchases legitimately change with available cash, position caps and held shares. These controls measure the portfolio effect of exit permission conditional on the chosen forecasts, not individual sale alpha or passive five-stock buy-and-hold.

All accounts start with100,000 USD, without later deposits, on identical observed sessions through2026-11-11. Stock fills use the same split-adjusted opens,176 genuine gross dividends, delayed spendable payment cash,8dp shares and10% purchase/20% symbol limits at execution. Appreciation may increase a position above its purchase cap. Costs are5bps fee plus5bps adverse slippage per side unless stated otherwise. No leverage, shorts, taxes, cash interest or hypothetical final liquidation. SPY is the frozen adjusted ETF total-return proxy, not the exact S&P500 Total Return Index.2026 is partial.

### Full2018–2026, baseline costs

|Account|CAGR|Maximum drawdown|Ending equity|Buys / sells|
|---|---:|---:|---:|---:|
|Cached20, immediate model exits|18.6259%|24.7658%|454,173.23 USD|223 /29|
|Same20 BUY intents, SELL disabled|19.6714%|32.7881%|490,888.48 USD|157 /0|
|New63, immediate model exits|18.1581%|32.9671%|438,549.44 USD|242 /26|
|Same63 BUY intents, SELL disabled|19.6599%|32.8103%|490,471.81 USD|170 /0|
|SPY proxy|14.1669%|33.7196%|323,443.40 USD|One initial purchase|

The longer horizon fails to improve either full-period return or drawdown versus the retained20-session candidate. Disabling its sales also improves both full-period return and drawdown slightly. None of these full accounts exceeds20%; zero-sale controls cannot fulfill the requested exit objective.

### Full2018–2026, higher25+25bps costs

|Account|CAGR|Maximum drawdown|Ending equity|Buys / sells|
|---|---:|---:|---:|---:|
|Cached20, immediate exits|18.0422%|24.8852%|434,749.92 USD|221 /29|
|Same20 BUY intents, SELL disabled|19.6089%|32.7853%|488,623.53 USD|161 /0|
|New63, immediate exits|17.6888%|32.9141%|423,355.63 USD|245 /26|
|Same63 BUY intents, SELL disabled|19.5966%|32.8187%|488,177.28 USD|179 /0|

These are the SAME forecasts and signal rules at higher execution costs, not separately chosen models or thresholds. All four accounts remain below20%.

### Fresh100,000 USD from2021

|Account|CAGR|Maximum drawdown|Ending equity|Buys / sells|
|---|---:|---:|---:|---:|
|Cached20, immediate exits|17.5778%|14.6771%|258,252.42 USD|135 /19|
|Same20 BUY intents, SELL disabled|20.4581%|17.0295%|297,596.46 USD|95 /0|
|New63, immediate exits|17.0325%|14.2141%|251,313.60 USD|153 /17|
|Same63 BUY intents, SELL disabled|19.9712%|17.3089%|290,617.78 USD|100 /0|
|SPY proxy|14.2527%|24.4997%|218,296.20 USD|One initial purchase|

The20-session no-SELL control exceeds20% only on this shorter fresh-capital interval and has no sales. It is not the full2018–2026 result, an admitted candidate or evidence that the whole ML goal is achieved. The63-session no-SELL control earns19.9712%, which is below20%; rounding cannot turn it into a pass.

### Actual exit-permission effects

CAGR and ending-wealth differences below are enabled minus disabled. Positive drawdown reduction means the enabled account has lower maximum drawdown.

|Paired forecast/account|CAGR difference, percentage points|Ending wealth difference|Drawdown reduction, percentage points|
|---|---:|---:|---:|
|20, full baseline costs|-1.0454|-36,715.25 USD|+8.0223|
|20, full higher costs|-1.5667|-53,873.62 USD|+7.9001|
|20, fresh2021|-2.8803|-39,344.05 USD|+2.3523|
|63, full baseline costs|-1.5017|-51,922.37 USD|-0.1569|
|63, full higher costs|-1.9077|-64,821.65 USD|-0.0954|
|63, fresh2021|-2.9387|-39,304.18 USD|+3.0948|

Immediate20-session exits reduce risk but sacrifice return; removing all sales is not a complete improvement. Immediate63-session exits are worse in both full-period measures. More sells or positive realized profit alone cannot establish effective exits. Changes in later holdings, cash and affordability are part of these policy effects, not accounting errors.

### Forecast and calculation verification

On10,820 observed full63-session outcomes, AUC is .49944 and Brier .30823 versus the SAME63-event causal annual training-prior Brier .23260. Later7,040 outcomes give AUC .48477 and Brier .31207 versus own prior .23224. These scores fail their own simple references. Twenty-session priors and raw20/63 Brier levels describe different events and are not substituted or interpreted as cross-event calibration improvement.

Eight synthetic checks pass. An independent auditor performs zero model fits and zero producer simulations. It verifies all nine exact original-five64-open fitting populations and own63 priors,117 immutable reference files, cached20 forecast/decision parity, all nine new accounts,72 annual-return rows, positions, dividend entitlement/payment cash, caps, actual next opens, fees, exposure and all six effect JSON/CSV records. **95,282 numeric ledger comparisons have maximum error0.0.** The old20 enabled accounts are not replayed.

Private evidence is under`models/boosted-horizon63-20261112`, with prefit plan commit`97bb00b`, driver SHA beginning`5f454645` and full forecast SHA`ff217f3a5846c279f92ff99b4540c143becb9f1da25087b367000344a7eba4e4`. The archived prefit protocol is retained independently of later clearly marked outcome notes. The18.6259% research reference remains retained; the20% goal remains unmet.

## Five-session confirmation of the original20-session exits

The [separately fixed policy protocol](../../docs/superpowers/plans/2026-11-12-boosted-exit-confirmation.md) preserves the cached20 forecasts and every BUY, while requiring five consecutive observed sessions of the complete existing SELL eligibility: raw score<=.35 AND negative MA200 AND negative momentum20. HOLD/BUY days break the sequence. Confirmation is computed over the complete historical stream before fresh-account slicing; annual refits do not reset it. No prior2018 scores, future labels or rescores are manufactured. The existing unrelated RF5 `persistent5` shortcut is not used because it would change entries and the predicate.

|Five-session confirmation account|CAGR|Maximum drawdown|Ending equity|Buys / sells|
|---|---:|---:|---:|---:|
|Full2018–2026,5+5bps|19.1415%|29.6675%|471,962.53 USD|196 /9|
|Full2018–2026,25+25bps|18.8845%|29.6823%|463,018.41 USD|205 /9|
|Fresh2021,5+5bps|19.6365%|15.6482%|285,898.45 USD|116 /8|

Full return improves by .5155 percentage point versus immediate exits, while drawdown worsens by4.9017 points. Fresh return improves2.0587 points, with .9711 point worse drawdown. The rule reduces return drag but fails both20% flags and does not improve the joint risk/return objective. The18.6259%/24.7658% point remains the lower-drawdown reference; the19.1415%/29.6675% point is a disclosed tradeoff, not a hidden replacement.

|Confirmation compared with|CAGR difference, percentage points|Ending wealth difference|Drawdown reduction, percentage points|
|---|---:|---:|---:|
|Immediate20, full baseline|+0.5155|+17,789.30 USD|-4.9017|
|No-SELL20, full baseline|-0.5299|-18,925.95 USD|+3.1206|
|Immediate20, full higher costs|+0.8423|+28,268.49 USD|-4.7971|
|No-SELL20, full higher costs|-0.7244|-25,605.12 USD|+3.1030|
|Immediate20, fresh2021|+2.0587|+27,646.03 USD|-0.9711|
|No-SELL20, fresh2021|-0.8216|-11,698.01 USD|+1.3812|

The rule is not return-enhancing versus no-SELL: those matched CAGR/wealth differences remain negative. It preserves less drawdown protection than immediate exits. Mean full investment increases from88.00% to96.73%; reduced cash and fees explain part of the risk/return change. Full fees fall723.43 USD versus immediate exits but remain476.71 USD above no-SELL. BUY intents are identical while executed purchase quantities legitimately differ.

The full2022 return drops from the immediate policy's-.33% to-14.85%, despite improved aggregate growth. This unfavorable year is not substituted with the fresh2021 account's positive2022 return: those accounts have different inherited holdings and wealth paths. Full annual-return CSVs remain in the immutable evidence.

Eight synthetic checks pass. A separate auditor reconstructs confirmation with independent per-symbol counters and Boolean queues, verifying all11,140 evidence rows, including105 unknown-label forecast tails. All2,344 BUY intents are unchanged; SELL intents are an exact189→32 subset, with nine actual full-account sales. It verifies three complete ledgers, confirmation traces, all annual returns/exposure, both reference trees and six paired effects. **31,841 numeric ledger comparisons have maximum error0.0**, with ZERO new model fits, producer-simulator calls or reference-account replays. Full/later forecasts and AUC/Brier files remain byte-identical; a policy change cannot improve their calibration.

Private evidence is under`models/boosted-exit-confirmation-20261112`, with prefit plan commit`a5174a1` and driver SHA beginning`c873177d`. Together the horizon and confirmation studies pass16 synthetic checks and127,123 independent numeric ledger comparisons. These checks prove the specified calculations, not predictive generalization.

## Explicit SPY-context representation

The [fixed representation protocol](../../docs/superpowers/plans/2026-11-12-boosted-spy-context.md) appends four already implemented direct SPY statistics to the original28 inputs, keeping the same20-session target, model and immediate trading policy. Two momentum coordinates are algebraically redundant;200-day market trend and realized volatility add direct market statistics. All nine new annual models use exactly the original fitting populations, labels and priors. No future downside label enters the feature matrix or population filter.

|SPY32 account|CAGR|Maximum drawdown|Ending equity|Buys / sells|
|---|---:|---:|---:|---:|
|Full2018–2026, immediate exits|17.9741%|23.0973%|432,532.53 USD|197 /28|
|Full2018–2026, SELL disabled|19.6758%|32.7636%|491,049.16 USD|158 /0|
|Full, immediate exits,25+25bps|17.3481%|23.0957%|412,618.03 USD|191 /28|
|Full, SELL disabled,25+25bps|19.6150%|32.7622%|488,845.28 USD|160 /0|
|Fresh2021, immediate exits|13.8524%|14.1838%|213,853.42 USD|149 /21|
|Fresh2021, SELL disabled|18.1393%|15.9247%|265,563.00 USD|86 /0|

Full drawdown improves1.6684 percentage points versus original28 immediate exits, while CAGR falls .6519 point. The fresh account loses3.7254 CAGR points and underperforms SPY's14.2527%. This is another risk/return tradeoff, not a replacement that meets the joint objective. All full and fresh20% flags fail.

|SPY32 immediate account compared with|CAGR difference, percentage points|Ending wealth difference|Drawdown reduction, percentage points|
|---|---:|---:|---:|
|Same32 no-SELL, full baseline|-1.7017|-58,516.63 USD|+9.6663|
|Old28 immediate, full baseline|-0.6519|-21,640.71 USD|+1.6684|
|Old28 no-SELL, full baseline|-1.6973|-58,355.95 USD|+9.6907|
|Same32 no-SELL, full higher costs|-2.2669|-76,227.26 USD|+9.6665|
|Old28 immediate, full higher costs|-0.6941|-22,131.89 USD|+1.7895|
|Old28 no-SELL, full higher costs|-2.2608|-76,005.50 USD|+9.6896|
|Same32 no-SELL, fresh2021|-4.2869|-51,709.58 USD|+1.7409|
|Old28 immediate, fresh2021|-3.7254|-44,399.00 USD|+0.4934|
|Old28 no-SELL, fresh2021|-6.6057|-83,743.05 USD|+2.8457|

Within-model controls preserve every BUY and isolate exit permission at the portfolio-policy level. Cross-model comparisons also change entries and cannot isolate sales. Full Brier is .28983 versus its own .24584 annual training-prior reference and .25065 broader annual reference; later Brier is .28424 versus .24759/.25213. AUC is .51097/.51525. These scores worsen versus original28 and remain weaker than the simple same-event priors.

Eight synthetic checks pass. Independent review verifies all16,345 frame rows,457,660 unchanged original-feature cells,65,380 direct-SPY formula cells, all nine fitting populations and105 target-unknown forecast tails. It checks six complete ledgers,48 annual-return rows, nine effects and362 immutable reference files. **63,435 numeric ledger comparisons have maximum error0.0**, with zero auditor fits, producer simulations or reference replays. SPY rolling-formula numerical errors stay within the declared tolerance; original28 parity is exact.

Private evidence is under`models/boosted-spy-context-20261112`, with prefit plan commit`4fc4429`, driver SHA beginning`ab8599a0`, forecast SHA`1355a83d760a1144f4514953ebda64b92ba7883dbe8cc9125919644988e8c384` and independent-audit SHA`c839b38dc380e597564907ebff92440df34d7213919762a4f1c542e3c63ff06a`. Across these three completed studies,24 synthetic checks and190,558 independent numeric ledger comparisons pass. Eighteen new models and18 new accounts were calculated; none qualifies or activates a model.

## Monthly original28 cadence diagnostic

The [fixed cadence protocol](../../docs/superpowers/plans/2026-11-12-boosted-monthly-upside.md), committed before fitting as`1942c53`, changes only annual to monthly fitting of the original28-feature20-session learner. One existing-core call completes107 causal fits, each using its actual first observed monthly session, four-year window and strict known-date purge. All915 January scores are **bit-identical** to the annual reference, with zero maximum difference, all nine January fit dictionaries identical apart from recipe and exact BUY/SELL/HOLD parity before any replay. No scores are rounded or snapped; later holdings/fills legitimately differ.

|Monthly original28 account|CAGR|Maximum drawdown|Ending equity|Buys / sells|
|---|---:|---:|---:|---:|
|Full2018–2026, immediate exits|16.6715%|33.8450%|392,010.45 USD|214 /32|
|Full2018–2026, SELL disabled|19.6875%|32.8042%|491,476.01 USD|168 /0|
|Full, immediate exits,25+25bps|16.0346%|33.8473%|373,453.90 USD|208 /32|
|Full, SELL disabled,25+25bps|19.6246%|32.8013%|489,192.10 USD|175 /0|
|Fresh2021, immediate exits|16.3994%|15.1208%|243,452.46 USD|151 /24|
|Fresh2021, SELL disabled|20.3546%|16.5885%|296,101.68 USD|110 /0|

Monthly fitting loses1.9545 CAGR points and worsens full drawdown9.0793 points versus the retained annual28 immediate policy. Fresh return also falls1.1784 points while drawdown worsens .4437 point. The fresh zero-sale control exceeds20% on its shorter interval; neither enabled model nor any full account reaches20%. It cannot substitute for the long-period/genuine-sales objective.

|Monthly immediate account compared with|CAGR difference, percentage points|Ending wealth difference|Drawdown reduction, percentage points|
|---|---:|---:|---:|
|Same monthly no-SELL, full baseline|-3.0161|-99,465.57 USD|-1.0408|
|Old annual immediate, full baseline|-1.9545|-62,162.79 USD|-9.0793|
|Old annual no-SELL, full baseline|-2.9999|-98,878.03 USD|-1.0570|
|Same monthly no-SELL, full higher costs|-3.5900|-115,738.20 USD|-1.0460|
|Old annual immediate, full higher costs|-2.0076|-61,296.01 USD|-8.9621|
|Old annual no-SELL, full higher costs|-3.5743|-115,169.63 USD|-1.0620|
|Same monthly no-SELL, fresh2021|-3.9552|-52,649.22 USD|+1.4676|
|Old annual immediate, fresh2021|-1.1784|-14,799.96 USD|-0.4437|
|Old annual no-SELL, fresh2021|-4.0587|-54,144.01 USD|+1.9086|

Full Brier worsens to .28963 versus .28525 annual-model, .24578 own-monthly-prior and .25065 frozen-broader-annual-prior losses; AUC falls to .49277 from .51954. Later Brier is .27760 versus .27793 annual-model, .24796 own-monthly and .25213 broader-annual, with AUC .51996 versus .52879. The slight later loss change does not establish calibration or financial improvement.

All107 monthly diagnostic rows and nine annual forecast-quality rows are retained, including November's40 forecasts/zero known outcomes, null losses and actual .57798 monthly fitting prior. The broader annual reference remains separately keyed by year. Full account exposure is continuous, not reset monthly. The cadence change updates recency, moving-window endpoints and priors together; it is not an isolated recency effect.

Eight synthetic checks and the independent audit pass. The auditor reconstructs all107 exact causal fit populations/weights/priors, all915 January scores/actions,107 monthly and nine annual forecast diagnostics,48 financial annual-return rows, six ledgers and nine effects. It verifies362 reference files, both later failed-study proofs and all51 source paths in the actual union of the four prior source closures. **63,830 numeric ledger comparisons have maximum error0.0**, with zero model fits, producer simulations or reference-account replays.

Private evidence is under`models/boosted-monthly-upside-20261112`, with driver SHA`dcefa970fa55c17a90ccbf76e8900242bb0ff3588023190f1df4253251446b25`, forecast SHA`57c69af4490c3267e2440784540b7f26fa18b5ad1e24da31131ed646e23ef708` and independent-audit SHA`b8393bd58c059e64e75e0ff5094869101309cdfcb56bb99a263462cf4db7a28e`. Across these four completed studies, **32 synthetic checks and254,388 independent numeric ledger comparisons pass**. They add125 fitted models and24 account replays, with no qualification, promotion or runtime change. The annual18.6259%/24.7658% reference remains retained; the19.1415% persistence point remains a disclosed higher-risk tradeoff. The full20% objective is unmet.

## Entry-only quarter exits on the same annual28 forecasts

The [fixed quarter-exit protocol](../../docs/superpowers/plans/2026-11-12-boosted-quarter-exit.md) reuses the exact annual20 scores and every BUY intent. Its preregistration stays unchanged; completed evidence is reported here. The first observed row seeds each symbol's SELL eligibility; subsequent complete-predicate false-to-true entries schedule one25% sale at the next actual open. State runs continuously across years/refits and before the fresh2021 slice. A sale uses floor8(actual fill-time shares/4), leaving at least75%; unheld/dust entries are consumed without fees and terminal unexecuted orders are explicit. This is the model's SELL predicate, not the monthly DCA STRONG_TRIM rule.

The account preserves pooled cash, symbol execution order, original BUY caps/sizing, costs and dividend timing. The DCA bucket engine is not substituted. Partial basis/remainders are separate accounting sidecars; original nine trade/six equity columns remain unchanged. There are zero model fits, benchmark replays or reference-account replays.

|Quarter-exit account|CAGR|Maximum drawdown|Ending equity|Buys / sells|
|---|---:|---:|---:|---:|
|Full2018–2026,5+5bps|18.9329%|30.9147%|464,693.47 USD|209 /86|
|Full2018–2026,25+25bps|18.5646%|30.9592%|452,096.65 USD|208 /86|
|Fresh2021,5+5bps|19.3892%|14.0444%|282,454.17 USD|128 /67|

The fresh account improves both CAGR and drawdown versus immediate exits. The full account improves return only .3070 percentage point while worsening drawdown6.1490 points. It is also worse on both full-period measures than the previous19.1415%/29.6675% confirmation variant. Neither full nor fresh return exceeds20%; the lower-risk annual reference is retained.

|Quarter policy compared with|CAGR difference, percentage points|Ending wealth difference|Drawdown reduction, percentage points|
|---|---:|---:|---:|
|Immediate20, full baseline|+0.3070|+10,520.24 USD|-6.1490|
|No-SELL20, full baseline|-0.7384|-26,195.01 USD|+1.8733|
|Immediate20, full higher costs|+0.5224|+17,346.74 USD|-6.0739|
|No-SELL20, full higher costs|-1.0443|-36,526.88 USD|+1.8261|
|Immediate20, fresh2021|+1.8114|+24,201.75 USD|+0.6327|
|No-SELL20, fresh2021|-1.0689|-15,142.29 USD|+2.9850|

Mean full investment rises from88.00% to95.27%; fees fall536.74 USD versus immediate exits. More retained exposure helps explain the different risk profile, not improved forecast calibration. The full account reconciles100,000 initial capital +173,975.50 net realized P&L +155,256.48 unrealized P&L +35,461.49 gross dividends to464,693.47 equity. Fees are already included in cost basis/net proceeds and are not subtracted twice. Positive realized profit alone cannot prove sale alpha: matched no-SELL wealth remains higher. This tests episode entry and sale size jointly, not an isolated25% quantity effect.

Eight synthetic checks pass. Independent review reconstructs all11,140 entry rows/105 unknown-label tails, all2,344 unchanged BUY intents and189→86 SELL-intent subset, three partial-sale ledgers and every order/basis/position/dividend/cost/cap/SPY/annual sidecar. It verifies24 annual-return rows, six effects,362 reference files, all51 old source paths and later proof bindings. **148,781 numeric comparisons have maximum error0.0** with zero auditor fits, producer simulations or control replays. One checker column-order mismatch was corrected before ledger comparisons, preserving the original checker/failure evidence; no financial values, producer outputs or strategy were changed/replayed.

Private evidence: `models/boosted-quarter-exit-20261112`, pre-replay plan commit`10a3f61`, driver SHA beginning`cc8ae488`, replay helper SHA beginning`16782079`, independent-audit SHA`9f3dd020b00ecba61fe68c4c54051c7dfbacabc6517e315cf3773616e9672a23`. Across these five completed diagnostics,40 synthetic checks and403,169 independent numeric ledger comparisons pass;125 research fits and27 new account replays were calculated. The separate genuine snapshot reproduction remains one additional fit/no account replay. The >20%/lower-drawdown objective is unmet, and all failed/frontier points remain recorded.

## Original-five upside calibration continuation

The [fixed upside-isotonic study](boosted-upside-calibration.md) reuses the existing calibrated fitting branch with a separate latest-year holdout and strict base/calibration purges. Nine actual base fits/nine calibrations produce six same-score enabled/no-SELL accounts. Full enabled CAGR rises to19.2814%, but drawdown worsens to32.5747%; fresh2021 return falls to13.7676%, below SPY14.2527%. One actual sale reduces full wealth8,956.07 USD versus its same-score no-SELL control without lowering full maximum drawdown. Brier improves to .26917 but still trails every causal-prior comparator; full AUC is .48644. This changes base history and mapping jointly, not calibration alone.

Eleven synthetic checks and251,937 independent zero-error ledger comparisons pass. The auditor verifies all nine exact partitions/87,695 membership rows, four priors, forecast tails/annuals, six accounts,48 financial annual rows and nine effects, with no fits or producer/reference replays. One checker-only forecast CSV column-order correction is documented; original evidence is retained and producer outputs are unchanged. Across these six diagnostics:51 synthetic checks,655,106 independent numeric comparisons,134 base-model fits/nine isotonic calibrations/33 accounts. The18.6259%/24.7658% reference remains retained; the full20%/lower-risk objective is unmet.

## Operating limits

Five surviving evaluation stocks, dependent overlapping outcomes, current adjusted-price/index vintages and repeated inspection limit generalization. Neither exact accounting nor a new protocol restores untouched validation or guarantees future20% growth. The [genuine boosted28 snapshot/registry/catalog workflow](boosted-snapshot.md) reproduces all1085 frozen2026 scores, saves/loads the actual model through an isolated Archived registry and projects its metadata through the read-only application catalog. Its historical50 Brier remains worse than its fitting prior; prospective evidence is PENDING. Native28-feature/vintage inputs, expected-session execution/cost/dividend parity and prospective paper/restart checks still remain. RFv7 selection, schedules, database and broker state remain unchanged. All studies retain `research_qualified=False` and `promotion_authorized=False`.

The [source-bound input preparer](boosted-vintage.md) now reproduces the complete28-feature matrix and1085 historical2026 scores/policy rows with zero fits, through the existing real Archived artifact. Native acquisition/cycle integration and prospective execution/restart verification remain separate; current UTC preparation of historical sources is not evidence of past publication.

The [broader-data readiness assessment](broader-data-readiness.md) also prevents substituting an unverified111-name account: existing broader sources have no certified cash-event/zero-dividend coverage, historical membership/lifetime or terminal proceeds, and include missing sessions/zero-volume candles. The19.67% no-SELL result is a tested reference point, not a mathematical ceiling on five-stock strategies. More names alone do not establish better returns or executable fills.
