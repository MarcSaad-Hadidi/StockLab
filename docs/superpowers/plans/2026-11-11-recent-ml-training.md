# Recent ML Training Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Reproduce the assessed recent training window without changing signal/risk thresholds or existing lineage.

**Architecture:** Add one opt-in `recent-history-v1` retraining profile. Compute causal features from the complete preserved snapshot, reserve the latest 50 supervised dates for evaluation, and retain four calendar years before that boundary for training. Existing trainers, artifacts and audited promotion remain authoritative.

**Tech Stack:** Existing Python/pandas/scikit-learn retraining workflow, native Windows tasks and SQL paper engine.

**Spec:** The constraints and research evidence below form the scoped design; cadence/implementation choices were delegated by the user.

## Global Constraints

- Keep BUY/SELL thresholds 0.60/0.40 and directional-score minimum 0.65.
- Keep 20% symbol exposure, 10% order allocation, ten positions and no shorts.
- Preserve all datasets, legacy journals, historical decisions, registry versions and personal portfolios.
- Do not promote a rejected audit. The user explicitly accepted a 0.2-percentage-point drawdown-increase tolerance for this virtual portfolio; save a new evaluation under that policy and preserve rejected evidence.
- No new provider requests, paid resources, real broker orders or forced trades.

## Review Focus

- Optional profile omission must preserve legacy config identities and readable journals.
- Training feature and target dates must precede the holdout boundary; no holdout leakage.
- Small datasets must skip safely; invalid profile values must fail before writes/provider calls.
- Preserve complete historical input/feature warm-up while limiting fitted rows.
- Replay and native startup must preserve completed decision identities and user records.

## Evidence

Five completed live sessions generated two BUYs, both below 0.65. The existing v6 candidate regressed and was rejected. A declared 2025 selection across unchanged LR/RF trainers and 1/2/3/4-year histories selected RF/four years. A separate September–November 2026 historical screen improved F1 from 0.504 to 0.561 and gross return from 1.22% to 4.12%, with drawdown 2.82% to 2.92%. The original zero-increase audit rejected that last delta; no promotion yet. Prior inspection makes this exploratory historical evidence.

## Task 1: Reproducible recent training

- [x] Add failing tests in `ml/tests/test_retraining.py` for real publication, legacy identities, invalid profiles and insufficient history.
- [x] Implement the optional config in `retraining/contracts.py`, legacy omission in `state.py`, and one shared input/fraction selection in `orchestrator.py` used by both trainers and validation.
- [x] Run retraining tests and full ML suite; document the profile in `ml/docs/local-operation.md`. Full ML: 1,681 passed; strengthened review target: nine passed.
- [x] Obtain independent review; fix supported findings with regression tests. No Critical/Important findings; strengthened the exact earliest-row assertion.

## Task 2: Conditional commissioning

- [x] If explicitly accepted, save a new audited evaluation with the accepted tolerance and apply only APPROVED evidence. Preserve the rejected audits.
- [x] Retire the completed old cycle scope; configure a disjoint new model-version scope and the new training profile.
- [x] Run a genuine native cycle and replay; inspect actual orders/refusals and unchanged personal data. Native result: four HOLD/one rejected SELL, zero purchases. Replay preserved every SQL table count/checksum and personal records remained unchanged.
- [ ] Commit/push the reviewed public change and update the existing draft PR with exact validation and limits.

Integration preserves the prior unmerged ML work while incorporating current
develop. Backend verification: 1,389 passed with 77 opt-in skips, then 116 real
SQL cases passed with zero skips. This is an improvement among assessed variants,
not evidence of a world-best model or maximum future returns. The latest genuine
cycle still has no BUY; do not force a purchase or substitute historical fills.
