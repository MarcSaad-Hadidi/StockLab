# Daily Multi-Asset ML Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Commission an evidence-backed daily multi-asset paper strategy using the existing ML and backend workflows.

**Architecture:** Reuse the established Python algorithms, chronological splits,
backtests, fitted artifacts and audited promotion. Keep commissioning data and
machine configuration private. Change public runtime code only if a demonstrated
defect prevents this existing multi-symbol contract from working.

**Tech Stack:** Python/scikit-learn/pandas, existing .NET paper engine, Windows Task Scheduler and LocalDB.

**Spec:** `docs/superpowers/specs/2026-11-06-daily-multi-asset-ml-design.md`

## Global Constraints

- Fixed universe: AAPL, JNJ, JPM, MSFT and XOM; genuine daily OHLCV, no fabricated rows.
- Preserve USD 100,000 system portfolio, user portfolios, registry and prior journals.
- Default risk floor 0.70; explicit selected `daily-multi-asset-v1` pins 0.65 only in its child process; exposure 20%; order allocation 10%; ten positions; no shorts.
- No forced decisions, unvalidated score changes, cloud provisioning or real broker orders.
- Original and new machine state remain in ignored private directories.

## Review Focus

- No-trade model: screening must reject it even when classification scores pass.
- Temporal leakage: selection and final holdouts remain distinct; fit/scaling use train rows only.
- Universe change: use fresh input/state scope, preserve old scopes and exact historical versions.
- API limits: bound requests to the five-symbol universe and retain immutable cached inputs.
- Repeated task starts: existing owner/pass locks and durable journals prevent duplicate execution.

### Task 1: Authentic multi-asset assessment

**Files:** Private commissioning directory `ml/models/portfolio-daily-20261106/`; existing modeling/backtesting/artifact/promotion APIs are consumed unchanged.

- [x] Prepare authentic completed-session data for the five-symbol universe.
- [x] Run the initial fixed-0.70 study with January–June 2026 validation and July–November final evaluation; reject it for insufficient activity.
- [x] Compare the declared 0.60/0.65/0.70 floors with algorithm/policy selection on 2025 only and final historical evaluation on 2026; disclose earlier inspection of 2026.
- [x] Select RF/0.65 by eligible validation return, then verify final activity, classification, drawdown and estimated turnover costs. Disclose benchmark underperformance.
- [x] Refit the exact selected artifact, assert report equality, five-symbol coverage and immutable source digest, and save its audited APPROVED evaluation.

Expected: an eligible genuine trading candidate, or a precise rejection report without runtime mutation.

The first fixed-floor study was rejected. Under the spec's exploratory extension,
repeat assessment with selection in 2025 and final evaluation in 2026 for the
declared score-floor grid 0.60/0.65/0.70. Select only on validation, disclose prior
inspection of 2026, preserve current runtime risk settings during research, and
apply the cost/activity/quality screens before any deployment.

The eligible pair is RF/0.65. Implement the explicit `risk_profile` enum in
`ml/src/stocklab_ml/operational_cycle.py`, the matching named policy in
`backend/StockLab.Api/Operations/AiTradingCycleCommand.cs`, and schema-v2 policy
pinning in `AiTradingCycleJournal.cs`. Tests in `ml/tests/test_operational_cycle.py`
and `backend/StockLab.UnitTests/Api/AiTradingCycleCommandTests.cs` must prove scoped
arguments, unknown-profile rejection, a .67 BUY accepted only under the named
policy, unchanged defaults and rejection of policy changes on replay.

- [x] Add and verify the explicit process-scoped profile and schema-v2 policy pinning.
- [x] Reproduce and fix review findings: ambient profile inheritance and adoption of SQL decisions from a fresh state directory.

### Task 2: Audited commissioning and recovery

**Files:** Existing private registry/artifact roots, native task launcher/configuration and new private input/journal scope; `ml/docs/local-operation.md` only if procedure changes.

- [x] For an eligible candidate only, register its exact fitted artifact, run audited evaluation and apply only an APPROVED decision.
- [x] Point the cycle and cadence-gated retraining to the same five-symbol universe with fresh journals; preserve old scopes. Reuse verified cycle inputs for retraining to avoid a competing API batch.
- [x] Run a genuine paper cycle and replay under the native task context. Inspect recorded decisions, positions, trades and unchanged user records.
- [x] Run relevant existing tests; any discovered production bug requires a failing regression before its fix. Final ML: 1,672 passed. Backend with real LocalDB enabled: 1,504 passed, zero skips/failures.
- [x] Review the resulting public diff and record exact evidence and limitations for the existing draft PR.

Expected: a reproducible accepted strategy operating on five symbols; otherwise retain the previous runtime and state the strategy-quality blocker.

Commissioned RF v000005 using the exact approved audited CAS, preserving the
existing LR-family Active version and original USD 100,000 portfolio. The genuine
2026-11-05 cycle produced AAPL HOLD plus four SELLs: JNJ/JPM/XOM rejected for low
score, MSFT rejected because no position was held. There was no BUY signal and
no immediate purchase. Replay preserved every SQL table count/checksum; non-AI
tables also matched the pre-cycle snapshot. Native readers validate nine models
and seven reports. Historical buying/selling is not a promise of an order today.
