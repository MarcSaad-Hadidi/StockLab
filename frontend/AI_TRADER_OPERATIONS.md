# AI Trader: operational prerequisites after #59

The React page reads the eleven JWT-protected GET endpoints from backend #80.
It does not execute trades, initialize a portfolio, capture valuations, train or
promote a model, start a backtest, invoke Python, or access SQL/S3 directly.

## Operator commands (#265)

Run from the repository root with the intended API environment and existing
private connection/provider/JWT settings. Identify the configured database before
initializing; the expected name below must match it exactly. The command refuses
an unavailable database or pending migrations. It never applies migrations.

```powershell
dotnet run --project backend/StockLab.Api -- --initialize-ai-trader --AiTraderSetup:ExpectedDatabase=<database-name>
dotnet run --project backend/StockLab.Api -- --check-ai-trader
```

Initialization reuses #66, creates the portfolio and baseline once, and preserves
existing cash, positions and all user data on subsequent runs. It does not depend
on a populated ML catalog. The read-only check independently reports portfolio,
model and backtest availability. Exit 0 means initialization succeeded, or that
the check found an initialized portfolio and nonempty validated catalogs. Exit 1
means a prerequisite failed, including valid but empty catalogs; its output
distinguishes empty, unavailable and invalid sources. Neither command starts an
HTTP listener or background workers. Normal API startup and GET routes do not
initialize anything. This source check is not a general backend readiness probe;
verify authenticated HTTP requests and real quote access separately.

## Produce authentic local catalog exports

Use an existing verified #56 daily OHLCV CSV. If needed, fetch it once using
`prepare_historical_dataset` as documented in `ml/README.md`. Supply the authorized
ML credential through `TWELVE_DATA_ML_API_KEY` in the Python process environment;
never put its value in arguments, source files or reports. Reusing a Website
Fallback credential requires explicit operator authorization and shares its
provider quota. Preserve the original raw provider response as provenance.

```powershell
$env:PYTHONPATH = "$PWD/ml/src"
ml/.venv/Scripts/python.exe ml/scripts/export_ai_trader_catalogs.py --input-path <verified-ohlcv.csv> --output-dir <new-private-run-directory>
```

The export script makes no network calls. It reuses the existing cleaner, feature
engineering, chronological holdout, Logistic Regression/Random Forest estimators,
registry, backtest and Buy & Hold writers. A new output directory is mandatory;
existing registries/reports are never overwritten. Publication is atomic per file,
not across the whole run: configure the API only after successful completion and
the final `provenance.json`. The provenance records the source CSV SHA-256,
observed period, training run IDs, exact registered versions and report paths.
The operator must establish source authenticity; a hash alone does not prove it.

Models remain Archived. This command neither promotes a family nor selects a
global bot model. Native #73/#74 reports lack proven version/timestamp fields in
their HTTP projection; these correctly remain null. No #78 promotion evidence is
invented.

Configure `AiTraderMetadata:RegistryPath` to the run's `registry.json` and each
`BacktestReportPaths:<index>` / `BenchmarkComparisonPaths:<index>` to the reports
listed in `provenance.json`. Use existing User Secrets for local Development or
the deployment's secure configuration. Leave unproduced promotion sources empty.
Copy/mount original export bytes when the API runs on another host. Restart the
API after changing catalog configuration, then run `--check-ai-trader` and inspect
the page with a valid JWT. Do not point a deployed catalog at test/smoke fixtures.

## Local commissioning evidence — 2026-10-25

The `Development` environment targets the Windows LocalDB preview database
`StockLab_LocalPreview_20261025_1a174fc`. It was initialized twice with the same AI
portfolio identity; the second run preserved the existing state. No user portfolio
or transaction was created or modified by setup.

One authorized TwelveData historical request returned 1,458 actual AAPL daily
observations from 2021-01-04 through 2026-10-22 (requested through 2026-10-23;
missing sessions were not filled). Those observations produced two Archived
model records and four historical report entries: two backtests and two Buy & Hold
comparisons. Both strategy backtests recorded zero executions under the existing
confidence policy. The backend readers validated them through `--check-ai-trader`.
Authenticated browser verification against the actual local API returned HTTP 200
for portfolio/positions/history/catalogs and empty activity histories. All seven
tabs, report selection, refresh and mobile rendering were exercised without HTTP
interception or JavaScript errors. Authentication used a short-lived JWT signed
locally with the existing API signing key; no user/account row was inserted.
The local private run is `ml/models/operational-20261025`; raw data, exports,
credentials and machine-specific absolute configuration paths remain untracked.
This establishes local configuration only; it does not deploy to Azure or start
the automatic decision/execution/performance cycles #257–#259.

## Deployment prerequisites

- Deploy backend #80 and configure the frontend's existing
  `VITE_STOCKLAB_API_BASE_URL` and JWT authentication. An empty URL uses the
  existing same-origin `/api` route (the Vite proxy is local development only).
- Verify the intended Azure SQL connection and migrations, including #79
  `AddAiTraderPerformanceMetrics`. Frontend tests do not establish Azure state.
- Initialize the system-owned AI portfolio through the trusted application/operator
  workflow. GET endpoints never create it. Before initialization the page displays
  the backend's 404 state, with no default capital.
- Configure an existing real market provider and its credentials/budgets outside
  the frontend. A Development Mock provider is not evidence of real prices;
  production valuation endpoints reject it. Quote failures remain errors.
- Provide trusted, private, operator-managed ML metadata exports to the backend
  service account. Configure `AiTraderMetadata:RegistryPath`,
  `BacktestReportPaths`, `BenchmarkComparisonPaths` and
  `PromotionEvaluationPaths` as applicable. Preserve original Python numeric
  serialization for promotion evidence hashes. Publish complete exports atomically.
- Missing/unconfigured catalogs return 503; invalid exports return 502. Only a
  validated `200 []` is an empty catalog. Never deploy test fixtures as substitutes.

## Run one decision cycle (#257)

This operator-only command automatically performs inference and append-only
recording once, then exits. It does not start the HTTP server or hosted workers.
The existing JWT-protected decision-history GETs make recorded decisions visible
to the AI Trader page; the command never creates an account or portfolio.

Set these private Development User Secrets on `backend/StockLab.Api` (or the
equivalent secure deployment configuration). There are no default model, symbols,
paths or implicit artifact trust:

| Key | Required value |
| --- | --- |
| `AiTraderMetadata:RegistryPath` | Absolute path to the authentic existing registry |
| `AiDecisionPipeline:PythonExecutable` | Absolute path to the compatible Python executable, normally `ml/.venv/Scripts/python.exe` on Windows |
| `AiDecisionPipeline:MlDirectory` | Absolute path to this checkout's `ml/` directory; its `src/` supplies the fixed Python module |
| `AiDecisionPipeline:DatasetPath` | Absolute path to a verified daily OHLCV CSV from the existing ingestion pipeline |
| `AiDecisionPipeline:ArtifactsDirectory` | Absolute private #76 artifact root containing `<model_name>/<model_version>/model.joblib` and `manifest.json` |
| `AiDecisionPipeline:ModelName` | One explicitly chosen registered family, e.g. `logistic_regression_baseline` |
| `AiDecisionPipeline:Symbols:0`, `:1`, ... | 1..100 distinct canonical symbols present on the requested session |
| `AiDecisionPipeline:TrustedArtifacts` | Explicit `true` only for trusted private artifacts; joblib deserialization executes code |
| `AiDecisionPipeline:TimeoutSeconds` | Optional 1..300 seconds; default 60 |

Save the fitted artifact during the existing training/storage workflow using
`save_model_artifact(registry.bind_version(...))` (#76). Select/promote the intended
version explicitly using the existing promotion workflow (#78). The #265 catalog
exporter saves metadata and reports, not fitted artifacts, and leaves models
Archived. A missing artifact or absent Active version is a prerequisite failure;
the command never trains, activates, falls back to another version or fabricates
a HOLD decision to fill that gap. The Python environment must match the verified
artifact manifest's supported package versions.

From the repository root, with the intended connection string already installed:

```powershell
dotnet run --project backend/StockLab.Api -- --run-ai-decisions --AiDecisionPipeline:ExpectedDatabase=<exact-database-name> --AiDecisionPipeline:DecisionDate=<YYYY-MM-DD>
```

The date must be strictly before today's UTC date and name a completed daily
session with a valid observation for **every** requested symbol. This conservative
rule avoids treating today's daily bar as complete; exchange-calendar validation
and fresh-data ingestion remain operator responsibilities. Future observations
are excluded before features, and the model's training split must precede or
equal that session. Insufficient history, invalid historical rows, changed Active
identity during inference, bad hashes/schema, unexpected process output or a
wrong database/pending migrations fail with exit 1. Exit 0 prints
`decisions_recorded: N`. Timeouts/cancellation stop the child process; errors do
not echo private stderr or paths. No provider key is required for this local CSV
cycle and it makes no market-data request.

The command invokes the same Python module that can be inspected independently:

```powershell
$env:PYTHONPATH = "$PWD/ml/src"
ml/.venv/Scripts/python.exe -m stocklab_ml.decision_pipeline --registry-path <registry.json> --artifacts-dir <private-artifact-root> --dataset-path <verified.csv> --model-name <chosen-family> --decision-date <YYYY-MM-DD> --symbols AAPL --trusted-artifacts
```

Its stdout is a single schema-v1 JSON batch. Each row contains `symbol`, uppercase
`signal` and a string `confidence` preserving the exact decimal through .NET/SQL
(at most 28 fractional digits). The backend rejects duplicate/unknown fields,
wrong dates/models/symbol sets and oversized outputs before recording anything.
Confidence retains #65's signal-alignment meaning; it is not forecast accuracy.

Stable IDs include the model name/version, symbol and session. Identical reruns
return the original records and timestamps; corrected inputs producing a changed
payload for the same ID conflict rather than silently replacing history. Keep the
original CSV and artifact immutable for retry/audit. Storage commits per decision:
a storage failure or cancellation can leave a valid prefix. Rerun the exact same
inputs to resume; this is not an all-or-nothing batch transaction.

Backend process-contract tests require Python's standard library on PATH, or
`STOCKLAB_TEST_PYTHON` pointing to an explicit executable. They use isolated fake
modules to test timeout, cancellation and output limits, never private artifacts.
Python inference tests exercise actual fitted estimators and verified test-only
artifacts with all network calls blocked.

## Execute explicit decisions (#258)

`--execute-ai-decisions` is a separate trusted operator command. It consumes
already persisted decision IDs and requires no Python/model artifact. It neither
generates decisions nor selects a recent backlog implicitly. Obtain the exact
IDs from the existing authenticated `/api/ai-trader/decisions` read endpoint or a
trusted application caller of #257. The command preserves the supplied order.

First identify the intended database and explicitly apply the normal migrations,
including `AddAiRiskExecutionAttempts`, using the existing procedure in
`backend/README.md`. Initialize the system-owned AI portfolio with #265 if needed.
Neither execution nor normal API startup applies migrations or creates portfolios.

```powershell
dotnet run --project backend/StockLab.Api -- --execute-ai-decisions --AiRiskExecution:ExpectedDatabase=<exact-database-name> --AiRiskExecution:DecisionIds:0=<persisted-decision-guid> --AiRiskExecution:DecisionIds:1=<another-guid>
```

Supply 1..100 distinct nonempty IDs. The database name, connection, migrations,
complete ID set and existing AI portfolio are checked before processing. The
same private provider/risk configuration as the current backend applies. The
pre-approval quote uses the existing execution provider and its USD/symbol/price
validation; the engine fetches execution quotes again and revalidates risk inside
its settlement transaction. It may reduce the approved quantity. This does not
claim a live-market entitlement or add a new freshness policy.

BUY/SELL pass the original symbol, signal and confidence to the risk manager.
HOLD creates no attempt, quote or order. A business refusal is persisted through
the existing rejection history; a successful paper order is persisted through
the existing engine. The decision ID is also the stable order ID for this
orchestration. Terminal decisions are read back without quoting or re-evaluation,
even if a prior process lost its response. The engine's existing serializable
transactions and unique decision links remain the execution/rejection authority.

`AiExecutionAttempts` records STARTED durably before market/risk work, then the
validated requested price/approved quantity before submission. Completion uses
EXECUTED, REJECTED, FAILED or CANCELLED, with controlled codes and an optional
committed trade link. Original model/version metadata is retained through the
decision link. Technical quote/concurrency failures remain retryable FAILED
attempts; they do not invent a permanent ML rejection. A new explicit retry starts
a new attempt and reuses the same order identity. There is no automatic retry loop.

Audit completion and settlement are separate commits. If SQL fails or the process
stops after settlement, an attempt may remain STARTED alongside a real trade or
rejection. Rerunning the same decision reconciles open attempts from that
authoritative result without another transaction. `IsReplay` indicates an existing
or recovered outcome, not proof of which worker originally settled it. An open
attempt with no terminal outcome remains uncertain; the command does not mark a
possibly active worker interrupted. Cancellation makes a bounded independent-token
attempt to save its outcome; if SQL remains unavailable the durable STARTED row
is retained for inspection/recovery.

The command prints safe per-decision outcomes and a final count summary, then
exits without starting HTTP/background workers. Exit 0 includes ordinary risk
refusals/HOLD/replays; exit 1 denotes invalid prerequisites, a technical FAILED
attempt, cancellation or a journal/persistence failure. Earlier valid outcomes
remain if a later decision fails: this is not a batch transaction. Inspect the
journal and retry explicit unresolved IDs. Provider exception text, SQL details
and credentials are neither printed nor stored as failure codes.

Trusted application orchestration can connect both stages without a new endpoint:

```csharp
var decisions = await decisionPipeline.RunAsync(session, cancellationToken);
foreach (var decision in decisions)
    await riskExecution.ExecuteAsync(decision.Id, cancellationToken);
```

This PR does not invoke that sequence on normal startup, schedule it, activate
models or execute anything on a user's account. The journal migration only adds
a table/indexes/non-cascading foreign keys; existing account/trade data is retained.
Tests use isolated databases and explicit Mock/controlled quotes, never a broker.

## Remaining ML → backend orchestration

Issue #257 adds the explicit `--run-ai-decisions` operator cycle documented in
`backend/README.md`. It invokes verified Python inference and records original
versioned decisions, including HOLD, without risk approval or trades. The operator
must select one model family, provide its verified artifact and explicitly trust
the private joblib source. The selected family must have an Active version; the
Archived catalog exports above alone cannot run inference.

Issue #258 adds explicit persisted-decision risk/paper execution with attempt
journaling as described above. Metadata/data synchronization, periodic scheduling
and periodic valuation capture (#259) remain separate operational work.
Normal API startup and the React page never invoke inference/execution. Per-family Active
versions do not choose a global operational model: the command requires that
choice explicitly in private backend configuration.

No automatic orchestration, cloud configuration or #92 work is added by #265.

## Data coverage and refresh

Overview reuses `/performance` for its shared metrics, rather than requesting
another current valuation from `/overview`. The API client also supports and
tests `/overview`. Overview loads its eight independent resources through a
three-request queue; detailed histories request up to 200 rows only when opened.
Trade/decision detail reads use the exact selected persisted ID. Successful
resources are reused within the current session. Refresh reloads the visible
section's resources; Retry reloads one failed source. There is no polling.

History charts filter the latest 200 persisted observations by UTC calendar ranges
relative to the current time, without filling missing dates. ALL means the loaded
sample, not guaranteed lifetime history. Sample coverage and legacy sales with
missing realized costs are displayed. Position returns use exact decimal operands;
trade returns are omitted because sold cost basis is not provided.

## Verification limits

Automated API, hook and rendered-page tests use test-only HTTP projections in
`tests/fixtures/aiTrader.ts`. They do not certify production data availability.
The original #59 browser smoke used fixtures for populated states and real local
ASP.NET for unavailable-catalog responses. #265 separately verifies the configured
LocalDB portfolio and authentic populated catalogs as described above. A real
database-backed login, Azure deployment and current quotes for nonempty positions
still require verification in the intended environment. No production database is
modified by local commissioning.
