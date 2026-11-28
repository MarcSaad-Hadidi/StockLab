# Continuous-return boosted diagnostic

**Goal:** Continue the user-authorized search for >20% long CAGR, SPY outperformance and lower drawdown using genuine executable model decisions; production RFv7 remains unchanged.

**Decision:** One fixed HGBT continuous-return model on the same original-five histories and 28 features. The preceding original-five binary upside-domain diagnostic improved historical CAGR to 18.6259% with 24.7658% drawdown, but Brier .28525 trails both causal priors. Those are preliminary producer results pending independent audit at plan creation. Preserve that entire study and the original broader 16.1739% reference. Stronger classifier regularization would still discard outcome magnitude; broader regression would require a new continuous-target panel. Neither is mixed into this experiment.

## Frozen model, target and score units

Use the existing validated `swing_features` frame, including `forward_return_20` and `target_date_20`: enter observed open[t+1], exit observed open[t+21]. The target is **price return net of a hypothetical baseline round trip**, not dividend-inclusive total return. Pre-2018 stock dividends are not supplied and are not invented. The account still credits the same 176 actual 2018+ dividend events.

For fee f=.0005 and slippage s=.0005, fix B=(1+f)(1+s), S=(1-f)(1-s), k=S/B. Target y=k*(1+forward_return_20)-1. No clipping, binning, target rescaling, calibration or future-derived cutoff. Score name `expected_net_price_return_20` denotes a signed expected return; it is NEVER a probability or substituted into the 65% classifier confidence field.

Use installed `HistGradientBoostingRegressor(loss='squared_error', max_iter=200, learning_rate=.05, max_leaf_nodes=15, l2_regularization=1., min_samples_leaf=20, max_bins=255, early_stopping=False, random_state=42)` with OMP_NUM_THREADS=1. Exactly nine annual 2018–2026 fits; four previous calendar years, strict `target_date_20 < first_prediction`, only original AAPL/JNJ/JPM/MSFT/XOM rows and the same equal-total-weight-per-date convention. No public fitting/core/panel changes or new dependency.

## Fixed executable policy

Let c=B/S-1 be the baseline gross round-trip cost band and implied gross forecast g=(1+score)/k-1. BUY when g>c (equivalently score>0) AND close_ma50>0. SELL when g<−c (equivalently score<2*(k−1)) AND close_ma200<0 AND momentum_20<0. Otherwise HOLD. Implement the equivalent net-score boundaries directly so equality is consistently HOLD. The economic band is fixed from baseline costs before results, not optimized. This target/return-policy combination is a distinct strategy; it cannot isolate a loss-only causal effect.

Keep existing shared-cash next-open simulation: 100,000 USD initial capital, no external deposits, shorts or leverage, 10% purchase/20% symbol limits at execution, 8dp sizing, baseline 5+5 bps costs, dividend entitlement and delayed spendable payment cash. No forced transactions. Same observed sessions and adjusted-price/index vintages through 2026-11-11, with 2026 partial. No cash interest, taxes or hypothetical final liquidation.

## Comparison and verification

One fixed candidate, all nine fits and three accounts unconditionally: full 2018-01-01 through 2026-11-11; same forecasts with 25+25 bps costs; fresh 100,000 USD start on 2021-01-01. Higher costs change account execution only, not the frozen model, target or signal boundaries. Copy the entire frozen original-five binary and broader reference evidence unchanged, never refit or retune them. No grid, threshold search or fallback variant within this study.

Private driver/self-checks live under `ml/models/boosted-return-regression-20261112`. Reuse existing validated source/data/market/account helpers. Refuse output overwrite. Before fitting, require the original-five binary independent audit to pass and bind its exact inputs, summary and forecasts. Archive/hash that proof, the plan, driver/helpers, current imported sources, reference forecasts/accounts, input/calendar/market commitments and environment. Verify unchanged source hashes and reference forecast `f53007c0ad1de79ba3fe306c31a30c3299c9dd2454f6b9702c7fbf3040e68630` plus broader `9eb037ed4921c45f4b6ee99628d346b7be78321ad7b61ac03174620bc7be4fc7`.

Synthetic checks: flat-price target equals friction loss; positive/negative/constant continuous targets remain valid; missing target/known-date pairs fail while genuinely unknown trailing pairs stay censored; strict 21-session known-date purge; future-label changes cannot alter a fit set; targets cannot be features; exact BUY/SELL boundaries and trend conditions; duplicate/missing forecast keys, empty fits and nonfinite forecasts fail. Record exact nine causal fit populations, weights, target distribution, mean and last known date. Independently audit model-to-order traces, complete cash/dividend/position/trade ledgers and reported metrics without fitting or calling the producer simulator.

Report full/later observed-outcome MAE/RMSE against each annual causal training-mean forecast and the zero-net-return baseline, with outcome counts. Do not report Brier or calibrated confidence for regression. Report CAGR, DD, exposure, turnover/fees, ending wealth, annual returns and actual sales versus SPY and BOTH frozen references. Full target flag requires >20% CAGR, SPY/new binary reference outperformance, DD lower than that binary reference and genuine sales; later >20%, paired outperformance, DD versus SPY and sales are separate transparent flags. No single aggregate flag can hide a later miss.

Everything remains `classification='adaptive_diagnostic'`, `research_qualified=False`, `promotion_authorized=False`. All years have already been inspected; the old binary excess classifier was also tested previously. Continuous absolute net-price regression changes target magnitude/loss/score-policy units, not the historical data's independence. Five correlated survivors, squared-loss outliers, overlapping outcomes, current adjusted-price/index vintages, annual staleness and missing old dividend targets remain limitations. No future 20% guarantee or completed native commissioning is implied.

- [x] Prepare/review the private driver and pass synthetic checks; freeze commitments before fitting.
- [x] Execute exactly nine fits and the three fixed accounts without reference changes.
- [x] Independently audit and publish every result, favorable or unfavorable, in the existing draft PR.

Post-study outcome: full13.108232% CAGR /23.950460% DD,301 buys/49 sales; higher costs12.142410%; fresh2021 16.277670% /13.727303% DD. Return RMSE/MAE trail both causal-mean and zero baselines. Independent review verifies actual open-based targets, all nine fits and33,400 numeric ledger comparisons with max error0.0; eight synthetic checks pass. Both >20% flags fail and the original-five binary reference is retained. No qualification or promotion. Prefit bytes remain archived and committed at6c6a97e; this outcome note changes no frozen rule.
