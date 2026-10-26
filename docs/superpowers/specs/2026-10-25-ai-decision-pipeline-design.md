# AI decision pipeline (#257)

Implement the manually triggered first cycle agreed in the project sequence: a trusted operator command runs actual Python inference and records original BUY/SELL/HOLD decisions. No risk approval, order, valuation, promotion, training or periodic scheduler belongs to this change.

## Approach

Reuse the existing Python artifact verification, registered prediction association, causal features and confidence/signal policies. A backend inference provider invokes a fixed Python module without a shell, then an application service records the validated batch through `IAiDecisionHistoryService`. An explicit `--run-ai-decisions` command uses the existing DI container and exits without starting HTTP/background workers.

The alternative of importing arbitrary prediction JSON would not trigger actual inference. Adding an ML HTTP server, broker or cloud worker is unnecessary for the first operator cycle. The subprocess approach and these defaults are implementation choices, not additional user requirements.

## Inputs and selection

- Required private configuration: Python executable, ML directory, existing registry path (`AiTraderMetadata:RegistryPath`), artifact directory, verified daily OHLCV CSV, one explicit model family, and 1..100 distinct canonical symbols.
- Executable joblib input requires explicit `TrustedArtifacts=true`; integrity hashes alone do not establish trust.
- Select that family's current Active version. Never select an arbitrary family, archived version or fallback, train, or promote implicitly. Verify the exact registered artifact before deserialization and recheck Active identity after inference.
- The operator supplies a closed decision-session date strictly before today's UTC date and an exact expected database name. Each requested symbol must have a valid observation on that date; insufficient, missing or invalid input fails the entire inference batch. Future observations are removed before feature engineering. The training split boundary must not exceed the decision date.
- This cycle consumes a locally prepared CSV; authenticated fresh-data ingestion remains the existing data pipeline's/operator's responsibility. It does not claim live-market freshness or validate exchange calendars.

## Boundary and persistence

Python stdout carries one schema-v1 JSON object: `schema_version`, `model_name`, `model_version`, `decision_date`, `decisions`. Each decision has only `symbol`, uppercase `signal` and an exact decimal `confidence` string in [0,1], at most 28 fractional digits. No paths, models or credentials appear in the result. Standard error and unexpected failures are never echoed by the backend command.

Use fixed module arguments through `ProcessStartInfo.ArgumentList`, a private working directory/PYTHONPATH, bounded concurrent stdout/stderr readers, a timeout and cancellation that kills the process tree. Reject malformed, duplicate/extra-field, oversized, wrong-date, wrong-family, missing/extra-symbol and invalid numeric results before persistence.

Derive each stable decision Guid from an unambiguous serialization of a versioned namespace, model name/version, symbol and session date. Validate the whole batch before the first history call. Existing history semantics preserve identical retries and reject payload changes for the same identity. Writes remain atomic per decision, not per batch: a storage failure may leave a valid prefix; rerun the same immutable inputs to resume safely. No user or AI portfolio initialization/mutation occurs.

## Verification

Python tests use real fitted models and verified artifacts, block training/network, and cover both families, Active selection, trust/integrity, missing/stale sessions, future-prefix invariance, version changes and CLI failure sanitation. Backend tests cover all signals, complete validation before writes, deterministic replay/conflict, failures/cancellation, strict process output and operator database guards. A local actual-process smoke joins both sides without promoting the operational models. Run relevant tests, full Python and backend suites, review the diff and publish a PR against develop.
