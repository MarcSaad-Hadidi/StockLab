# Isolated HGBT28 Python/.NET diagnostic

This is an explicit read-only diagnostic for the genuine `hgb_swing20_market28` Archived artifact. It is not an Active inference path, a persisted decision batch, a promotion, or an executable trading command. No provider, fitting, portfolio replay, database, scheduler or broker work belongs to this mode. The financial and full native commissioning goals remain separate.

The Python function is:

```python
infer_diagnostic(*, registry_path, registry_sha256, artifacts_dir, model_version,
                 bundle_dir, manifest_sha256, as_of_utc, trusted_artifacts=False)
```

The Python CLI takes the same explicit arguments as kebab-case options, with `--trusted-artifacts` required. Its fixed module is `stocklab_ml.boosted_diagnostic`; model name, target and policy are fixed rather than caller-selected. Registry, artifacts and bundle paths must be absolute ordinary paths. Registry and input manifest hashes are external lowercase SHA-256 values. No Active selection, latest-version lookup or fallback occurs.

The .NET operator mode is `dotnet StockLab.Api.dll --diagnose-boosted` plus `--python-executable`, `--ml-directory` and all Python options. This branch runs before `WebApplication.CreateBuilder`: it does not read application configuration, construct the HTTP host or resolve History/Risk/Execution/DB services. Unknown, duplicate or mixed operator options fail. No application setting, native worker configuration or existing operator behavior is changed.

## Exact diagnostic semantics

The preparer validates the source-bound bundle and returns all eligible features. This diagnostic selects only the five canonical original-stock rows at the cutoff, verifies that cutoff is on/after this model's historical fit boundary, loads the explicit version using existing artifact safety, then calls `loaded.predict(latest)` and `predictions.policy_decisions(latest)` on the SAME frame. The existing versioned context binding is retained. The registry bytes are checked against the external hash before and after inference; no record or binary is changed.

The closed payload contains exactly:

```text
schema_version, mode, execution_eligible, model_name, model_version, model_status,
target_name, target_definition, horizon_sessions, policy, feature_date,
as_of_utc, prepared_at_utc, inferred_at_utc, trained_at_utc, fit_boundary,
model_created_after_as_of, prospective_status, input_manifest_sha256,
feature_frame_sha256, policy_context_sha256, artifact_sha256, registry_sha256,
decisions
```

Constants are schema 1, mode `diagnostic_only`, eligibility false, model `hgb_swing20_market28`, status `Archived`, target `target_20`, definition `open[t+21]/open[t+1]-1 > .002`, horizon 20, policy `trend200-immediate-v1` and prospective status `PENDING`. There is no confidence field, nullable substitute or persistable decision ID.

`decisions` contains exactly five ordered objects for AAPL, JNJ, JPM, MSFT, XOM, each with exactly `symbol, probability_up, action, reason, close_ma50, close_ma200, momentum_20`. Probability and trends are finite JSON numbers representing their actual binary64 values; probability stays in [0,1]. No clipping, rescaling or fabricated reliability applies.

- BUY: p >= .65 and MA50 > 0, reason `up_model`.
- SELL: p <= .35 and MA200 < 0 and momentum20 < 0, reason `up_model`.
- High-score trend-blocked HOLD: `buy_trend_failed`.
- Low-score trend-blocked HOLD: `sell_trend_failed`.
- Interior-score HOLD: `threshold_hold`.

The .NET typed parser independently checks this truth table, all closed fields, five identities/order, expected version/hashes, target/policy/status, false eligibility and UTC relationships. A genuine p=.99 HOLD must retain .99 and its failed-trend reason. This type never converts to `AiInferenceBatch` or implements the live inference interface.

## Time and evidence

`as_of_utc` is the requested source cutoff, while preparation and inference UTC come from actual clocks. UTC arguments support at most microsecond precision; .NET timestamps with a nonzero seventh fractional digit are rejected before starting Python, without rounding. `trained_at_utc` is the artifact's actual creation time, and `fit_boundary` its separate historical first-prediction boundary. Require `feature_date >= fit_boundary`, `feature_date < as_of_utc.date`, `as_of_utc <= prepared_at_utc <= inferred_at_utc <= now`, and `trained_at_utc <= inferred_at_utc`. A historical as-of before actual model creation is permitted in this diagnostic and requires `model_created_after_as_of=true`; it does not permit backward prediction before the fit boundary.

Hashes bind the actual inputs/version, not historical publication availability. The feature-frame hash describes the five rows actually passed to prediction; the policy-context hash describes their date/symbol and three trend columns. Historical reproduction is never relabeled prospective. Repeated invocations retain stable scores/context/hashes but generate fresh real preparation/inference timestamps. No qualification or prospective assessment is produced.

## Process and validation boundary

Use safe process ArgumentList, strict UTF-8, at most 64 KiB each for stdout/stderr, bounded timeout/cancellation and child cleanup. OMP1/PYTHONPATH changes are child-local only. Output is one validated JSON document; errors are generic sanitized stderr with a nonzero exit, without partial decisions, private paths, provider details or tracebacks. No host starts even when diagnostic arguments fail.

Synthetic tests cover actual Archived artifact inference, raw policy/context preservation, backward/date/hash/status refusal, process/stream/error handling and unchanged protected bytes.

## Actual bridge proof

At committed source `ae06a86`, two real .NET→Python operator invocations pass: first with byte-identical copied Python sources and private call probes, then with the normal workspace package. Both use the genuine Archived `v000001`, the externally bound historical bundle and `2026-11-12T00:00:00Z` as-of. No artifact is fitted or account replayed. This is distinct from enabling the automatic trading cycle.

The independent file-only auditor passes at its first execution. It compares both five-row outputs against the prior independently audited CSVs and original annual forecasts: **40 binary64 scores/trends are bit-exact, maximum difference zero**. All ten actions/reasons are independently derived and match; these latest historical rows are all HOLD. Latest-five feature/context digests, 79 copied Python files, six assemblies/dependency files, 53 prior source paths, 84 snapshot bindings and all 15 protected files remain exact. The instrumented pass observes one genuine artifact load and one five-row/28-feature prediction; all 17 guarded fitting, calibration, future-target, replay, provider/network and publication APIs have zero calls. The auditor itself calls no operator, preparer, model loader, fit or account producer.

Actual training is November 13 at 02:01:59.101472 UTC, after the historical November 12 as-of. Current preparation/inference times are retained; `model_created_after_as_of=true`, `execution_eligible=false`, `Archived` and prospective `PENDING` are explicit. This proves faithful historical transport, not prospective prediction, qualification or native execution. No HTTP host/database service is constructed by the early operator dispatch; this is a structural boundary, not a measured global database-call counter.

Private evidence is under `models/boosted-diagnostic-20261112/proof-root-v1`; independent-audit SHA `c696b19fe3a68b28f90102787ce1bc2e9c21db184450347a28a9ca481caa29b4`. The pre-proof protocol is unchanged. Root's final local ML suite passes **2,278 tests with three Windows skips and five known older calibration warnings**; backend passes **1,457 tests with 77 skips**, including 76 opt-in SQL cases not exercised locally and one existing skip. SQL verification belongs to current-head CI.

During adjacent operational-cycle testing, an existing Windows directory-publication failure was captured before fixing it: `Path.rename` raised WinError 5 while the destination was absent. The underlying transient denial cause is unknown. The bounded Windows-only retry preserves the first failure and never intentionally overwrites an existing snapshot; 14 focused tests and both final suites pass. See [local publication behavior](local-operation.md). No existing proof-bound research or snapshot source was changed.
