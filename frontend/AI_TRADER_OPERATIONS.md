# AI Trader: operational prerequisites after #59

The React page reads the eleven JWT-protected GET endpoints from backend #80.
Backend #259 adds two read-only cycle/context routes for trusted consumers; the
current React page continues using its existing performance/history contracts.
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

## Run the full local paper cycle (#273)

The local operator runner composes the existing #257 decision pipeline, #258
risk/paper execution and an idempotent valuation capture. Normal HTTP startup
does not launch it. It never submits an order to a real broker or changes a
user portfolio. One loop owns its passes sequentially; all invocations for the
same local portfolio must use the same private state directory.

1. Keep the intended SQL database and its history. Apply reviewed migrations
   explicitly if needed, initialize the system paper portfolio through the
   existing setup command, and configure `MarketData:Provider=TwelveData`.
2. Prepare a fitted artifact with the existing #76 APIs. Use the audited #78
   evaluation/application workflow to promote a selected version. Retain the
   validation/test, calibration, backtest and benchmark evidence privately.
   The runner requires the exact Active version; it never tunes thresholds,
   trains, promotes, or falls back to a different model. A policy-approved
   historical evaluation is not prospective evidence of profitability.
3. Build the operator backend, using an isolated output if a server is running:

   ```powershell
   dotnet build backend/StockLab.Api --artifacts-path .tmp/ai-trader-build
   ```

4. Copy `ml/examples/ai_trader_cycle.config.example.json` into an ignored private
   location and replace every example path/database value. Paths must be
   absolute; environment-variable placeholders are not expanded. Keep inputs,
   state, registry and artifacts separate. The initial universe is limited to
   1–10 USD US-equity symbols on the supported exchange calendar. The history
   start must preserve the fitted feature history. Set `trusted_artifacts=true`
   only after verifying ownership and provenance of that fitted joblib.
5. Configure the dedicated `TWELVE_DATA_ML_API_KEY`. Sharing the configured website
   credential is an explicit alternative: set `share_website_quota=true` and add
   `website_secrets_path` with the absolute path of the existing Development
   User Secrets JSON. This reuses only its configured Website/Fallback selector,
   with no credential failover. It shares that credential's quota. Keys stay out
   of the cycle config, command arguments and logs. The ML credential is removed
   from the child environment; the backend resolves its quote credential through
   its existing configuration.
6. From the repository root, run one pass and inspect its actual outcomes:

   ```powershell
   $env:PYTHONPATH = "$PWD/ml/src"
   ml/.venv/Scripts/python.exe -m stocklab_ml.operational_cycle --config <private-config.json> --once
   ```

The runner first checks the exact database, migrations, initialized portfolio,
real provider, active model and effective risk policy. Confidence below 70% or
short-selling configuration blocks this cycle. It then fetches daily
split-adjusted inputs using the existing ingestion pipeline. The inclusive end
is sent as `T23:59:59`, so the requested closed session is not lost at midnight
([Twelve Data's date/time selection contract](https://twelvedata.com/docs)).

Only a session before today's UTC date is eligible. The regular US-equity
calendar currently covers 2026–2028, including observed holidays and the 2028
Saturday New Year's exception, using the
[published exchange calendar](https://www.nyse.com/trade/hours-calendars).
An unknown year or a missing expected bar blocks the cycle. Exceptional closures
are not guessed: absent data blocks until explicitly reviewed. Partial attempts
remain private; only complete, validated, hashed snapshots are published. A
same-session retry verifies and reuses that immutable snapshot without another
historical request. Execution and valuation reject future quotes and source
observations older than the required session. Prices are the provider's latest
available observations with their original timestamps, not a promise of live
executable prices.

`COMPLETED` means decisions and terminal outcomes were persisted and valuation
captured. `executed`, `rejected` and `hold` count the actual outcomes; a successful
pass may contain zero trades. HOLD confidence describes neutrality, not expected
profit. The existing risk manager and settlement engine retain sizing, cash,
exposure and no-short safeguards. Opening AI Trader and selecting **Refresh**
reads these persisted decisions, orders, holdings and performance from the real
authenticated backend.

After verifying the manual pass, start sequential operation with the same config:

```powershell
ml/.venv/Scripts/python.exe -m stocklab_ml.operational_cycle --config <private-config.json> --loop
```

`interval_seconds` must be 60–86400; the example checks every 30 minutes. A
completed session is replayed without inference, quotes, orders or new captures.
To request a clean stop after the active pass, use a second terminal:

```powershell
ml/.venv/Scripts/python.exe -m stocklab_ml.operational_cycle --config <private-config.json> --stop
```

The next explicit `--loop` clears the stop request and resumes the same journals.
Do not delete lock files: ownership belongs to the OS and is released on exit.
An interrupted trade reuses its original decision/order IDs. An interrupted
capture reuses its saved capture ID without revaluation. Before advancing to a
new session, the backend reconciles unfinished older journals using the original
persisted outcomes and capture identity. Remaining stale unresolved signals are
marked `ABANDONED` in that journal and are never submitted. Capture reconciliation
uses current quotes and records its real capture time; it does not fabricate a
historical valuation. Failed reconciliation blocks new inference.

The private state directory contains bounded atomic `status.json` (last attempt,
stage and last success) and `session-<date>.json` checkpoints. CLI errors expose
only fixed status/stage codes; raw child output, private paths, inputs and model
audit evidence are not published. A changed model/universe/input/journal blocks
instead of silently rewriting a session. Preserve evidence and resolve the
specific configuration/data issue before retrying. Do not run legacy manual
execution commands concurrently with this managed cycle.

## Periodic performance cycle (#259)

Apply `AddAiPerformanceCycles` explicitly to the intended database. It adds
nullable cycle/context columns, a consistency constraint and a unique filtered
portfolio/UTC-slot index to the existing `AiPortfolioSnapshots` table. Legacy and
manual observations retain null cycle metadata; no account, trade or observation
is replaced. The existing manual capture API still revalues and rejects changed
facts when a capture ID is reused.

Run one current-slot capture after paper execution, without starting HTTP/workers:

```powershell
dotnet run --project backend/StockLab.Api -- --capture-ai-performance --AiPerformanceCycle:ExpectedDatabase=<exact-database-name>
```

The command validates the exact target, connection, current migrations and an
existing system AI portfolio. It never initializes/migrates, infers or trades.
Mock valuations are permitted only in Development. Failures/cancellation return
exit 1 with safe output; success prints capture ID, slot and replay status.

For periodic observation, configure private backend settings before normal startup:

```json
{
  "AiPerformanceCycle": {
    "Enabled": true,
    "Interval": "01:00:00",
    "ExpectedDatabase": "<exact-database-name>"
  }
}
```

The shipped default is disabled. The interval must be 1 minute..1 day (default
one hour). The worker waits for its first tick, owns a fresh scope per cycle and
never overlaps its own runs; shutdown cancels waiting/in-flight work. Each cycle
uses the current UTC time floored to the configured interval. Multiple workers,
restarts and one-shot commands share that slot identity. A committed slot returns
the original capture without any new quote, even after prices/positions change.
Keep the interval stable for comparable coverage. Missed slots are not fabricated
from today's prices; downtime/provider failures leave real gaps in history.

New captures reuse the existing quote validation and serializable portfolio
verification. Valuation and the cumulative context of committed AI trades are
saved in one transaction. Each trade retains its ID, original nullable decision
and model/version, executed quantity/price, time and nullable realized P&L. Legacy
unknowns remain unknown. Total portfolio return combines all strategies; these
links do not prove per-model causality or create training labels automatically.
Technical failure leaves no partial capture/context and can be retried. A losing
SQL worker may return a controlled concurrency conflict before the winner is
visible; rerunning the slot reads the committed result. Worker
warnings omit exception details; keep normal provider/server logs private.

Existing `/performance` and `/performance/history` expose the resulting metrics
and chart samples unchanged. New authenticated, quote-free reads are:

- `GET /api/ai-trader/performance/cycles?limit=20` (1..200, newest slot first).
- `GET /api/ai-trader/performance/cycles/{captureId}?offset=0&limit=100`
  (1..200 trades per page, with `totalTradeCount`; unknown capture: 404).

Monetary/quantity response values are invariant decimal strings. The captured
context is available to trusted exporters for future backtesting/retraining.
Its cumulative JSON storage grows with trades × captures; normalize provenance
storage before operating at a materially larger history/retention scale. Paging
bounds the response, not the cost of loading that one snapshot's context.

Implementation tests use owned SQLite/LocalDB and controlled prices. They do not
enable this worker or apply migrations to the user's preview/production database.

## Remaining ML → backend orchestration

Issue #257 adds the explicit `--run-ai-decisions` operator cycle documented in
`backend/README.md`. It invokes verified Python inference and records original
versioned decisions, including HOLD, without risk approval or trades. The operator
must select one model family, provide its verified artifact and explicitly trust
the private joblib source. The selected family must have an Active version; the
Archived catalog exports above alone cannot run inference.

Issue #251 connects the Dashboard summary to the same validated `/performance`
read as the dedicated AI Trader page. It loads only that resource, displays actual
aggregate return/P&L/win rate/executed count and the UTC valuation timestamp, and
refreshes manually or on focus/visible return/persisted pageshow. Loading, a missing
portfolio, provider errors, nullable metrics and a genuine zero-trade portfolio
remain distinct. It does not poll, fetch model catalogs or imply the bot is running.
The former unconnected seven-day sparkline is replaced by the actual trade count;
no historical series is invented. The full chart remains on the AI Trader page.

Issue #258 adds explicit persisted-decision risk/paper execution with attempt
journaling as described above. Issue #259 adds the opt-in performance worker and
one-shot capture above. The local functional building blocks are now present;
operational completion still requires:

1. Select/promote a verified fitted model, configure its trusted artifact and
   input datasets, synchronize private metadata exports and apply migrations to
   the intended database. Historical catalog entries alone cannot run inference.
2. Commission and verify the complete #257 → #258 → #259 sequence with actual
   intended data/providers. The #259 worker only samples performance; schedule
   inference/order orchestration separately if unattended trading simulation is
   wanted. Retraining/promotion must retain the existing evaluation safeguards.
3. Deploy asynchronous/cloud jobs if required (#50 SQS / #52 Lambda). These are
   distinct from a working local paper-trading cycle.
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
