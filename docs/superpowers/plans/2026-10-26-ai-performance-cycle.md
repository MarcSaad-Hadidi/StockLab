# AI performance cycle implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement in this session; independent read-only review before publishing.

**Goal:** Complete #259 with durable periodic AI paper-portfolio observations and original trade/model context.

**Architecture:** Extend the existing append-only valuation capture transaction, preserving manual capture semantics. Use a disabled-by-default scoped worker and one-shot operator entry with a shared UTC-slot service; add quote-free authenticated provenance reads.

**Tech stack:** Existing .NET 10, EF Core SQL Server/SQLite, TimeProvider, PeriodicTimer; no dependencies.

**Spec:** `docs/superpowers/specs/2026-10-26-ai-performance-cycle-design.md`.

## Constraints and review focus

- Never initialize/migrate implicitly, activate models, infer, trade or mutate user accounts.
- Manual `CaptureCurrentAsync` continues revaluing and conflicting on changed facts; cycle replay returns immutable stored facts without quotes.
- Capture context and portfolio value in one verified serializable transaction; preserve unknown legacy fields as null.
- Concurrent slot insert, stale portfolio during quotes, cancellation and malformed quote must leave no partial history.
- Disabled worker does no I/O; enabled runs sequentially, validates target/migrations and retries only on a later tick with sanitized logs.
- Persisted context grows with history; page public reads and document this limit. Historical observations cannot be backfilled from current prices.

## Tasks

- [x] Add failing `AiPerformanceCycleTests` using real existing isolated fixtures: exact trade/decision/model identity after BUY/SELL, same-slot no-quote replay after market/state changes, next slot, missing prerequisites, invalid quotes, cancellation and rollback. Observe missing-contract compile failure.
- [x] Add cycle DTO/interface/options; extend snapshot entity/configuration and the existing valuation capture core. Add scoped cycle service using exact target checks and deterministic current-slot identity; generate migration with separate build artifacts. Run cycle and all existing performance tests.
- [x] Add failing worker/operator/API tests for disabled behavior, scoped sequential ticks, failure/recovery/shutdown, mutually exclusive command safe output, bounded authenticated read-only cycle/context responses and legacy nulls. Implement worker/DI/config/command/reads using existing project patterns. Run targeted checks and actual Mock/LocalDB operator process.
- [x] Add SQL race/migration/rollback checks and run full backend including LocalDB/Python process contracts. Review the complete diff independently, resolve findings and verify the final tree.
- [ ] Update operations/README and remaining-work summary; commit/push/create PR to develop, attach it and wait for all CI checks. Do not merge or commission the user's database.

## Verification ledger

- Observed missing cycle contract compile failure before implementation; 40 real cycle/manual/performance tests then passed.
- Observed missing worker/command compile failures and missing HTTP route (404 rather than authenticated route); implemented components, then 14 tests passed.
- Corrected migration test seed to the existing SQL `Name` column; no product change was required.
- Ruling: a SQL deadlock victim may reread before the winning transaction commits and return controlled ConcurrencyConflict; the spec explicitly permits this and a later invocation must replay the one committed slot. The race test accepts only that category, requires at least one committed result, one persisted cycle and an immutable successful replay. No automatic in-call retry loop is added.
- Ruling after review: sanitize the new worker's own warning and all operator output, while retaining existing private server EF diagnostics. Clarified the initially overbroad spec wording to match the existing alert-worker/application logging policy and operations documentation; no unrelated global logging suppression is introduced.
- Full-suite OpenAPI test exposed its exact list of eleven original routes. Extended that shared contract and auth/write checks for the two cycle reads, documented their decimal-string schemas/bounds, and supplied the same standard error metadata/validation shape as the existing controller.
- Final build: 6 OpenAPI/write-refusal checks passed, then the entire backend suite passed 1,368 tests with zero failures/skips and LocalDB enabled. EF reports no model drift; preview/frontend and API health both still return HTTP 200. Independent review found no remaining blocker.
