# AI Trader operational cycle (#273) Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task. The user explicitly requested completion of the existing #273 scope in this session.

**Goal:** Commission a reproducible local paper-trading cycle and verify its persisted decisions, outcomes and performance on AI Trader.

**Architecture:** Reuse the existing ingestion, fitted baseline/artifact/promotion APIs and #257/#258/#259 services. A Python local operator runner refreshes immutable session inputs and invokes one guarded backend composition command. Private atomic journals and an OS-held lock preserve progress across crashes; original decision/order identities prevent duplicate trades. No new cloud platform or trading policy.

**Tech Stack:** Python 3.12, existing pandas/scikit-learn/pytest, .NET/EF Core, the existing local SQL Server and React application.

**Spec:** https://github.com/MarcSaad-Hadidi/StockLab/issues/273 and the user's explicit request to finish it and launch the ML on the existing AI Trader page.

## Global constraints

- Preserve the existing database, user portfolios and paper cash/history; no reset or real broker orders.
- Keep BUY/SELL thresholds and the 70% minimum confidence unchanged. HOLD and risk rejection are successful recorded outcomes, not fabricated trades.
- Require the exact database, real configured providers, trusted fitted artifact, Active version and fresh completed daily sessions.
- Sharing the configured Twelve Data website quota requires explicit operator opt-in; no credential is logged or committed.
- Keep raw data, fitted artifacts, database names, local paths, model run identities and individual audits in ignored private locations. Publish generic code/procedure and sanitized verification only.
- Existing #274 diagnostic remains valid; its observations do not certify profitability or authorize threshold tuning on that holdout.

## Review focus

- A stopped process after a committed trade must resume with the same decision/order and capture identities.
- An altered registry, journal, input snapshot or configured universe must fail before execution.
- Missing/stale/partial data or an unsupported calendar date must block; no backlog of old signals.
- Concurrent invocations and restarting the runner must preserve one session's original decisions and completion.
- Errors, CLI arguments and provider failures must not leak credentials, private paths or raw subprocess output.

## Tasks

### 1. Correct inclusive historical ingestion

Files: `ml/src/stocklab_ml/data/models.py`, `ml/tests/test_historical_pipeline.py`.

- [x] RED: the real HTTP request must end at `23:59:59` on the inclusive requested date.
- [x] GREEN: fix the shared request parameter and run ingestion/retraining regression tests.
- [x] Verify one authentic closed session with the corrected ingestion; preserve the earlier probe as private evidence.

### 2. Prepare fresh immutable cycle inputs

Files: new `ml/src/stocklab_ml/operational_data.py`, `ml/tests/test_operational_data.py`.

- [x] RED/GREEN: latest prior-UTC-day US equity session, verified 2026–2028 exchange calendar, no same-day inference; reject unsupported years.
- [x] RED/GREEN: call existing daily/splits ingestion once per symbol, require the expected session for every symbol, preserve original raw responses, validate cleaned snapshots and hashes, reject partial/corrupt/changed cache entries.
- [x] Reuse validated same-session snapshots without another provider request; preserve private diagnostics.

### 3. Compose and checkpoint the existing backend services

Files: new `backend/StockLab.Api/Operations/AiTradingCycleCommand.cs`, its command/journal tests, `Program.cs`, and the valuation-history interface/implementation for idempotent cycle capture.

- [x] RED/GREEN: exact database/migration/portfolio/provider guards and read-only preflight before inference or execution.
- [x] RED/GREEN: explicit model/version/universe and snapshot hash; sequential inference → risk/execution for only returned original IDs → capture after outcomes.
- [x] RED/GREEN: bounded validated private journal, OS-held lock, atomic checkpoints, crash/retry after execution, completed-session replay without new inference/quotes/trades/captures.
- [x] RED/GREEN: stale prepared date, metadata/input/journal mismatch, failed risk attempt, cancellation and safe structured diagnostics.

### 4. Add the local start/stop runner

Files: new `ml/src/stocklab_ml/operational_cycle.py`, `ml/tests/test_operational_cycle.py`, generic example configuration/procedure in existing ML/operator documentation.

- [x] RED/GREEN: preflight → fresh data → guarded backend command; preserve private stdout/stderr and validate the bounded structured result.
- [x] RED/GREEN: explicit private config, explicit shared-credential consent, one-shot operation, sequential loop, stop request and restart/replay; never automatically train or promote in this runner.
- [x] Record safe last-attempt/stage/failure/success status and keep unattended work local.

### 5. Commission and verify the real local environment

- [x] Use existing fixed-model training/artifact APIs and the audited #78 evaluation/promotion workflow. Record classification, calibration/signal distributions and benchmark evidence privately; activate only a candidate that passes the declared existing policy. Do not optimize thresholds on the inspected holdout.
- [x] Configure the exact existing local database, verified artifact/registry and real providers without committing configuration values.
- [x] Run the first cycle and replay it; verify decisions/outcomes, cash/positions/trades and valuation consistency through the real backend and AI Trader UI. A first cycle may correctly yield only HOLD/rejections.
- [x] Verify multiple runner passes and stop/restart without duplicates; leave the authorized local runner and servers usable.
- [x] Full relevant local suites and fresh independent branch review.
- Publication gate: create/attach the completion PR, require all CI checks green and update #273 with sanitized evidence. GitHub records the final publication/CI outcome.

## Verification ledger

- Base: latest `develop` including merged #274; a clean dedicated `codex/ai-trader-operational-cycle` branch.
- Existing local setup check initially used the preview binary; the current operator's stronger preflight identified missing merged migrations before any execution.
- Authentic data probe: date-only end omitted the requested closed session; the same bounded request ending at `23:59:59` included it. This is an ingestion-boundary defect, separate from #274's model signal diagnosis.
- Ruling: retain a local operator runner/private journal rather than add cloud scheduling or a second SQL execution engine — existing decision/order and valuation services own persistence and settlement.
- Ingestion RED observed on the date-only request; corrected shared parameter. Initial combined regression run: 212 passed, one retraining assertion still expected the old transport boundary. Updated that caller assertion; the 90 ingestion/bounded-refresh cases now pass.
- Operational data: RED for missing module, then 20 cases passed (calendar, freshness, immutable reuse, corrupt cache and invalid scope).
- Backend composition: RED for missing command after using an isolated build output to avoid locking the existing server. Eleven cases pass, including committed-trade and committed-capture interruption/replay, exact guards, input/journal corruption, busy lock and cancellation.
- Runner: initial fixture corrected to use the real cleaned OHLCV contract; RED for missing module, then 13 cases passed. Loop/status, interleaving and Windows-format regressions were observed RED and corrected.
- Private model assessment used unchanged algorithms, selection on an earlier chronological validation period and a separate final historical test. The existing native policy approved the selected candidate and the exact audited promotion was applied. No threshold was changed; trade creation is never forced when original signals are HOLD.
- Independent review identified four issues; all were reproduced with new regressions and fixed: effective confidence floor, execution/valuation quote timestamps, failed-status publication under the pass lock, and reconciliation before advancing to a later session. The reviewer confirmed no remaining blocker in those fixes.
- Final backend verification: 1,291 non-SQL cases passed; the environment-conditional LocalDB case skipped in that selection was explicitly enabled and passed in the final SQL-inclusive run of 96 cases. This includes a SQL-backed full-cycle settlement/replay case. Two timing-sensitive tests initially failed during simultaneous heavy suites; all 21 affected/cycle cases and the full non-SQL suite passed on recheck with bounded concurrency.
- Final full ML suite: 1,661 passed. Real commissioning found a UTF-8 BOM in the existing Windows User Secrets file; two regressions reproduced the failure, and operator config/credential reads now accept that encoding while preserving strict/bounded JSON validation. Expanded input/runner suite: 40 passed.
- Correction to initial environment check: the old preview binary did not know the two newly merged migrations. The current operator correctly blocked at MIGRATIONS; reviewed additive migrations were explicitly applied to the guarded local target, with unchanged user portfolio/holding/transaction counts and checksums.
- Authentic local cycle, same-session replay, two automatic passes, clean stop and restart succeeded. Session journal bytes remained unchanged across replays, and user portfolio/holding/transaction counts/checksums remained unchanged. The private runner remains active at the documented 30-minute check interval.
- Authenticated AI Trader overview, decisions and original decision details were verified against the real backend without interception; no browser error was recorded during that flow. Detailed screenshots/runtime/model evidence remain private and ignored by Git.
- Final independent review found no actionable blocker in the code/documentation deltas.
