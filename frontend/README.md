# StockLab frontend

StockLab's React application connects to the ASP.NET Core API for accounts,
personal paper trading, portfolios, transactions, watchlists and AI Trader reads.
It uses React 19, TypeScript 6, Vite 8, i18next and MUI/MUI X Charts. English and
French share the existing language selector and locale-aware formatters.

This document describes the code merged into `develop` as of **31 October 2026**.
An implemented integration still needs a configured backend and its dependencies;
running Vite alone does not provide SQL persistence, external quotes or a bot.
See the [project overview](../README.md), [backend contracts](../backend/README.md)
and [AI Trader operations](AI_TRADER_OPERATIONS.md) for the corresponding setup.

## Local development

Use **Node.js 24.x** and npm, matching CI. Run the following commands from the
repository root:

```sh
npm --prefix frontend ci
npm --prefix frontend run dev -- --host 127.0.0.1
```

Open `http://127.0.0.1:5173/login`, or the port printed by Vite if 5173 is occupied.
The root `/` redirects to `/login`. Register an account, then sign in to load its
private pages.

Start the API in another terminal after completing the
[backend setup](../README.md#api-backend): .NET 10, a private JWT signing key, the
intended SQL Server/Azure SQL connection and explicitly applied EF migrations.
The normal HTTP launch command is:

```sh
dotnet run --project backend/StockLab.Api/StockLab.Api.csproj --launch-profile http
```

Both Vite development and preview proxy `/api` to `http://localhost:5274`.
`VITE_STOCKLAB_API_BASE_URL` optionally selects an absolute API origin; an empty
value uses the proxy. For direct cross-origin access, configure the backend's
allowed CORS origins. This public Vite variable is bundled into the browser:
provider keys, JWT signing keys and database credentials belong only in private
backend configuration. See [configuration and secrets](../README.md#configuration-et-secrets).

The committed backend selects `Mock` market data for offline development. It
still needs SQL for account persistence. External providers require their own
configuration and request budgets; a recently fetched response does not itself
prove that a quote is recent or came from a live provider.

## Navigation and page status

The app has multiple HTML entries and uses full-document navigation between
pages. [src/navigation/routes.ts](src/navigation/routes.ts) is the shared registry
for links and Vite's development/preview URL rewriting. Stock Details uses the
Market document with a symbol query, for example `/market?symbol=AAPL`.

| Route | Access | Delivered behavior |
| --- | --- | --- |
| `/login`, `/register` | Public | API-backed sign-in and account creation; registration returns to Login. |
| `/market` | Public | Search, quotes, OHLCV charts, movers and company information; saving favorites and submitting orders require a session. |
| `/dashboard` | Authenticated | Account valuation, positions, recent transactions, saved watchlist and AI performance summary. |
| `/portfolio` | Authenticated | Persisted cash/holdings enriched with quotes, current valuation and allocation. |
| `/transactions` | Authenticated | Persisted executions with filters, pagination, totals and retry after a load failure. |
| `/watchlist` | Authenticated | Load, add, remove and manually refresh saved symbols and their quotes. |
| `/profile` | Authenticated | Load/save the name and email, display account dates/capital/currency, and change the password. |
| `/ai-trader` | Authenticated | Seven sections backed by the system AI Trader's read APIs. |
| `/alerts` | Authenticated | Existing layout and draft asset search; alert persistence is not connected to this screen yet. |
| `/not-found` | Public | Fallback for unknown page paths. |

Trailing slashes and legacy HTML entry URLs remain supported, including their
query strings. Sidebars mark the active destination with `aria-current="page"`;
Settings opens Profile. The shared mobile/tablet navigation supports keyboard
focus containment, Escape and focus restoration. Language selection persists
across documents.

Vite runs in `mpa` mode: missing static assets remain HTTP 404 rather than receiving
the root document. When deploying outside Vite, map the canonical routes to their
HTML entries and provide the Not Found fallback. The Vite `/api` proxy must also
be replaced by an appropriate deployment API URL or reverse proxy.

## Authentication and account management

[authApi.ts](src/api/authApi.ts) connects Register and Login to
`POST /api/auth/register` and `POST /api/auth/login`. Registration creates the
account and its initial portfolio in the backend; it does not sign the user in.
Validation, duplicate email, invalid credentials, rate limits and service errors
are surfaced through localized messages. Editing the registration email clears
errors belonging to its previous value while preserving other relevant errors.

Login saves the returned Bearer token, expiry and user identity in the
`stocklab-auth` localStorage entry. A storage failure does not report a successful
login. Private clients send that token in the `Authorization` header; the backend
remains responsible for authorization and ownership checks.

[RequireAuth](src/auth/RequireAuth.tsx) wraps Dashboard, Portfolio, Transactions,
Watchlist, Profile, AI Trader and Alerts, including direct and legacy URLs. Missing
or expired sessions hide private content and redirect to Login. Session changes
are observed in the current document and across tabs, with expiry timers and
checks on focus, visibility and page restoration. A 401 for the current request
token clears that session; an old response cannot clear a newer login's token.
Logout removes the local session and navigates to Login. Private loaders cancel
or ignore obsolete requests when the account or token changes.

Profile reads and updates `/api/profile`. Successful name/email edits update the
stored identity used by the top bar; capital and currency come from the portfolio
API. Unsupported profile fields remain unavailable. The password dialog calls
`PUT /api/auth/password`, validates the current/new/confirmation fields and shows
success only after `204 No Content` confirms persistence. It displays backend
errors rather than a simulated success.

There is no refresh-token flow, Google OAuth or working forgot-password service.
Password changes revoke all previously issued JWTs on the server, including the
current one. Its next authenticated request receives a 401 and the existing guard
redirects to Login; sign in with the new password. Local logout clears browser
state and does not independently revoke tokens on the server. After the session
version migration is deployed, existing users must sign in again once.

## Personal paper trading and decimal amounts

The Stock Details ticket prepares and confirms BUY/SELL orders, then submits
`POST /api/portfolio/trades`. It loads the authenticated account's available cash
before the first order and offers retry on a cash-load failure. The backend
validates the quote, currency, cash and holdings, and saves the transaction,
cash balance and position atomically. No real broker order is sent.

Both `market` and `limit` are immediate paper executions. A limit order is rejected
when the current execution quote does not meet its limit; it is not queued as a
standing order. The confirmation is an estimate, since the execution quote can
change before the backend processes the request.

Quantities normalize to eight decimals and execution prices to four; their exact
product retains up to twelve decimals. Personal ledger totals and cash use
`decimal(27,12)` in SQL. Relevant API amounts, prices and quantities stay decimal
strings through validation and monetary calculations in
[decimalMath.ts](src/api/decimalMath.ts), using BigInt rather than converting the
operands to JavaScript numbers first. Transaction totals and confirmations retain
fractional precision; percentages and charts may use approximate numeric values.
Live valuation retains quote precision and rounds each position product to twelve
decimals before summing.

An `orderId` identifies a submission for backend deduplication. In-flight recovery
for the same account preserves that ID, including after token rotation. A lost
response can follow a committed trade: recovery must reuse the original order,
not blindly prepare another one. Definitive backend rejections remain distinct
from an uncertain outcome. This is not a durable pending-order queue across page
reloads. See the [portfolio API contract](../backend/README.md#portfolio-api).

## Portfolio, transactions and Watchlist

Dashboard and Portfolio use `GET /api/portfolio` for stored cash, positions and
acquisition-cost values, then enrich holdings with market quotes. Current total
value is cash plus quoted holdings; total return is measured against initial
capital, including realized gains. Missing or mismatched-currency quotes leave
the affected market values and aggregate valuation unavailable, preserving the
stored accounting data and other available prices.

Quote enrichment currently covers the first **20 positions**, in batches of five.
An account above that cap cannot obtain a complete market valuation by repeatedly
retrying the same request. The UI distinguishes that limit from retryable quote
failures. This is a current valuation, not a persisted personal performance
history: Dashboard and Portfolio time-series charts remain unavailable pending
[#252](https://github.com/MarcSaad-Hadidi/StockLab/issues/252).

Recent activity uses `GET /api/portfolio/transactions`; the Transactions page uses
`GET /api/portfolio/transactions/history` with search, BUY/SELL, date and page
filters. Execution prices come from the ledger, never today's quote. Loading,
confirmed empty results and errors are distinct. Retry after a history failure
keeps the filters/page and reloads the API without reloading the document.
The summary shows trade counts, invested totals and proceeds; its Net P&L field
and the asset-type filter remain unavailable.

Watchlist membership persists through `GET`/`POST /api/watchlist` and
`DELETE /api/watchlist/{symbol}`. The Watchlist page, Market favorites, Stock
Details button and Dashboard summary use this account-owned list. Mutation
success follows the API result; duplicate additions/already removed items are
reconciled with the stored membership. Quote failures do not invent prices or
erase saved symbols. Manual refresh bypasses the frontend quote cache; backend
and provider freshness still apply. There is no frontend polling loop.

## Market data and company information

[marketDataApi.ts](src/api/marketDataApi.ts) calls StockLab endpoints for market
data. The backend's selected provider supplies quote/search/history; Alpha
Vantage supplies enrichment, company news and latest available EOD movers when
configured. The shared StockLogo loads public Elbstream logos directly, with a
ticker fallback and visible attribution; it does not spend Alpha API credits.
Keep that attribution when reusing the component. See the
[backend provider documentation](../backend/README.md#market-enrichment-and-frontend-integration-82).

Market initially requests five curated symbols (AAPL, MSFT, NVDA, AMZN, GOOGL), with
at most three concurrent quote requests. These are identifiers, not seeded prices.
Search waits 450 ms, cancels stale requests and skips empty queries. Search rows
do not fan out into per-result quote or fundamentals requests. Exchange-qualified
symbols remain intact when opening Stock Details. Partial failures preserve
successful data and offer explicit retry where applicable.

Stock Details loads quotes and OHLCV history independently. Its Overview,
Financials, News, Key Metrics, Forecast and Insights tabs load enrichment on
demand. Financials uses reported provider figures; Forecast shows external
analyst targets/ratings, and Insights summarizes observed data deterministically.
These are not StockLab model predictions. News displays relevant, deduplicated
HTTPS publisher links and timestamps, not reproduced article bodies.

Charts use returned bars rather than generated points. The 1D window uses Minute
bars over 24 hours ending one minute after the last quote observation, so its bar
is included; 5D uses Hour bars. Month/year ranges clamp dates at month ends, YTD
begins January 1, and 5Y uses Week bars. MAX uses daily bars over the backend's
bounded 4,999-day window, not an unlimited history. Calendar bars preserve
`periodDate`; intraday labels show UTC. Successful history responses have a
bounded five-minute memory cache. Invalid bars or provider failures are not
repaired with fabricated prices.

## AI Trader reads

Overview, Positions, Trades, Decisions, Performance, Model and Backtests read
validated, JWT-protected `/api/ai-trader/*` responses through
[aiTraderApi.ts](src/api/aiTraderApi.ts). The AI portfolio is a separate system
portfolio, shared among authenticated readers; it is not the user's personal
portfolio. Its settlement rules are documented separately in the backend.

The UI loads resources needed by the selected section, supports manual refresh
and per-resource retry, and distinguishes empty, unavailable and failed responses.
Decision/trade detail reads retain model/version and risk outcomes. History views
are bounded to the observations loaded, at most 200, rather than promising an
entire historical series. The Dashboard AI summary reads actual performance and
refreshes when the page becomes active again; a nullable win rate stays
unavailable when it cannot be calculated.

These frontend pages do not start training, inference or order execution. Backend
commands for decisions, risk/execution and periodic performance capture are
available, but require their own initialization, data, model/catalog and operator
configuration. An empty portfolio or HOLD/rejection is not evidence of a broken
UI. Follow [AI_TRADER_OPERATIONS.md](AI_TRADER_OPERATIONS.md) for prerequisites and
operational verification; Vite startup does not commission an autonomous bot.

## Remaining integrations

- Alerts has draft asset search and an existing layout, but its save action is
  disabled until [#57](https://github.com/MarcSaad-Hadidi/StockLab/issues/57)
  connects it to the backend. Backend alert CRUD and periodic monitoring exist;
  persistent triggering remains [#43](https://github.com/MarcSaad-Hadidi/StockLab/issues/43).
  The UI's email/push options do not provide notification delivery.
- Personal performance history (#252), Google
  OAuth and password recovery remain incomplete as described above.
- Consistent visible Mock-provider provenance and quote-observation timestamps,
  notably in Watchlist, remain [#254](https://github.com/MarcSaad-Hadidi/StockLab/issues/254).
- Controls with no connected service, including unsupported profile fields and
  exports, remain disabled or explicitly unavailable. Market currently enables
  All/Stocks filters; the other asset filters remain disabled. See the
  [project roadmap](../README.md#limites-et-feuille-de-route) for cloud work.

Account data is persisted by the backend, not synthesized from market data or
frontend seed arrays. Test fixtures and the explicitly configured Mock market
provider are development tools; they do not establish live-provider or deployment
availability. API failures must not become simulated success or fake history.

## Validation and production preview

From the repository root:

```sh
npm --prefix frontend run lint
npm --prefix frontend run build
npm --prefix frontend test
npm --prefix frontend run preview -- --host 127.0.0.1
```

`npm --prefix frontend test` uses Node's test runner, tsx and jsdom. Coverage
includes API contracts, rendered forms/pages, sessions and account isolation,
decimal calculations, navigation and keyboard behavior, plus real HTTP route
checks against Vite development and built preview. Those tests use controlled
fixtures/servers; they do not validate Azure availability or authenticate external
market providers. The [CI workflow](../.github/workflows/ci.yml) also installs with
`npm ci` and runs `npm audit --audit-level=high` before lint/build/tests.

The standard build writes every page to `frontend/dist`. The legacy Dashboard
command uses the same multi-page configuration, writing to `dist-dashboard`:

```sh
npm --prefix frontend run build:dashboard
npm --prefix frontend run preview -- --config vite.dashboard.config.ts --host 127.0.0.1
```

Pages stay in their own folders under `src/`; shared UI is in `src/components`,
typed clients/decimal helpers in `src/api`, session management in `src/auth`,
and EN/FR translations and formatters in `src/i18n`. Reuse these existing modules
when extending an integration.
