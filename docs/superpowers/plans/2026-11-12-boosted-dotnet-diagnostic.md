# Genuine HGBT28 Python to .NET diagnostic integration plan

> **For agentic workers:** Use the existing native TDD/review workflow; public code is committed before the single actual-artifact process proof. The full ML goal is not replaced by this integration stage.

**Goal:** Verify the genuine28-feature/20-session model through the actual .NET/Python process boundary needed for local commissioning, preserving its raw event and policy evidence. The requested >20%/sales/lower-drawdown and automatic paper-cycle goal remains incomplete until separately demonstrated and integrated.

**Architecture:** Add a typed read-only diagnostic entry point using the already verified input preparer, explicit Archived registry/version and artifact APIs. It cannot return the legacy persistable `AiInferenceBatch`. Invoke it from an operator command dispatched BEFORE host construction. No new provider, dependency, EF model/migration, DB state, model activation or runtime config change.

## Why this stage

The existing Python pipeline requires Active, split_date and seven baseline features. Its confidence generator disallows high-score trend-blocked HOLD. C# `AiDecisionPipelineService` always persists, and the old batch omits target/raw probability/reason/status. Therefore do not route this Archived experiment through that pipeline, coerce p=.99 HOLD to .5, call p(up20) SELL confidence, bypass Active, or fabricate legacy holdout metrics. This stage proves the actual process/semantic handshake; native acquisition, qualified promotion, target-aware persistence/risk/execution and restart remain necessary later work.

## Python contract

New `ml/src/stocklab_ml/boosted_diagnostic.py` exports `infer_diagnostic(*, registry_path, registry_sha256, artifacts_dir, model_version, bundle_dir, manifest_sha256, as_of_utc, trusted_artifacts=False) -> dict` plus `python -m stocklab_ml.boosted_diagnostic` CLI. All paths are explicit absolute ordinary paths. Require literal trusted flag, exact lowercase hashes/version and explicit HGBT28 Archived record; never select Active/fallback, fit, fetch, publish or write state.

Read/hash the isolated registry before/after; prepare the externally hash-bound local bundle; select EXACTLY original-five sorted rows at its latest cutoff; load that exact version through existing trusted artifact APIs; call `loaded.predict(latest)` then `policy_decisions(latest)` on the SAME frame. Keep every raw p and trend-blocked reason. Require valid original model identity/target20 and genuine estimator SHA, actual training UTC and historical fit boundary from the bound record. Generate preparation/inference timestamps internally; compare model creation to the requested as-of explicitly. Historical reproduction before actual model creation is permitted ONLY in this diagnostic, flagged true, never called prospective.

Closed output fields: `schema_version=1`, `mode=diagnostic_only`, `execution_eligible=false`, model name/version/status Archived, target_name/definition and horizon20, policy `trend200-immediate-v1`, feature_date, as_of_utc, prepared_at_utc, inferred_at_utc, trained_at_utc, fit_boundary, model_created_after_as_of, prospective_status PENDING, input_manifest_sha256, feature_frame_sha256, policy_context_sha256, artifact_sha256, registry_sha256, and `decisions`. Exactly five ordered decision objects contain symbol, numeric finite probability_up, action, reason, close_ma50, close_ma200 and momentum_20. No `confidence` field or live decision IDs.

Schema, model/target/policy/status/false eligibility, date/time ordering and probability ranges are mandatory. BUY iff p>=.65 and MA50>0; SELL iff p<=.35 and MA200<0 and momentum20<0; otherwise HOLD. Reasons are the existing up_model/buy_trend_failed/sell_trend_failed/threshold_hold meanings. Strict .65/.35 inclusive cutoffs and strict trend signs match existing code. Generic sanitized stderr/failure, bounded stdout, no private path/report/credential disclosure.

## .NET contract and scope

- Add distinct `BoostedDiagnosticBatch`/decision DTOs, never implementing/converting to `AiInferenceBatch` or `IAiDecisionInferenceProvider`. Trading eligibility is always false. No History/Risk/Execution service calls.
- Add `PythonBoostedDiagnosticRunner`: safe ArgumentList/UTF8, fixed module, process-local OMP1/PYTHONPATH, bounded64KiB stdout/stderr, validated timeout/cancellation/cleanup and strict duplicate/unknown-field JSON parsing. Independently recompute each action/reason from raw p and its three trends. Verify expected version, input/registry hash, target/profile/status and all UTC relationships. Reuse existing process/validation patterns; extract a small shared process helper only if BOTH real callers use it while preserving existing validation/error handling.
- Add `BoostedDiagnosticCommand` and tiny `Program.cs` dispatch for `--diagnose-boosted` BEFORE `WebApplication.CreateBuilder`. Require explicit `--python-executable`, `--ml-directory` and all Python arguments; no appsettings expansion. Reject mixing old operator commands and unknown/duplicate options. Print only the validated diagnostic result or a sanitized failure. Do not create host/services/database/HTTP/background worker.
- Tests reach new Python module/test, DTO/runner/command/Program dispatch, appropriate C# tests and docs. Existing baseline Active/inference/persistence tests remain unchanged. Keep53 research/84 snapshot source bindings unchanged; backend code is outside those closures. No old build cache/worktree tempdata reused.

## Tasks and proof

- [ ] Write/run failing Python and .NET tests, then implement the minimal complete CLI/runner/typed parser/dispatch. Cover boundary probabilities/signs/HOLD p=.99, exact5 keys/order, target/status/version/hash substitution, duplicate/extra JSON, nonfinite/truncated output, dates/UTC flags, bounded streams, process failure/timeout/cancellation and no writes. Retain raw scores, no confidence conversion.
- [ ] Run new and adjacent suites, then required full ML/backend checks on final code; independent review semantic and process/error boundaries.
- [ ] Commit public code/protocol, execute one actual .NET operator invocation through Python/preparer/genuine Archived artifact against the existing historical bundle. Compare all five latest scores/actions/reasons/three-trend values against the independently audited existing proof, and exact identity/digests/timestamps/eligibility. Trap or otherwise verify zero fitting/provider/account/persistence calls and all protected bytes unchanged. The historical as-of12Nov/trained13Nov distinction remains explicit. A second identical-input invocation may verify read-only retry/fingerprint stability only; it is not a new model/account experiment.
- [ ] Independently audit process/source/input/output/protection evidence without refitting/replaying/reloading pickle unnecessarily; publish results and remaining real-cycle requirements in the existing draft PR. No merge or ML-complete claim.

Keep this pre-proof protocol unchanged after actual execution. Completion belongs in the public guide. A passing diagnostic does not qualify, activate or commission a model and does not satisfy the financial objective by itself.
