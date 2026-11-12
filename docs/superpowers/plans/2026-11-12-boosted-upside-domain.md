# Boosted upside training-domain diagnostic

**Goal:** Improve the user's fixed boosted reference (16.1739% CAGR on 2018–2026) toward more than 20% annual compound growth, effective model sales and lower drawdown, without changing production RFv7.

**Architecture:** One fixed offline training-domain ablation, using existing loaders, fitting, signals and accounting. Native inference, artifacts, configuration, API, database and schedules are outside this experiment. The user has already delegated research choices and authorized continued improvement; no new paid services or live trades are involved.

## Decision and scope

The existing broader raw market upside model has weak known-outcome ranking (full AUC .47860). The original-five risk-history experiment changed only downside training; its upside still used the broader corpus. This experiment changes ONLY the upside training population to the original AAPL/JNJ/JPM/MSFT/XOM histories. An original-five 24-feature boosted learner was already weak in an earlier 2015–2017 comparison; the present 28-feature market/domain contrast is not a new untouched hypothesis or a promise of improvement.

Continuous-return regression would require separately validated continuous targets and a new return-score policy; a new universe would require execution/dividend/identity evidence. Neither is mixed into this contrast. No grid, threshold search, ensemble, parameter optimization or post-result budget change is permitted within this study.

## Frozen method

- Start 2018-01-01, end 2026-11-11; 2026 is partial. Same cached stock/SPY sessions, 176 gross cash dividend events, initial 100,000 USD, no later deposits, shorts, leverage, cash interest, taxes or hypothetical final liquidation.
- Exactly nine annual causal fits for 2018 through 2026. Keep four calendar years of training, strict `target_date_20 < first_prediction`, the executable open[t+1] to open[t+21] binary target and equal total weight per eligible date. Fit ONLY the original five histories, never added panel rows.
- Same 24 swing plus four lagged Cboe market features; existing HGBT classifier: 200 iterations, .05 learning rate, 15 leaves, L2=1, early_stopping=False, random_state=42, OMP_NUM_THREADS=1. Existing six bound feature/core sources remain unchanged.
- Same raw .65 BUY/.35 SELL and trend200 confirmations, 10% purchase and 20% per-symbol execution limits. No risk head or invented confidence. Same 5 bps fee plus 5 bps adverse slippage per side; same actual dividend entitlement/receivable/payment timing and 8-decimal quantities.
- Reuse `swing._walk_forward_predict` with `training_frame=features` and `date_weighted=True`, `control_details` and `evaluate_account`. No public fitting/core changes or new dependency.
- Copy the unchanged reference forecast (SHA256 `9eb037ed4921c45f4b6ee99628d346b7be78321ad7b61ac03174620bc7be4fc7`), fit audits, three account exports and summary from `models/boosted-reference-long-20261112/run-v1`; never refit its models. Verify current inputs, panel lineage, macro features and sources against its frozen commitments before candidate fitting.

## Execution and checks

The private driver lives under `ml/models/boosted-upside-domain-20261112`; output must be new and refuse overwrite. Archive/hash this plan, driver, source files, reference evidence, input/calendar/panel/market commitments and environment BEFORE fitting. Synthetic checks must reject missing/duplicate forecast keys, nonfinite/out-of-range probabilities, wrong identity populations, leaked or incomplete fit audits and invalid positions.

Run the one fixed candidate on the complete interval as an explicitly ADAPTIVE historical diagnostic. This is not a continuation admitted by an earlier rejected gate. All previous admissions/rejections remain unchanged. There is no candidate selection based on 2018–2020, and the fully inspected later years are not called untouched validation.

Primary account: continuous 2018–2026. Fixed secondary accounts: same forecasts with 25+25 bps friction, and a fresh 100,000 USD start on 2021-01-01. No extra fits or new choice for secondary results. Export forecasts, nine fit audits, orders, executed score/reason traces, daily accounts/positions, SPY and annual returns. Check all expected keys and exact causal fit populations/weights/priors.

Report economic conditions separately: full CAGR >20%, full/later CAGR > matching SPY and frozen reference, full drawdown below the frozen reference, later drawdown below matching SPY, and genuine model sales. Report full/later AUC and Brier on observed outcomes, using both actual original-five fitting priors and the unchanged broader causal-prior comparator; do not substitute a favorable reference. These diagnostic flags cannot authorize promotion or establish prospective forecast reliability. `research_qualified=False` and `promotion_authorized=False` throughout.

An independent reviewer must verify fit populations, source/input commitments, probability-to-order mapping, all cash/dividend/position/trade ledgers and reported metrics without fitting or calling the producer simulator. Report failures and unfavorable outcomes. Never raise exposure or relabel arithmetic annual averages to claim the 20% CAGR objective.

## Tasks

- [ ] Prepare and review the private driver; pass synthetic checks and freeze plan/source/input/environment commitments.
- [ ] Execute exactly nine candidate fits and the three fixed accounts; preserve all reference bytes.
- [ ] Independently audit and publish the comparison and limits; update the existing draft PR without changing runtime.

All periods have already been inspected adaptively. Five correlated survivors, current adjusted-price/index vintages and retrospectively available training identities limit generalization. No future return guarantee or completed ML commissioning is implied.
