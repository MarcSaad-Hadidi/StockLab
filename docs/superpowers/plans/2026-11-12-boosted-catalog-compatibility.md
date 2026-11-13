# Target-aware HGBT28 model catalog

The actual HGBT28 snapshot and isolated Archived registry/artifact prediction path are verified. The real model catalog still rejects that record and the frontend assumes non-null one-day holdout metrics. Fix this compatibility gap without promoting a model or claiming the financial20%/lower-drawdown objective is met.

## Minimal closed projection

Reuse the existing authenticated read-only model route, bounded metadata reader, family grouping and UI. Add a single HGBT28 branch after common identity/version/time/status checks and BEFORE legacy holdout/metric parsing. Accept only `hist_gradient_boosting` /`hgb_swing20_market28`, Archived, null metrics/holdout, report keys `snapshot_metadata`/`estimator_sha256`, and the actual closed snapshot schema. Preserve all LR/RF parsing and wire shape.

Validate metadata identity, exact ordered28 columns, target open[t+21]/open[t+1]-1>.002/offset21, false promotion authorization, genuine annual/monthly period and four-year fit/known boundaries, original-five date-weighted counts/priors, actual timestamp equality/no creation before consumed outcomes, fixed parameter map, bounded recorded environment/source/digest types and exact prospective PENDING/null evidence. Do not read source paths, deserialize estimators, enforce server Python-runtime compatibility, or claim that this projection authenticates market data/artifacts.

Application/API model metrics and evaluationPeriod become nullable, required-null only for HGBT. TrainingPeriod remains actual fit dates. Add optional `forecast`, omitted for legacy responses, containing ONLY targetName, targetDefinition, horizonSessions20, exact featureColumns, refitFrequency, fitBoundary and prospectiveStatus PENDING. Never expose raw reports, source paths/hashes, estimator hashes, dependency/code inventories or artifact/registry locations. Actual trainedAtUtc is distinct from the historical fitBoundary.

Production reach: `JsonAiModelCatalogReadService.cs`, an optional small isolated HGBT parser helper, application model DTO, API catalog response, `frontend/src/api/aiTraderApi.ts`, `AiTraderPanels.tsx` and existing EN/FR locales. Add no new route, catalog storage, provider, activation/retrain/backtest controls, Python/core change, config selection or database migration.

Frontend dispatches exact HGBT type to a strict pending validator; it cannot fall back to legacy numeric metrics. Preserve current legacy validation, including its current non-HGBT modelType text behavior and unrelated extra-field handling; do not globally weaken shape validation. Use an explicit HGBT case/nullable type and guard the evaluation period. Existing percent formatting renders null as em dash. Show localized prospective-pending, target/horizon, cadence and historical boundary; detailed view may show the28 feature names. Preserve family-status-does-not-imply-trading guidance and existing legacy rendering.

## Verification

Use synthetic public fixtures only, not the private registry or fitted binary. Focused C# parser/API tests cover annual/monthly/mixed families; strict Active/metrics/target/order/32-feature/cadence/parameter/count/date/digest/pending rejection; authenticated projection without private sentinel paths/hashes/environment; no writes/provider calls. TypeScript API/panel tests cover pending/null/EN-FR details, malformed HGBT refusal, legacy numeric/null behavior and mixed lists. Run meaningful focused checks, then normal lint/build/tests and backend build/unit suite on final code; exact-head CI verifies all jobs.

After code validation, an explicitly isolated read-only parser proof may load the actual privately verified HGBT registry without changing application config, registering/activating anything or touching the database. A real authenticated UI/worker commissioning claim requires later dedicated proof. Native28-feature/vintage preparation, next-session costs/dividends and prospective paper/restart work remain required, as does improved financial performance.

- [x] Implement/review the closed C# projection and safe nullable response with focused fixtures.
- [x] Implement/review strict frontend handling and localized pending rendering.
- [x] Verify final compatibility checks and actual isolated read-only catalog projection.
- [ ] Publish evidence while retaining the full financial and native commissioning objective.

Post-implementation evidence:14 public files implement the closed reader/projection and localized pending UI. Independent review is clear after fixing reserved HGBT-name/wrong-type fallback. Local verification:74 focused backend cases,1419 full backend passes/one existing skip,662 frontend passes, lint/build and zero backend build warnings/errors. Actual isolated read-only service/API-mapper proof passes with registry SHA unchanged`ce99ed92…cff8e` and proof SHA`b0781c65…25e05a`; no host, database, provider, estimator load, config or registry write. Sandbox MSBuild/loopback restrictions were verified separately from assertion failures and final checks ran outside those restrictions. An initial fallback touched generated files in an old build cache; it was stopped and left in place, with subsequent verification isolated. Private evidence records this incident without claiming byte preservation. No Python/research-source change, promotion or full commissioning is claimed.
