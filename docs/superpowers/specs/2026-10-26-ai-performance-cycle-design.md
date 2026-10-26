# AI Trader performance cycle (#259)

## Intent and approach

Continue the merged #257/#258 sequence with automatic paper-portfolio performance observation. Reuse `AiTraderValuationHistoryService` and its quote validation, decimal math, serializable state verification and append-only snapshots. No decision inference, order submission, model activation, retraining job or user-portfolio mutation is added.

An in-process opt-in worker fits the existing alert-monitoring architecture and needs no cloud dependency. An external scheduler can use a one-shot operator command calling the same service. A cloud queue/Lambda is deferred to #50/#52. These are implementation choices made within the user's instruction to continue the next issue; no additional approval gate is needed for the established workflow.

## Durable capture and lineage

Extend existing snapshots with nullable `CycleSlotUtc` and nullable `TradeContextJson`; legacy/manual observations retain null context. Unique filtered `(AiPortfolioId, CycleSlotUtc)` prevents duplicates. A deterministic capture GUID names each UTC slot; the interval defaults to one hour and is constrained to 1 minute..1 day. `RunOnceAsync` computes the current UTC slot; it never fabricates missed historical samples.

Refactor only the existing capture core to accept optional cycle metadata. A cycle replay looks up the committed slot before any provider I/O and returns the original observation even when prices or holdings have changed. Under the same serializable transaction as state verification and snapshot insertion, project all committed trades for the verified AI portfolio, with original nullable decision/model identity and realized P&L. Persist that context with the snapshot, retaining legacy unknown identities/P&L as null. The portfolio value is aggregate across models; do not invent per-model attribution or training labels. A crash before commit leaves no partial capture/context. Concurrent insert conflicts read back a committed slot after disposing the failed transaction; otherwise surface a controlled conflict for retry. Manual capture retains its existing revalue-and-compare contract.

Context is a cumulative immutable JSON projection of trade facts, intended for the current small paper-trading history; storage grows with trades times captures. Record this limit in code/docs, and move to normalized/paged provenance storage if history grows materially. Read responses page context at 1..200 trades and report total count. Do not return the unbounded JSON projection or private provider details to clients.

## Scheduling and operator boundaries

`AiPerformanceCycleOptions`: Enabled=false, Interval=01:00:00, ExpectedDatabase required when used. Before new capture, validate exact database name, connectivity, current migrations and an existing initialized system portfolio. No implicit schema/setup changes. Reject Mock valuation outside Development, matching the read API's existing rule. Worker uses a fresh scope, sequential timer ticks and cancellation; it catches failures with a fixed sanitized warning and retries only on a later tick. Its warning never logs exception text/objects; existing EF/provider server diagnostics retain the application's normal private logging policy. Do not globally suppress unrelated API diagnostics. Operator command `--capture-ai-performance` runs once and exits, mutually exclusive with existing commands; suppress command-only provider logs and return safe summary/nonzero on failure.

Normal startup with disabled worker does no capture or quote I/O. Do not enable the worker, apply migrations or run captures in the user's preview database as part of implementation. No catch-up/backfill, distributed scheduler, metrics dashboard redesign or endpoint writes.

## Read contracts and verification

Keep existing chart/history/performance contracts intact. Add authenticated read-only cycle list and exact-capture paged trade-context routes using existing decimal-string conventions; quote-free reads. These data are also available to trusted backend/export callers for future backtesting/retraining without claiming causality or leaking model artifacts.

Use real isolated SQLite/LocalDB portfolio/risk/execution/history services to test capture after trades, original model/version lineage, legacy nulls, replay with changed prices and no provider calls, next slots, bad quotes, missing portfolio/database/migrations, rollback/cancellation, stale state and concurrent duplicates. Test worker disabled mode, sequential ticks/recovery/shutdown and actual operator entry. Run full backend and CI; preserve the existing preview servers and all user state.
