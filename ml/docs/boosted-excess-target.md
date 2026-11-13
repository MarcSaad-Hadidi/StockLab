# Original-five boosted relative-event diagnostic

Changing the boosted learner's target to beating SPY by more than20bps over20 sessions does not improve the requested portfolio. The full enabled account earns8.0571% CAGR, below SPY14.1669%, despite252 buys/51 sales. Its21.8051% maximum drawdown is lower than the retained raw model's24.7658%, but fresh2021 drawdown worsens. The18.6259%/24.7658% reference stays retained; the >20%/sales/lower-risk objective remains unmet.

## Fixed contrast

The [preregistered protocol](../../docs/superpowers/plans/2026-11-12-boosted-excess-target.md), committed before fits at`695a735`, changes ONLY the target/labels/priors and resulting models. All28 feature cells, original-five fitting identities/dates, annual four-year purges, date weights, HGBT parameters, raw .65/.35 thresholds and trend200 policy are unchanged. This is not a wholly new hypothesis: a24-feature excess target already failed on2015–2017; the present exact28-feature/original-five/full-period contrast is adaptive historical research.

The event is `(stock_open[t+21]/stock_open[t+1]-1) - (SPY_open[t+21]/SPY_open[t+1]-1) > .002`. Its true known date is observed t+21. A low score means a low estimated probability of exceeding SPY by MORE THAN20bps; class0 also includes small positive excess returns. A SELL therefore forecasts neither necessarily negative excess nor a negative absolute price return.

Primary forecasts/execution evidence use `probability_outperform_spy` and `excess_model`, never the absolute-up label. One temporary, bit-preserving adapter supplies the old policy helper's `probability_up` parameter and restores the relative-event names before output or account evaluation. The [native retained snapshot/diagnostic](boosted-diagnostic.md) still describes the separate absolute target and cannot load this study as that model.

Actual observed execution is nine HGBT fits, zero calibrations and six stock-account simulations. Existing SPY/reference evidence is copied, not replayed. All start with100,000 USD/no deposits, on observed2018–2026 sessions throughNovember11(2026 partial), with actual next opens,176 gross dividends,8-decimal quantities, pooled cash,10% purchase/20% per-symbol purchase caps and unchanged fee/slippage/basis/receivable timing. No leverage, shorts, taxes, interest or invented final liquidation.

## All six accounts

|Account|CAGR|Maximum drawdown|Ending equity|Buys / sells|
|---|---:|---:|---:|---:|
|Full2018–2026,5+5bps|8.0571%|21.8051%|198,684.95 USD|252 /51|
|Same scores, SELL disabled|19.2808%|33.7395%|476,874.98 USD|114 /0|
|Full,25+25bps|7.0595%|23.2299%|183,010.88 USD|258 /51|
|Same scores, SELL disabled,25+25bps|19.2196%|33.7428%|474,710.23 USD|121 /0|
|Fresh2021,5+5bps|8.1313%|21.7225%|158,097.35 USD|154 /32|
|Same scores, SELL disabled, fresh2021|18.9687%|17.2823%|276,674.68 USD|72 /0|

Enabled full/fresh accounts lose to both SPY and the retained raw model. Fresh SPY earns14.2527%; its24.4997% drawdown is higher than the candidate, but the retained raw fresh drawdown14.6771% is lower. Full/fresh strict20% flags fail, as do all higher-cost return conditions. More executed purchases/sales do not establish better performance.

Relative to its same-score no-SELL control, full exit permission loses11.2237 CAGR points while reducing drawdown11.9344 points. Fresh exit permission loses10.8374 CAGR points and WORSENS drawdown4.4403 points. Identical BUY intents do not imply identical fills, because sales change later cash/holdings. These comparisons measure permission at portfolio-policy level, not individual-sale alpha. Cross-target comparisons additionally change entries and forecasts.

Mean investment is63.48% full enabled/94.05% disabled and66.13% fresh enabled/96.53% disabled. Full enabled fees are1,325.70 USD versus66.24 USD disabled. Reduced exposure and turnover matter to the outcome; activity alone is not a successful exit policy. Neither no-SELL control reaches20% on its respective full/fresh interval or substitutes for the requested genuine-sales result.

## Own-event forecasting and audit

All11,140 forecasts and105 unknown-label tails remain. Known quality uses11,035 full outcomes and7,255 later outcomes, compared only with each fit's causal EXCESS prevalence. Old absolute-event Brier/priors are not valid same-event quality comparators.

|EXCESS diagnostic|Full|Later|
|---|---:|---:|
|Brier|.29454|.28063|
|Own-event causal-prior Brier|.25030|.25010|
|AUC|.50838|.52996|

Neither Brier beats its proper prior; AUC alone cannot qualify the forecasts or portfolio. Nine populations/44,320 membership rows and priors are independently verified, including exact equality of old absolute/new excess fit keys. The target uses split-adjusted stock PRICE while SPY is all-adjusted; it excludes cash stock dividends from the event and is not an exact relative total/net-return target. Actual account dividends remain fully included.

Eleven synthetic self-checks pass(2 miniature base fits/0 calibrations/1 miniature account, separate from historical execution). The independent study audit passes at its FIRST execution: **267,977 numeric ledger/sidecar comparisons**, maximum absolute difference **5.820766091346741e-11**, within the unchanged previously frozen comparator tolerance. This is not claimed bit-exact financial equality. It verifies six accounts,48 financial annual rows, nine effects, exact labels/known dates/prior/quality/feature populations,53 old sources,84 snapshot bindings and117+245 reference files/proofs. There are zero auditor fits, producer simulations or reference replays; no checker repair or financial rerun occurs.

Private evidence: `models/boosted-excess-target-20261112`; driver SHA`bec9b132d697555043f908b4618ca1cb1ac9a6e3b5e592c251d5ef7e72e8b678`; helper SHA`8e1fb72b80779598d2ac031e630f1225e661ab25ed7bf4752bc1514d05e06e06`; independent-audit SHA`e54df7297d1c6e8af129228dead7d7e5e0e9c10a1289f3f65844cf3497a473f7`. Predeclared recipe/budget, observed9/0/6 calls, invocation, source/environment/input/protection digests and all unfavorable outcomes remain archived. Original RFv7 registry/two inspected configs are byte-identical.

Across the seven continuation diagnostics—[63-session horizon, exit confirmation, SPY context, monthly fitting and quarter exits](boosted-exit-controls.md), [upside isotonic](boosted-upside-calibration.md), and this excess-event study—there are62 synthetic checks and923,083 independent numeric comparisons, with the nonzero bounded tolerance disclosed above;143 historical base-model fits/nine isotonic calibrations/39 new accounts were calculated, excluding miniature synthetic fits/accounts. The separate genuine snapshot reproduction remains one additional fit/no account.

All periods are already inspected adaptively, on five selected surviving stocks and current adjusted/index vintages with overlapping targets. `research_qualified=False` and `promotion_authorized=False` remain. No worker, database, schedule, broker or DCA Site changes, no future20% guarantee and no completed native commissioning follow from this study.
