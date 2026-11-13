# DCA and S&P 500 research implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Test the user's target of at least 20% annual compound growth and outperformance of an S&P 500 proxy, using their DCA methods without changing the live model on unproven evidence.

**Architecture:** A separate offline research module and example consume validated local daily/monthly exports. They produce costed, chronological paper portfolios and a reproducible report; existing model artifacts, promotion schemas, schedules and personal portfolios remain compatible.

**Tech Stack:** Existing Python, pandas, scikit-learn, pytest; no new dependencies.

**Spec:** The protocol and scope below constitute the specification for this bounded experiment.

## Global constraints / protocol fixed before fitting

- Target: CAGR >=20%, strict outperformance of SPY after assumed costs, at least 365.2425 calendar days. CAGR means compound growth, not arithmetic averaging or deposits counted as gains.
- Fixed universe: AAPL, JNJ, JPM, MSFT, XOM; actual cached 2021–2026-11-11 data. This small survivor universe limits generalization.
- Initial capital $100,000; no deposits, withdrawals, shorts, leverage, interest or final liquidation. Stocks exclude dividends; SPY uses provider `adjust=all` as a dividend-adjusted total-return proxy, with that asymmetry explicitly disclosed.
- Decisions after close, fills strictly at the next observed same-symbol open. Costs: 5bps proportional fee +5bps adverse slippage per side, deducted during simulation. These are assumptions, not measured broker costs.
- Preserve .65 directional-score threshold, 20% symbol exposure, 10% order allocation, 10 positions. DCA rules have no probabilistic confidence; report them as rule baselines, never fabricate ML scores.
- DCA: original range_rsi_v1, monthly Wilder RSI14, ranges9/12/19; only completed native monthly candles available strictly before the decision day. Profile24 (=12 months) drives buy-only monthly allocation; trim keeps cash. Base monthly budget per stock = initial capital /12 /5, no later deposits.
- Research features: existing daily features normalized by price/volume, plus confirmed monthly range positions and RSI. Original seven-feature RF is the baseline.
- Fixed candidates: RF with existing hyperparameters; normalized+DCA RF; normalized+DCA Logistic (existing hyperparameters); normalized+DCA HistGradientBoosting (200 iterations, learning rate .05, 15 leaves, L2=1, seed42, no early stopping). No hyperparameter search.
- Fit annually on up to four prior calendar years. Purge rows whose next-session target is on/after the first prediction day. Common eligible dates for all models.
- Selection period2024: choose enriched candidate by net CAGR, only if drawdown <= baseline +.002. If none qualify retain the baseline. Freeze selection before2025–2026-11-11 evaluation; do not inspect other candidates' final-period performance.
- Also evaluate original buy-only DCA and regular monthly DCA. Model stays active unless a separately compatible audited promotion passes. Existing historical inspections make this exploratory, not pristine prospective evidence.
- Scope: research module, CLI example, focused tests, protocol/results docs. No dependency, API, UI, artifact-contract, live-config or secret changes.

## Review focus

- A signal/label on a boundary cannot use a future or incomplete monthly candle.
- Costs affect buying power and position sizes; identical portfolio dates/capital are required for comparison.
- A two-month gain cannot pass an annual target; invalid/duplicate dates and nonfinite values must fail closed.
- Tests must hand-check fills, fee cash deductions and exposure after costs, including selling an empty portfolio.
- Selection uses2024 only; the test run must not fit on future targets or silently substitute a different candidate.

## Task 1: Tested offline comparison and bounded experiment

**Files:** Create `ml/src/stocklab_ml/research.py`, `ml/examples/dca_research.py`, `ml/tests/test_dca_research.py`, `ml/docs/dca-research.md`; link the report from `ml/README.md`.

**Interfaces:** Research-only helpers accept validated DataFrames; the example reads explicit local files and writes a new output directory. No production exports/callers/fixtures/configs change.

- [x] Write failing tests for CAGR/benchmark alignment, monthly causal availability, purged fit boundaries, and hand-calculated costed orders.
- [x] Run focused tests and observe the expected missing-function failures.
- [x] Implement minimal helpers and runner; run focused tests and neighboring feature/backtest tests.
- [x] Verify Python monthly signals against the pinned original TypeScript DCA engine on all acquired monthly bars.
- [x] Run the frozen2024 selection, save selection evidence, then run the selected recipe and baselines on2025–2026-11-11. Retain hashes, curves, orders and model fit boundaries privately.
- [x] Publish aggregate results and limits; no promotion when target/evidence/compatibility fails.
- [x] Obtain a fresh review, address findings, commit and update the existing draft PR.

## Execution ledger

-19 focused research tests pass; the final research/adjacent run passes220 cases. An earlier run hit an external Windows temp-folder ACL for two existing tests, resolved with a new workspace-owned `--basetemp`.
- Original DCA tests:38 pass.820 confirmed original TypeScript signals match (maximum absolute RSI/range error2.1316e-14; profile24 multipliers exact).
- Primary run repeated identically with final source manifest/code hashes (`run-v3`). Enriched ML candidates all fail2024 improvement; frozen selection retains baseline RF. Final CAGR: RF18.175%, regular DCA20.938%, range/RSI DCA22.977%, SPY proxy15.779%.
- Ruling: add fixed-rule earlier-history and higher-cost stress checks after the primary run, without tuning or changing model selection — a favorable679-day DCA interval alone cannot support durable20% claims. Cost if omitted: overstated generalization. On2023–Nov2026 DCA returns18.898% CAGR vs SPY20.741%, failing the target. No promotion.
- Latest develop8e9e177 integrated in merge2d52f9c. Targeted upstream integration checks:30 frontend auth/login tests and76 backend login tests pass; backend built in isolated artifacts. Existing live RFv7, API runtime and schedules remain unchanged by this research.
- Fresh reviewer found no Critical/Important issue. Both minor input-integrity concerns were reproduced red and fixed: daily cycle manifest/hash validation; rejection of a truncated monthly history instead of indefinite stale asof carry-forward. Final220-case run and genuine-data rerun pass after those fixes. Final API5274 readiness/frontend5173 return200; API/cycle tasks run, retraining task is Ready between due runs.
- Research commit93e4b34 pushed to the existing `codex/ml-local-completion` PR head. PR290 remains open/draft, with latest develop8e9e177 base and rewritten description reflecting actual scope/results/limits. Final reviewer independently confirmed19 research tests, identical run-v2/run-v3 metrics and all current code/input hashes. No new strategy promotion or paid resource was created.
