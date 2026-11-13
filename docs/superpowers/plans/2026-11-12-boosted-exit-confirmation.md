# Fixed five-session SELL confirmation on cached boosted20

**Goal:** Continue the active >20% annual compound-growth, better exits and lower-drawdown objective. The full ML goal is incomplete; this is one exit-policy diagnostic with ZERO new model fits, not a completed or promoted ML system.

## Fixed hypothesis and research history

The audited63-session experiment failed: full18.1581% CAGR /32.9671% DD versus the retained original-five20-session18.6259% /24.7658%. Its paired controls show immediate20-session exits trade1.0454 CAGR points for8.0223 points of lower maximum drawdown. Fresh20 without SELL earns20.4581%, but has no sales and cannot meet the user's full objective. Preserve all nine accounts and the failed63 experiment.

Test ONE causal confirmation rule: a SELL requires five consecutive OBSERVED sessions of the complete existing SELL predicate. Five is fixed as a trading-week confirmation before this policy's financial calculation, without3/5/10 tuning. The old RF5 `persistent5` experiment already earned15.0781% /13.7341% DD on2015–2017,56 buys/one sale, and was not retained. That older policy persisted only the raw score and changed BUY filtering. Do not call it a new untried idea or reuse its incompatible implementation.

## Exact policy

- Freeze the same cached20-session forecasts SHA`f53007c0ad1de79ba3fe306c31a30c3299c9dd2454f6b9702c7fbf3040e68630`, features, learner, target, model versions, confidence thresholds and all BUY decisions. No fitting, calibration, feature/core/panel mutation, new data or fake confidence.
- For EVERY forecast row, full original SELL eligibility is `probability_up<=.35 AND close_ma200<0 AND momentum_20<0`. HOLD and BUY rows are false. Within each symbol's complete observed-session stream, require `rolling(5,min_periods=5).sum()==5`. Retain the original SELL row on the fifth and later eligible days. This is not an entry-only edge, cooldown or fixed holding-period rule.
- Keep each original BUY key, score, allocation, reason and other metadata EXACTLY unchanged. Candidate SELL rows must be a subset of the original SELL rows. Existing actual-position and next-open rules determine whether an intent executes.
- Compute streaks on the FULL cached2018–2026 stream BEFORE slicing accounts. Continue across annual refits/year boundaries using the actual historical scores then available, never rescoring old dates with a future model. First four cached2018 sessions cannot confirm; no pre2018 scores are manufactured. Missing/duplicate/nonfinite or nonmatching expected calendar/context FAILS; never compress a gap into a streak.
- Current trend200 BUY remains `p>=.65 AND close_ma50>0`. Do not use existing `filtered_orders(policy='persistent5')`, which would also change entries. Unknown future20-session label tails still produce decisions; they are not filtered out of this policy.

## Exactly three new accounts

Same100,000 USD initial cash, no later deposits, original five assets, actual observed stock/SPY dates,176 gross dividends,8dp sizing,10% purchase and20% symbol limits at execution. Same delayed payment cash, costs, no shorts/leverage/taxes/interest/final liquidation. Full accounts2018-01-01..2026-11-11 and fresh2021-01-01..2026-11-11;2026 is partial.

Run unconditionally: `full-persistent5` at5bps fee+5bps slippage, `full-persistent5-higher-costs` at25+25, and `later-persistent5` at5+5. No extra forecasts or parameter choice. BUY quantities/trade counts/cash may legitimately differ after changing exits; fixed BUY intents do not imply fixed executed shares.

Compare each to BOTH immutable references: the corresponding original20 immediate-exit account under`boosted-upside-domain-20261112/run-v1` (passed audit SHA`80677e99b456ca8765c74770382d0181b7344c565450f4e39a8ea96c3a981eef`) and the matching new20 no-SELL control under`boosted-horizon63-20261112/run-v1` (passed audit SHA`d9972a7e3fa4f2870f9dff17736c8e17ff638faf8c940310690946d77a8da3d9`). Never refit or replay those reference accounts. Bind exact input/summary/forecast/account bytes and archived prefit plans; current workspace plans may contain clearly identified post-study notes.

Report all six paired differences: CAGR percentage points, ending wealth, drawdown reduction (reference DD minus candidate DD), fees and mean invested fraction. A return-enhancing exit claim requires positive matched no-SELL CAGR/wealth differences; risk reduction requires lower matched DD. Relative-to-immediate improvement can instead reduce exit drag and must be named accurately. Neither sale count nor positive realized profit alone proves better exits.

Full component conditions remain >20%, >same SPY and18.625926% reference, DD strictly below24.765767%, and genuine confirmed held-position sales. Report fresh2021>20%, paired outperformance, DD versus SPY and sales separately; no short-period or no-SELL result substitutes for the full objective. Score AUC/Brier are EXACTLY unchanged by this policy; never claim improved predictive calibration. All old gates/rejections remain unchanged and no new policy is promoted automatically.

## Freeze and independent verification

Private files under`ml/models/boosted-exit-confirmation-20261112`; exclusive output refuses overwrite. Before replays, commit/archive this plan, driver/helpers, imported unchanged sources, both independently passed reference trees, actual prices/calendar/market/dividend bindings and environment. Reuse `control_details`, existing simulation/account/positions and independent Fraction helpers. No public framework or shared constructor monkeypatch.

Synthetic checks: four versus five eligible sessions, HOLD/failed trend/score resetting a streak with correct inclusive probability and strict trend boundaries, cross-symbol isolation, year/refit continuity before fresh slicing, missing/duplicate coverage rejection, unchanged BUY frames, SELL-subset property, full unknown-label tail participation and future-row changes not altering earlier confirmations. Independently reconstruct the predicate/streaks without using the producer controller; verify both immutable reference audits, all three complete ledgers/positions/dividends/fees/caps/SPY/annual returns and six paired effect formulas with ZERO fits or producer simulations.

Five-session confirmation can delay crash exits or miss useful transient signals. Different holdings/cash can change future affordability, so returns need not lie between immediate and no-SELL controls. All inspected history is adaptive; five correlated survivors, current adjusted-price/index vintages and repeated research limit generalization. `classification='adaptive_diagnostic'`, `research_qualified=False`, `promotion_authorized=False`. Production RFv7/inference/artifacts/API/native tasks/database/broker state are unchanged. No future20% guarantee or completed commissioning.

- [x] Prepare/review the minimal cached-policy driver and pass synthetic checks; freeze before financial replay.
- [x] Execute the three fixed candidate accounts with zero new fits and unchanged references.
- [ ] Independently audit and publish every outcome, paired effect and remaining limitation.

## Post-study outcome

The pre-replay protocol committed as`a5174a1` remains archived unchanged. All three accounts completed with zero new fits. Eight synthetic checks and31,841 independent numeric ledger comparisons pass with zero ledger error; audit SHA`8b9c122eba2be8792c095d77affbda69c11871c6aabe3b99137df3977967c482`. Full CAGR increases to19.1415%, but drawdown worsens to29.6675%; fresh2021 earns19.6365%/15.6482%. Every BUY intent is identical and SELL intents are an189→32 subset. Both20% flags fail. The [complete outcome report](../../../ml/docs/boosted-exit-controls.md) records the risk/return tradeoff and unfavorable2022, without activating the policy.
