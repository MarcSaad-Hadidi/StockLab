# Monthly next-open DCA and25% trim comparison

The user requested confirmed monthly2x/3x/4x buys,next-session opening execution and a25% sale on entering STRONG_TRIM. They chose the same100000USD INITIAL capital as the frozen boosted/SPY comparison. This creates a separately named funded strategy: the original DCA Cercle v50 next_open instead starts at zero and addsB*m externally at eligible signals. Its retrospectiveC/N same-close comparator is not our SPY benchmark or a causal allocation rule.

**2018-01-01 through2026-11-11:** funded DCA without sales earns13.0502%CAGR;adding trim25 earns12.7722%. Frozen boosted earns16.1739% and SPY14.1669%. Both DCA accounts have17.2813% maximum drawdown. The25% sales do not improve this fixed comparison,and neither DCA arm reaches20%.

## Same capital,costs and dates

|Account|CAGR|Total gain|Max drawdown|Ending100000USD|Buys / sells|
|---|---:|---:|---:|---:|---:|
|Funded monthly DCA,buy-only|13.0502%|196.4671%|17.2813%|296467.07USD|46 /0|
|Funded monthly DCA,trim25 entry|12.7722%|190.0708%|17.2813%|290070.77USD|46 /2|
|Unchanged boosted reference|16.1739%|277.4444%|31.9147%|377444.36USD|88 /8|
|SPY adjusted total-return proxy|14.1669%|223.4434%|33.7196%|323443.40USD|One initial buy|

All accounts use the same frozen valuation sessions and start with100000USD,without additional external deposits.2026 ends November11 and is partial. DCA/boosted stock fills use split-adjusted opens,5bps proportional fee plus5bps adverse slippage per side and the same176 genuine gross dividend events. SPY is the existing provider-adjusted ETF total-return proxy,not the exact S&P500 Total Return Index. Cash interest,taxes,exchange conversion and hypothetical final liquidation are excluded.

The DCA arms use AAPL/JNJ/JPM/MSFT/XOM in five independent20000USD buckets,with no cross-bucket borrowing. Per-stock baseB=20000/12 is fixed before results. An eligible2x/3x/4x signal requestsB*m;the all-in purchase is limited to that bucket's spendable cash,then quantity is floored to8decimals. Internal sale/dividend proceeds can fund future requests but never count as new contributions. This differs explicitly from both v50's variable external funding and the boosted model's current-equity exposure limits. It is a common-capital comparison,not identical exposure constraints.

The lower DCA drawdown accompanies lower mean investment:65.43% buy-only and63.82% trim25,versus82.17% boosted. Ending cash is10504.67USD buy-only and23893.06USD trim25. Do not attribute the whole risk difference to the two sales.

## Exact signal and execution rules

Verified source:DCA Cercle published version50,commita7a850c1dc2358241758a6fdf89c1940b2685801,obtained from its Sites source repository. The old public GitHubd0c8f31 code is not substituted for the new scheduler. No changes were made to the DCA Site.

The [signal port](../src/stocklab_ml/dca_signals.py) preserves scalar WilderRSI14 initialization/recurrence,unroundedK=100*(close-lowestLow)/(highestHigh-lowestLow),and actual range windows9/12/19 for persisted profiles12/24/36. The comparison fixes Standard24/M:12 monthly bars for the range and15 confirmed bars before first RSI. RSI>=80 overrides every buy zone:K>=90 is STRONG_TRIM,otherwiseLIGHT_TRIM. Below RSI80,K<=3 buys4x,K<=10 buys3x,K<=25 buys2x;ADD1x is excluded. Current completed monthly OHLC is included in the range;intraperiod low touches and provisional candles do not place trades.

The [execution port](../src/stocklab_ml/dca_next_open.py) retains native-month start labels and month-end signal availability. The source filter is point.date>=startDate:December2017 does not create a January2018 buy. Each eligible monthly signal may buy again even when the zone persists. Execution uses the first EXPECTED declared session strictly after max(availableDate,periodEnd);missing/excluded opening prices fail instead of moving to a later observed price. The declared4241-session calendar reconciles the source fallback with primary evidence for three exceptional closures;see the [fixed protocol](../../docs/superpowers/plans/2026-11-12-dca-next-open-trim25.md).

The source next_open port separately preserves zero initial cash,B*m external allocations,zero fees/dividends,floating amount/open quantities,pending final budgets,multiple same-open buys and its retrospectiveC/N output for parity. None of those external contributions or future-dependentC/N allocations are imported into the funded performance table.

## New25% sale state and actual sales

The original v50 STRONG_TRIM suppresses buying and never sells. The NEW funded trim25 variant sells once on a confirmed valid non-STRONG→STRONG transition. The full valid warmup seeds prior state. Unknown-first STRONG does not fabricate entry;UNAVAILABLE/gaps hold the previous valid state. Persistent STRONG does not repeatedly sell;any confirmed valid exit,including LIGHT_TRIM,rearms.

At the validated next open,sell25% of then-held shares with8dp floor,release cost basis proportionally and keep net proceeds in the same bucket. Events have unique IDs;SELL executes before BUY at the same open. No position/zero rounded quantity is skipped explicitly;no terminal forced fill. Dividend entitlement precedes ex-date opening orders;receivables are equity immediately,cash is spendable only strictly after payment date.

|Executed STRONG_TRIM sale|Signal month / availability|Fill|Shares before|Shares sold|
|---|---|---|---:|---:|
|AAPL|2020-08 /2020-08-31|2020-09-01|85.99657817|21.49914454|
|JNJ|2026-02 /2026-02-28|2026-03-02|173.16001710|43.29000427|

All 46 BUY executions have identical dates, quantities, prices, fees and all-in costs in both arms. The account difference comes from the two sales and their subsequent cash and dividend effects.

Both DCA maximum drawdowns occur from2020-01-02 to2020-03-23,before either sale. Their equity curves therefore share that drawdown. The trim account finishes6396.30USD below the buy-only control,losing0.2780 percentage point of CAGR;this is a measured unfavorable result,not a cutoff or budget retuned after inspection.

Nine other entry events are explicitly skipped because the relevant bucket holds no shares;eleven purchase events are below the8dp quantity quantum. No sale or purchase is fabricated to inflate activity. The two executed sales realize6881.94USD of profit,already included in ending wealth;realized profit is not added again on top of cash and remaining holdings.

## Calendar returns

|Year|DCA buy-only|DCA trim25|Boosted|SPY|
|---|---:|---:|---:|---:|
|2018|0.57%|0.57%|-3.48%|-5.02%|
|2019|7.89%|7.89%|43.17%|31.21%|
|2020|0.92%|0.92%|27.83%|18.32%|
|2021|14.53%|13.63%|35.66%|28.72%|
|2022|10.41%|11.30%|-13.40%|-18.18%|
|2023|18.27%|17.28%|32.04%|26.17%|
|2024|19.70%|19.09%|8.47%|24.88%|
|2025|22.41%|22.47%|10.85%|17.71%|
|2026 throughNovember11|23.54%|22.45%|14.54%|12.29%|

Each calendar return includes the preceding year-end gap. These annual figures are not averaged to claim20% compound growth. The favorable later DCA years do not replace the full2018-2026 result.

## Verification and status

Source v50 tests:91pass. Public new signal/execution suites:78pass;root's new/adjacent DCA/dividend verification:109pass. Independent TypeScript/Python parity covers3045 signals across five203-month prefixes and all three profiles,including38715 bit-identical indicator values,3045 next-session dates,and301 bit-identical scheduled-fixture numbers with zero difference. The source fallback's three historical calendar discrepancies are retained and documented,not silently patched. New modules are committed at0174242;the six bound research/engineering modules and frozen boosted forecasts remain unchanged.

The independent funded auditor never imports or calls the new ledger producer. It reconstructs both accounts from canonical source signals,prices,declared sessions and dividend events using separate Fraction bookkeeping.353216 numeric ledger comparisons are bit-exact,with maximum ledger and metric error0.0. All94 executions,125 scheduled/skipped orders,2229 equity dates and11145 position/bucket rows per arm,annual returns,cost basis,realized/unrealized profit,internal sale cash and dividend timing reconcile. Source/protocol/code/environment commitments are verified against the prefit invocation;no model is refitted and no promotion is authorized.

The source-parity tests use identical cached inputs to both engines;they do not assert equality with an unavailable original personal-chart price prefix/provider. The funded arms are new capital/cost/dividend/rounding adaptations,not the exact original externally funded v50 account. Their25% trim state is a separately specified extension.

Private evidence remains under`models/dca-next-open-20261112`,including source/version provenance,canonical oracle inputs/outputs,the validated calendar,prefit protocol/code/environment snapshots and both funded ledger exports. Source data and credentials are not committed. Five selected survivors,current adjusted prices/index vintages,adaptive history and absence of pristine prospective testing limit conclusions. This is an offline historical comparison;no active model,inference,task,database or broker state is changed,and no future20% growth or completed ML commissioning is implied.
