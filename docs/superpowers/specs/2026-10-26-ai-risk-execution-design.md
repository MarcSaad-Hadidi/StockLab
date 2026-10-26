# AI risk/execution orchestration (#258)

## Goal and approach

Connect persisted original ML decisions to the existing risk manager, rejection history and AI paper-trading engine. Reuse their rules, atomic settlement and mutual exclusion; never duplicate risk sizing or add a broker integration. Continue the agreed operator-triggered sequence; periodic scheduling and performance capture remain #259/later work.

`IAiRiskExecutionService.ExecuteAsync(decisionId)` loads the original decision, skips HOLD without quotes or orders, and returns an existing terminal trade/rejection without reevaluation. New BUY/SELL attempts fetch a trusted USD execution quote using the engine's existing validation, pass the original symbol/signal/confidence to `IAiRiskManager`, persist a refusal or send the approval to `IAiTradeExecutionService`. Reuse the decision Guid as the one stable order ID for this orchestration. The engine remains the atomic revalidation/settlement authority.

## Attempt journal and recovery

The existing trades/rejections cannot record transient execution failures. Add one SQL table `AiExecutionAttempts`, linked to the original decision and optional committed trade: attempt ID, start/completion timestamps, controlled status/failure code, requested price/approved quantity and replay flag. No user foreign key or account mutation. Migration only creates this table and indexes; applying it remains an explicit operator action.

Persist STARTED before quote/risk/execution work, and persist the validated approval before submitting an order. Final statuses are EXECUTED, REJECTED, FAILED or CANCELLED. Provider/SQL exception text is never stored. HOLD is not an execution attempt and creates no journal row. A replay of a known terminal decision creates no new attempt.

Validate risk output against the original decision and resolved price before submission. Business refusals, including an engine `RiskChanged` with an explicit risk reason, use the existing append-only rejection history. Quote failures, concurrency errors and other technical failures are journaled as FAILED without inventing a permanent rejection. An explicit retry creates a new attempt with a fresh evaluation; no automatic retry loop or changed order identity.

Different workers may start attempts concurrently; the existing trade/rejection transactions remain the decision-level authority. After an execution/rejection exception, consult persisted outcomes before classifying the attempt as failed. If another worker won, return that authoritative result as a replay. Complete journal rows conditionally from STARTED so recovery cannot overwrite terminal evidence.

The engine and attempt completion use separate commits. A crash/database failure after settlement can leave STARTED alongside a real trade/rejection. A later invocation reconciles open attempts from that authoritative outcome before returning, without quoting or executing again. An orphan STARTED with no outcome remains explicitly uncertain; a retry is safe through the engine's existing stable-ID/unique-decision guards. Never mark another possibly active worker interrupted merely because its row is open. Cancellation attempts bounded, independent-token audit completion, then preserves cancellation; audit failure may leave STARTED and must not be reported as durable completion.

## Operator interface

Add `--execute-ai-decisions` with `AiRiskExecution:ExpectedDatabase` and explicit `AiRiskExecution:DecisionIds:0`, `:1`, ... (1..100 distinct nonempty IDs). Validate the complete ID set and existence before execution. Require an initialized system-owned AI portfolio and current migrations. Do not initialize/migrate, choose historical decisions implicitly, invoke Python, fetch inference data or activate models. Future schedulers may call #257 and then this service using the returned IDs.

One command exits after the explicit batch, without starting HTTP/background workers or adding a write endpoint. Reuse command-only provider-log suppression and sanitized summaries. Processing is per decision, not an all-or-nothing batch: valid earlier outcomes remain on later failure. The caller must inspect results and retry explicit unresolved IDs. No user portfolio or real-money trade is involved.

## Verification

Tests exercise real risk, history and paper execution with isolated SQLite/LocalDB and deterministic quote doubles: capped BUY, full SELL, HOLD, policy refusals, fresh-price reduction, quote/technical failures, cancellation, retries, concurrent calls and crash-window reconciliation. Add schema/migration and actual-entry-point command checks. Confirm user portfolios/transactions are unchanged and no market request runs inside an execution transaction. Run the full backend suite including LocalDB, an independent review, and CI before reporting readiness. Do not run operational decisions on the user's local account.
