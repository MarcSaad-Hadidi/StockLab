# AI Trader: operational prerequisites after #59

The React page reads the eleven JWT-protected GET endpoints from backend #80.
It does not execute trades, initialize a portfolio, capture valuations, train or
promote a model, start a backtest, invoke Python, or access SQL/S3 directly.

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

## Remaining ML → backend orchestration

Metadata synchronization, inference scheduling, recording original model decisions,
risk evaluation, paper execution and periodic valuation capture remain trusted
backend/operator responsibilities. They need a separately specified operational
workflow, ownership, scheduling, deployment configuration and monitoring. Neither
#59 nor #80 establishes an automatic ML → backend pipeline or a running bot.
Per-family `Active` registry versions do not select a global operational model.

No orchestration, cloud configuration or #92 work was started by this integration.

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
Browser smoke tests distinguish fixture-driven populated flows from real local
ASP.NET authentication and unavailable-catalog responses. A real database-backed
login, Azure deployment, populated real catalog and live market-provider flow must
be verified in the intended configured environment. No production database is
modified for frontend verification.
