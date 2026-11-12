# Broader training, fixed boosted learner experiment

**Objective:** Improve learned entry/exit information toward >=20% CAGR, SPY outperformance, genuine sales and lower DD on the same full/later paper accounts. User authorizes continued ML work; no live promotion, API acquisition or paid resources.

**Evidence before this hypothesis:** The completed annual broader RF experiment has231667 training-only rows/111 current-survivor identities. Selection2018-2020 control22.5988%CAGR/26.8792%DD haszero sales;dual20.6864%/26.9569% makes two late downside sales. Both admissions fail. Upside AUC.4486/Brier.25034 is worse than its causal reference.24859; no eligible buys remain in2019/2020 and no upside sales occur. Restoring vetoed buys with the same August2020 exits cannot improve the earlier March maximum drawdown, so that policy-only hypothesis is not run.

## Frozen single change

Replace both regularized RF learners with the existing fixed HistGradientBoostingClassifier: max_iter200,learning_rate.05,max_leaf_nodes15,l2_regularization1,early_stoppingFalse,random_state42. This uses the existing fitter/installed dependency, not new model tuning. Earlier boosted recipes on five-stock training failed; broader corpus/date-balanced learning is a new interaction hypothesis, not an untouched experiment or evidence it will succeed. No parameter/cadence/threshold grid.

Run the actual boosted study with process-local OMP_NUM_THREADS=1, recording that value in its input commitment to bound CPU use and preserve the model runtime choice. This is not a new CLI/config option; omitted/default RF metadata remains unchanged.

Keep the existing canonical panel/source hashes, original-five feature contexts and prediction/execution universe, four-year annual refits, head-specific full-known-date purges, per-date weights,24 upside/28 downside features,20-session targets,.65/.35 thresholds, trend/entry-veto/SELL priority,20% purchase exposure/10% allocation/ten positions, costs and176 actual dividends. Both heads refit freshly; the old RF scores are contextual only. Do not edit the six feature-source modules or regenerate the panel. The estimator parameters already live in the shared fitter source.

## Minimal implementation and boundaries

- Extend SAME `ml/examples/downside_research.py` with opt-in `--boosted`, valid only with `--training-panel` and incompatible with monthly mode. Default annual/monthly/broader RF behavior stays identical.
- Upside uses existing `boosted_swing20`. Downside reuses the same private fitting core with kind`boosted`, existing downside label/known date/features/score column and a distinct`boosted_price_downside20` audit recipe. No production recipe/artifact/config/catalog contract changes and no new generic model selector.
- New study protocol`broader-boosted-model-20261112-v1`; copy/hash this plan and exact runner/fitter/feature/model-parameter archives/environment before any fits. Use a fresh output directory. Preserve complete source/panel commitments and all existing validation.
- Refit both heads even though the old risk forecasts are available: this experiment changes the learner for both heads, so neither old head can substitute. No later fits before frozen admissions.
- Tests prove CLI rejection before input reads without a panel/monthly conflict, both learner routes/date weighting/purges, unchanged RF default, strict admissions and reject/control-only/dual later paths. No dependencies. No extra full local suite unless focused failures warrant it; GitHub runs the full suite.

## Admission and reporting

Selection2018-2020 ONLY first. Each arm needs>=20%CAGR,SPY outperformance,DD<original32.681374% and actual ML sales. Dual additionally requires CAGR>=its boosted control,DD<its control,genuine dedicated sales,AUC>.5/Brierbettercausalweightedreference. Keep exact existing gates, including the independent arm logic. Record both heads' matched-label diagnostics and participation; forecast quality never overrides failed portfolio admission.

Persist/hash admission BEFORE any2021-2026 fits/returns. Both rejected ->no later/full work. Control-only ->later upside only. Dual ->paired later/full accounts, selected25+25bps cost sensitivity. Overall qualification still needs>=20%/SPY/sales both full/later,fullDD<original,laterDD<SPY. No post-test arm switch or relaxation. No deployable artifact/runtime/promotion/restart claim from research alone.

Independent review verifies source/panel/code/environment, exact weighted training sets/priors, saved forecasts/decisions, accounting/model-sales and no prohibited later artifacts. Report all outcomes and adaptive/current-survivor/identity limitations. Stop this fixed experiment if rejected, preserve evidence, and keep the overall ML objective active.

- [x] Implement/test/review opt-in routing while preserving defaults and feature hashes.
- [x] Freeze source/protocol/input commitments, run single selection, obey later gating.
- [x] Independently audit results/ledgers and record the rejected outcome; publish with the next draftPR290 update.

Outcome: both rejected on2018-2020. Control20.0130%CAGR/33.2535%DD/one sale exceedsDD ceiling;dual8.1156%/22.3961%/12sales fails return/SPY/paired/Brier gates. No later/full fits or returns. Independent six-fit/forecast/account audit makes7953 numeric comparisons with zero difference. The broader ML objective remains incomplete; all evidence is retained under models/broader-boosted-20261112/run-v1.
