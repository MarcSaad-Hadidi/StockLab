# StockLab backend

The backend targets .NET 10. Install the .NET 10 SDK before building locally.

## Architecture

```text
backend/
|-- StockLab.sln
|-- StockLab.Api/
|   |-- Controllers/
|   |-- DTOs/
|   `-- Properties/
|-- StockLab.Application/
|   |-- Interfaces/
|   |-- Services/
|   `-- DTOs/
|-- StockLab.Domain/
|   |-- Entities/
|   |-- Enums/
|   |-- ValueObjects/
|   `-- Exceptions/
|-- StockLab.Infrastructure/
|   |-- Persistence/
|   |-- Identity/
|   |-- MarketData/
|   |-- Azure/
|   `-- AWS/
|-- StockLab.UnitTests/
|   `-- MarketData/
`-- README.md
```

| Project | Responsibility | Direct project references |
| --- | --- | --- |
| StockLab.Domain | Business entities, enums, value objects, exceptions and domain rules. Independent of ASP.NET Core and external providers. | None |
| StockLab.Application | Use cases, services, application DTOs and interfaces implemented by infrastructure. | Domain |
| StockLab.Infrastructure | EF Core persistence, market-data and future cloud implementations behind application interfaces. | Application, Domain |
| StockLab.Api | Controllers, HTTP configuration, OpenAPI, CORS, health checks and dependency injection composition root. | Application, Infrastructure |

Domain is the innermost layer. Application depends on Domain, and Infrastructure
depends on Application and Domain. The API references Application and
Infrastructure to compose the application. Domain and Application must not depend
on the API or Infrastructure. Controllers use application contracts rather than
concrete provider implementations.

`Program.cs` is the composition root and registers the available ASP.NET Core
services. Register future application interfaces and implementations here as their
issues introduce them. Application defines the market-data, registration, login
and profile contracts, plus the portfolio read contract;
Infrastructure implements these services and the EF Core SQL Server mapping. Registration atomically creates an account and its default
USD
paper-trading portfolio. Login verifies the stored password hash and returns a
stateless JWT access token.

## Entity Framework Core

`StockLab.Infrastructure/Persistence/StockLabDbContext.cs` owns the six currently
supported entities: User, Portfolio, Holding, Transaction, Watchlist and
PriceAlert. Their business properties and navigations live in
`StockLab.Domain/Entities`; no EF Core annotations or dependencies enter Domain.
Each SQL Server mapping is in `StockLab.Infrastructure/Persistence/Configurations`.
The context discovers these configurations from the Infrastructure assembly. They
define table names, keys, indexes, decimal and string types, check constraints,
`rowversion` concurrency tokens and `NO ACTION` foreign keys according to
`database-schema.md`. AI Trader has its own tables described below; valuation snapshots are not stored.

`StockLab.Api/Program.cs` calls `AddPersistence`, which registers the scoped
`StockLabDbContext` with the SQL Server provider. It reads the named setting
`ConnectionStrings:StockLab` when a context is resolved. To configure it locally,
use User Secrets with the API project (substitute your private connection string):

```powershell
dotnet user-secrets set "ConnectionStrings:StockLab" "<AZURE_SQL_CONNECTION_STRING>" --project backend/StockLab.Api/StockLab.Api.csproj
```

The environment variable `ConnectionStrings__StockLab` is an alternative. Neither
the connection string nor database credentials belong in tracked appsettings or Git.
The API's market-data endpoints can still start without a database connection; a
context requires the named setting when used. The model uses UTC `DateTime` values;
application services must supply UTC timestamps and normalized uppercase symbols.

Issue #22 configured the EF Core model. Issue #23 adds the `InitialCreate`
migration. Do not call `EnsureCreated`, `EnsureDeleted` or `Migrate` at API startup;
migrations remain an explicit operator action.
The persistence tests inspect the SQL Server model and run EF add/read/update against
an isolated SQLite database. A separate read-only `CanConnectAsync`/open-connection
smoke check may use the configured Azure SQL setting; normal tests need no cloud
credentials and never create Azure tables.

### Managing migrations

The repository pins `dotnet-ef` in its local tool manifest. From the repository root,
restore the tool and specify both the project containing the context and the API
startup project:

```powershell
dotnet tool restore

dotnet ef migrations list `
  --project backend/StockLab.Infrastructure/StockLab.Infrastructure.csproj `
  --startup-project backend/StockLab.Api/StockLab.Api.csproj

dotnet ef database update `
  --project backend/StockLab.Infrastructure/StockLab.Infrastructure.csproj `
  --startup-project backend/StockLab.Api/StockLab.Api.csproj

dotnet ef migrations script `
  --project backend/StockLab.Infrastructure/StockLab.Infrastructure.csproj `
  --startup-project backend/StockLab.Api/StockLab.Api.csproj
```

`database update` uses `ConnectionStrings:StockLab` from User Secrets or
`ConnectionStrings__StockLab`. Review generated SQL before applying it, and use the
StockLab development database for development migration work. No database update
runs automatically when the API starts.

### AI Trader portfolio (#66)

`IAiTraderPortfolioService` initializes and reads the system-owned `AI_TRADER`
portfolio in `AiPortfolios` and its positions in `AiPositions`. These
entities have no user relationship and never reuse user portfolios or holdings.
The first call creates **100000 USD cash**, with no positions. Subsequent calls,
including after a restart, preserve cash and positions. A unique portfolio key
and recovery limited to SQL Server duplicate-key errors protect concurrent creation.
The mappings follow `database-schema.md`: application `PortfolioKey` maps to the
unique, nonblank `Name` column (`nvarchar(100)`), and position `AiTraderPortfolioId`
maps to the `AiPortfolioId` foreign key column.
Each operation owns its EF context; it cannot save pending edits from other services.

`GetStateAsync` reads persisted cash, quantity and average cost without market data.
`GetSnapshotAsync` uses the existing `IMarketDataProvider` pipeline to calculate
position market value (`quantity * current price`), total value (`cash + market
values`) and P&L (`total value - initial capital`). Position unrealized P&L is
`quantity * (current price - average cost)`. No price or valuation is persisted.
Missing/invalid quotes, provider failures and non-USD quotes fail valuation without
substituting cost or zero; persisted state remains available. Empty portfolios
require no quote calls. Quote prices are the provider's latest available prices,
not guaranteed executable prices or observations from the same instant.

The `AddAiTraderPortfolio` migration adds only these two tables. It enforces
nonnegative cash, fixed V1 capital of 100000 USD, positive quantity/average cost,
one position per portfolio/symbol and portfolio `rowversion`. Money uses
`decimal(19,4)` and quantities `decimal(19,8)`. Apply migrations explicitly using
the commands above. No startup migration, API endpoint or trading
is included. The API only registers the scoped service through Infrastructure DI.

### AI Trader risk manager (#67)

`IAiRiskManager.EvaluateAsync(AiRiskRequest, CancellationToken)` evaluates an
already-produced ML decision; it never generates signals, recalculates confidence,
executes trades or writes decision history. `AiTradingSignal` is a controlled
`Buy`/`Sell`/`Hold` enum; unknown enum values are rejected. Future API boundaries
must explicitly map external BUY/SELL/HOLD strings. Symbols are required, at most
32 characters, without whitespace/control characters; matching is ordinal and
case-insensitive, while results preserve the original symbol, signal, confidence
and price. Confidence is a decimal fraction in [0,1], never a percentage.
`CurrentPrice` must be positive and resolved **in USD by trusted backend
orchestration**, never accepted directly from a frontend. No UserId, model name,
ML probabilities, Python process or HTTP endpoint is needed.

Policy is bound from `AiTrader:Risk` and validated at startup:

| Setting | V1 default |
| --- | --- |
| MinimumConfidence | 0.70 (inclusive, BUY and SELL) |
| MaxPositionExposurePercent | 0.20 of current total portfolio value |
| MaxOpenPositions | 10 positive-quantity positions |
| MaxCashAllocationPerTradePercent | 0.10 of current total portfolio value |
| AllowShortSelling | false (true is rejected as unsupported) |

Exposure/allocation must be in (0,1], allocation cannot exceed exposure, position
count must be positive, and minimum confidence must be in [0,1].

Checks run in this order: request/symbol/signal/confidence validation, positive
price, HOLD, minimum confidence, portfolio read and currency, then BUY position
count, remaining exposure, cash and sizing; SELL checks the actual holding.
HOLD always returns non-executable `HoldSignal` for valid input, even below the
confidence threshold. Rejections have zero quantity and a typed reason;
approvals have positive quantity and no rejection reason.

BUY takes one current snapshot. Target exposure is `held quantity * CurrentPrice`,
never average cost. If the snapshot's target quote differs, its target market
value is replaced in TotalValue with that same request-price exposure so both
sides of the exposure ratio use one price. Remaining exposure is
`TotalValue * MaxPositionExposurePercent - target exposure`. Maximum notional is
`min(CashBalance, remaining exposure, TotalValue * MaxCashAllocationPerTradePercent)`.
Thus an empty 100000 USD portfolio at a 100 USD price approves 100 shares
(10000 USD), while the symbol ceiling remains 20000 USD. Gains/losses change
these limits with current account value, not initial capital. At ten positions,
new symbols are rejected; additions to an existing symbol still undergo sizing.
Quantity is divided by price, floored to eight decimal places using decimal
arithmetic, and its final cost checked against all three limits. Zero quantity
is `TradeTooSmall`; there is no arbitrary minimum notional or cash reserve.
BUY also caps additions at the remaining `decimal(19,8)` position capacity
(`99999999999.99999999 - held quantity`) so an approval fits the existing schema.

SELL reads persisted state without quotes and approves the entire held quantity;
no holding means `NoPositionToSell`. It cannot exceed holdings and ignores BUY
exposure, cash allocation and position-count limits. HOLD and low confidence need
no portfolio or market calls. Provider/currency valuation failures propagate
without fallback; arithmetic overflow raises a controlled evaluation error.

Risk evaluation uses `IAiTraderPortfolioService` with `initializeIfMissing: false`
for both reads. A missing AI portfolio fails without creating it; initialization
remains the responsibility of #66's explicit orchestration. Existing callers of
the portfolio service retain first-use initialization by default. No user
portfolio is read, no cash/position/transaction/timestamp is changed, and no
execution service is a dependency.

**Approval is pre-execution only, not a reservation of cash or shares. #68 must
revalidate critical cash, holdings, exposure and position-count invariants
atomically against the execution price/state under concurrency control.** #67
holds no lock or transaction between evaluation and execution.

Tests use isolated SQLite databases plus the SQL Server model and migration
metadata. To also test eight simultaneous creators and real SQL Server rowversion
on Windows with LocalDB installed:

```powershell
$env:STOCKLAB_TEST_LOCALDB = "1"
dotnet test backend/StockLab.sln
```

That opt-in test creates and removes only its own randomly named LocalDB database;
it never reads the application's Azure connection. Otherwise it is reported skipped.

### AI paper trade execution (#68)

`IAiTradeExecutionService.ExecuteAsync(AiTradeExecutionRequest, CancellationToken)`
is implemented by the scoped `AiPaperTradingEngine`. Supply a non-empty,
caller-generated `OrderId`, the `DecisionId` of an already recorded ML decision,
and an approved `AiRiskDecision` with BUY/SELL, positive
quantity and no rejection reason. HOLD and malformed/rejected approvals never
execute. Initialize the AI portfolio separately: execution never creates capital.

The engine resolves a new USD quote through the existing `IMarketDataProvider`
pipeline keyed `Execution`: rate limiting and in-flight deduplication remain,
but the website's completed-quote cache is bypassed. BUY also quotes other held
symbols for valuation; SELL needs only the target quote. Missing, invalid,
wrong-symbol or non-USD quotes fail without mutation or a risk-price fallback.
This uses the provider's latest available observation, including outside market
hours; it does not invent a live exchange price.

All quote calls finish before a `Serializable` transaction loads tracked state.
The engine compares the portfolio rowversion, balances and positions against the
state used for valuation; any intervening change returns `ConcurrencyConflict`.
`AiRiskPolicyEvaluator` shares #67's existing rules with execution. Current
confidence, cash, position count, exposure, allocation and storage limits apply.
BUY may shrink to the current safe quantity, but never increases beyond the
original approval. SELL fails if the original approved quantity exceeds current
holdings; a partial sale is supported and retains the remaining average cost.
An approval contains no historical state token, so changes before execution's
initial read are handled by current-state revalidation, not historical matching.

Quantities are floored to eight decimals. Prices, totals and weighted average
costs use four decimals with `MidpointRounding.AwayFromZero`. BUY average cost is
`(old quantity * old cost + executed quantity * execution price) / new quantity`.
The rounded debit is checked again against cash, allocation and resulting
exposure (including rounding-induced breaches in other compliant holdings).
If settlement cannot satisfy the limits, execution fails without mutation;
neither negative cash nor short positions are allowed. SELL removes a fully
closed position. One UTC `TimeProvider` timestamp updates portfolio, position
and trade consistently.

Cash, position changes and the `AiTrade` insert commit together. SQL deadlocks,
rowversion conflicts and unique-order races produce a committed replay or a
controlled conflict; the caller may re-evaluate/retry. Other database failures
return `PersistenceFailure`, without SQL details. No internal retry blindly
executes a stale order, and cancellation propagates with rollback.

`AddAiTrades` creates only `AiTrades`, its positive-value/side constraints, a
NoAction FK to `AiPortfolios`, and unique `(AiTraderPortfolioId, OrderId)` index.
The trade stores a SHA-256 fingerprint of the decision ID, original normalized symbol, side,
confidence, risk price and approved quantity. Repeating the same approval returns
the original committed trade and post-trade balances (`IsIdempotentReplay=true`),
even if later trades or quotes changed. A changed approval using that OrderId
returns `DuplicateOrder`; a retry never fetches a new price for a committed order.
The stored execution quantity/price may differ from the original approval.

No user portfolio/holding/transaction is reused, and the user paper engine is
unchanged. There is no real broker, controller, scheduler, ML change or frontend.
AI API endpoints (#80) remain separate work.
Tests use fake quotes and isolated SQLite/LocalDB databases. The same
`STOCKLAB_TEST_LOCALDB=1` switch runs real simultaneous orders and rollback tests;
they never use Azure credentials or external market APIs.

## AI executed trade history (#72)

`AiTrades` is the source of committed AI paper trades, including BUY and SELL
after a position closes. The scoped `IAiTradesHistoryService` exposes
`GetByIdAsync` (unknown ID returns null; empty ID is invalid) and
`GetRecentAsync` (limit 1..200), ordered by `ExecutedAtUtc DESC, Id DESC`.
The `(ExecutedAtUtc, Id)` index supports this ordering through a backward scan.
Each read uses one projected, untracked SQL query with a LEFT JOIN to
`AiDecisions`. It never creates a portfolio, saves changes, reads user trading
tables, fetches market data or calculates current P&L.

`AiTradeHistoryItem` returns trade/order/decision IDs, side, symbol, actual
executed quantity, persisted execution price and total, and execution time.
Its optional `AiTradeDecisionSummary` contains the exact original decision ID,
signal, decimal confidence, decision date, model name and model version.
Historical execution prices and model identities remain unchanged by current
quotes or model versions. Database failures use a safe `PersistenceFailure`;
cancellation propagates.

New execution requests require a nonempty `DecisionId`. Record the raw ML
decision through #69 first, evaluate risk through #67, then execute the approved
order. Execution verifies the stored decision exists, matches the raw risk
symbol ordinally, signal and confidence exactly, is BUY/SELL, and has no #70
rejection. It checks again within the serializable transaction after market I/O;
the link, trade, cash and position changes commit atomically. It never creates,
updates or deletes raw decisions or rejection records.

The decision ID participates in the canonical order fingerprint. Exact retries
return the original linked trade without another quote. Reusing an order ID with
another decision returns `DuplicateOrder`; another order for an executed decision
returns `DecisionAlreadyExecuted`. The filtered unique index independently
enforces at most one trade for a non-null decision, including concurrent requests.

`LinkAiTradesToDecisions` adds nullable `AiDecisionId`, a NoAction FK to
`AiDecisions.Id`, and unique `IX_AiTrades_AiDecisionId` filtered by
`[AiDecisionId] IS NOT NULL`. Existing trades remain visible with null decision
ID/summary; no association is guessed or backfilled. Null is solely legacy
compatibility: the new execution contract always commits a real decision ID.
The migration preserves existing trade constraints and order uniqueness; its
Down removes only the link column, FK and index. No new trade table or endpoint
is introduced; #80 will expose these contracts later.

Tests cover fractional execution facts, BUY/SELL and closed positions, legacy
rows, bounded deterministic queries, no N+1/tracking/writes/market calls, user
isolation, decision validation and retries. `STOCKLAB_TEST_LOCALDB=1` additionally
tests real SQL query shape, FK/unique/delete restrictions, migration upgrade and
Down preserving old trades, and concurrent executions for one decision.
`IndexAiTradeHistory` adds only that recent-history index; Down removes only it.
Rejection recording also checks for an executed trade within a serializable
transaction. Rejection and execution cannot both commit for the same decision;
a late rejection returns `DecisionAlreadyExecuted`, and an overlapping SQL
deadlock returns a safe `ConcurrencyConflict` after rollback.

## AI current positions (#71)

`IAiCurrentPositionsService.GetCurrentAsync` returns a read-only list of open AI
positions in ordinal ascending symbol order. `AiPositions` is the current state
maintained transactionally by actual #68 paper executions; `AiTrades` is audit
history and is not replayed. Raw ML, rejected and HOLD decisions do not create
positions, and user portfolios/holdings are never read. Full SELL removes the
position; partial SELL preserves the remaining quantity and average cost.

`AiCurrentPosition` contains only `Symbol`, decimal `Quantity`, `AveragePrice`,
`CurrentPrice`, `MarketValue` and `PnL`. Average price maps the persisted
`AverageCost`; fractional quantities retain their eight decimal places. The
service maps #66's `GetSnapshotAsync(initializeIfMissing: false)` without new
quote calls or valuation formulas. That existing valuation uses `IMarketDataProvider`:
cost basis is quantity × average cost, market value is quantity × current price,
and P&L is market value − cost basis, equivalently quantity × (current price −
average price). Position P&L is **unrealized monetary P&L** in portfolio currency
(USD in V1), retains positive/negative/zero values and excludes realized gains.
No percentage is added.

A missing portfolio fails with the existing `InvalidOperationException` and is
never initialized by this read. An existing empty portfolio returns `[]` with
zero quote calls; otherwise #66 requests one quote per open position through the
existing provider cache/rate-limit pipeline. Missing, nonpositive or
currency-mismatched quotes fail the whole read; provider errors and cancellation
propagate. There is no fake/stale fallback, partial result or implicit FX.

No saves, timestamps, cash, position/history mutations or schema changes occur.
Current price, market value and P&L remain dynamic and are not persisted.
The service is registered scoped in API composition. No controller is introduced;
#80 will expose this contract for the future #59 frontend. Tests use fake quotes,
SQLite and isolated LocalDB databases, including actual executed BUY/additional
BUY/partial SELL/full SELL, rollback visibility and unchanged SQL rowversions.

## AI decision history

`IAiDecisionHistoryService` records the raw ML output in append-only `AiDecisions`,
including every valid BUY, SELL and HOLD, independently of confidence thresholds,
risk approval, portfolio existence and execution. It has no market, ML, risk or
execution dependency. Recording changes only this table; no controller or scheduler
is introduced. A future orchestrator must call it before risk evaluation.

The caller supplies a nonempty stable `DecisionId`, symbol, existing `AiTradingSignal`,
confidence, logical daily `DateOnly`, model name and explicit opaque model version.
Symbol validation matches AI risk: one nonblank identifier, at most 32 characters,
without whitespace, control characters or commas; the accepted symbol is preserved.
Model name and version are trimmed, nonblank and at most 128 characters, with their
casing preserved. No version is generated or substituted. Confidence is in `[0,1]`
and uses `decimal(29,28)` to preserve every .NET decimal exactly, including the
current ML pipeline's unrounded scores. The default date and dates after today's UTC date
are rejected. `RecordedAtUtc` comes from backend `TimeProvider`, retains UTC kind on
reads and is distinct from the logical SQL `date`.

An identical canonical payload with the same ID returns the original DTO and
timestamp. Any changed field, including model/version casing, returns the typed
`AiDecisionHistoryFailure.DecisionConflict`; history is never overwritten.
The caller-generated primary key arbitrates concurrent inserts. Only SQL Server
duplicate-key errors 2601/2627 trigger a reload and exact payload comparison; other
database failures return `PersistenceFailure` without SQL details. Each operation
owns its EF context, so recording cannot save unrelated scoped edits.

`GetByIdAsync` returns a detached record or null. `GetRecentAsync` accepts limits
1..200 and orders in the database by `DecisionDate DESC, RecordedAtUtc DESC, Id DESC`
before applying `Take`; both reads use `AsNoTracking`. The single composite history
index follows that ordering. `AddAiDecisions` creates only this table, its required
identity/signal/confidence checks and history index; Down drops only `AiDecisions`.
The SQL Server signal check uses binary collation and exact byte lengths so lowercase
or padded values cannot pass the constraint and later break the canonical reader.
The history service is registered scoped in the API composition root.

Risk rejection records (#70) and executed trades (#72) reference the stable decision
ID. Actual model version lifecycle belongs to #75; this issue stores
only the explicit version supplied by the caller. There is no portfolio/model FK,
rejection reason, execution data, probability field or public update/delete API.
SQLite tests cover validation, all signals, retries, conflicts, UTC timestamps,
safe errors and isolation. With `STOCKLAB_TEST_LOCALDB=1`, SQL Server tests additionally
force concurrent PK races and verify exact precision, bounded SQL queries, ordering
ties and database constraints in isolated randomly named LocalDB databases.

## AI rejected decision history

`IAiRejectedDecisionHistoryService` records an already evaluated risk rejection for
one existing `AiDecisions.Id`. The append-only `AiRejectedDecisions` table contains
only `AiDecisionId` (PK and FK), `RejectionReason` and backend `RejectedAtUtc`.
The FK uses NO ACTION; an original decision with a rejection cannot be deleted.
`AddAiRejectedDecisions` creates only this table, its constraints and one history
index; Down drops only the rejection table. The reason is a required `varchar(64)`
code from the existing `AiRiskRejectionReason` enum. SQL Server binary collation
and exact byte length reject lowercase, padded and unknown codes. The timestamp
uses `datetime2(7)` and retains UTC kind on reads.

The request contains `DecisionId` and the existing `AiRiskDecision`: `Approved`
must be false, `ApprovedQuantity` exactly zero, and the reason defined and nonnull.
All ten existing reasons are supported, including `HoldSignal`; HOLD decisions
are valid history. Symbol, signal and confidence must exactly match the original
decision, including symbol casing and all decimal digits. The supplied outcome
is stored without reevaluating risk. `RequestedPrice` is not stored or compared
on replay, so even an `InvalidPrice` rejection needs no usable quote. A missing
original returns typed `DecisionNotFound` and never creates a raw decision.

Same ID and reason returns the original DTO and timestamp; a changed reason
returns `RejectionConflict`. Both paths first validate the risk payload against
the raw decision. The PK resolves concurrent inserts: only SQL Server errors
2601/2627 dispose the failed transaction/context, reload the winner through a
fresh context and compare its reason.
Recording a new rejection holds a serializable transaction around the missing
trade check and rejection insert, preventing a decision from becoming both
rejected and executed. An already executed decision returns
`DecisionAlreadyExecuted`; an overlapping SQL deadlock returns `ConcurrencyConflict`.
Other database errors return safe `PersistenceFailure` without SQL or connection details. Each call owns its
EF context and cannot save unrelated scoped changes. There are no update/delete
methods, risk/execution calls, portfolio initialization or financial side effects.

`GetByDecisionIdAsync` returns null when no rejection exists. Both reads enrich
detached DTOs from the raw decision with one SQL join and `AsNoTracking`, without
duplicating symbol, signal, confidence, logical date or model identity in storage.
`GetRecentAsync` accepts limits 1..200 and applies `RejectedAtUtc DESC,
DecisionDate DESC, AiDecisionId DESC` and `Take` in SQL. The API registers the
service scoped; this issue adds no controller or scheduler. A future orchestrator
must record the raw decision first, evaluate risk, then record a rejection.

SQLite tests cover all reasons, required examples, validation, exact linkage,
replay/conflict, safe errors, detached DTOs and isolation of every existing user
and AI table. With `STOCKLAB_TEST_LOCALDB=1`, isolated SQL Server tests exercise
four concurrent same-reason inserts, different-reason races, full decimal
precision, one bounded joined query, all ordering keys, strict reason codes,
required columns, FK/PK enforcement and prevention of cascading deletion.

## Market-data contracts

`StockLab.Application/Interfaces/IMarketDataProvider.cs` defines asynchronous
quote, ticker/company search and historical OHLCV retrieval. Every operation
accepts a cancellation token. DTOs live in `StockLab.Application/DTOs/MarketData`.
The API composes cache, deduplication and rate limiting around an explicitly selected Mock or TwelveData terminal provider.

- Prices and price changes use `decimal`; volumes use nullable `long` share counts.
  Missing optional values stay null; zero means a measured zero. Stock prices must
  be positive and reported volumes nonnegative. Currency codes use ISO 4217.
- Timestamps use `DateTimeOffset` with UTC offset zero. Quote timestamps describe
  when the price was observed, not when the backend received it.
- History uses half-open ranges: UTC instants for Minute/Hour, DateOnly period dates for Day/Week/Month. Bars are unadjusted, sorted and unique by the matching temporal value; no synthetic market-closure bars.
- Unknown symbols return null for quote/history. A known symbol with no historical
  data in the range returns an empty Bars list. Search with no matches returns an
  empty list. Invalid arguments, cancellation and retrieval failures are distinct
  from these normal absence results.

History requests expose shared boundary validation and bars enforce temporal/OHLCV invariants. Provider
implementations must enforce the documented preconditions and output invariants
and keep provider-specific types and failures behind the application boundary.

### Local mock provider

`StockLab.Infrastructure/MarketData/MockMarketDataProvider.cs` contains only local
simulated fixtures for AAPL (Apple Inc.), MSFT (Microsoft Corporation) and NVDA
(NVIDIA Corporation), all in USD on NASDAQ. There are no network calls or API keys.
Quotes are fixed at the simulated August 28, 2026 session close, not current prices.
Their prices, changes and volumes agree with the final daily historical bars.

The mock provides five unadjusted daily OHLCV bars for August 24-28, 2026. Each has PeriodDate and null OpenTimeUtc. Only Day is supported; other valid intervals throw NotSupportedException. Invalid range/interval combinations throw ArgumentException. Filtering uses [FromDate, ToDate), without regenerating prices.

Symbols are trimmed and normalized to uppercase. Search matches partial symbols
or company names case-insensitively and returns each matching stock once.
Unknown symbols return null for quote/history; unmatched searches return an empty
read-only collection. Returned history collections are also read-only.

Null/blank inputs, non-UTC history bounds and invalid ranges are rejected with
argument exceptions. Unsupported intervals are checked before symbol lookup.
Cancellation is checked before validation and while constructing results, with
the caller's token preserved in `OperationCanceledException`. No artificial delay,
randomness or wall-clock dependency is used.

## Restore, build and run

Run from the repository root:

```powershell
dotnet restore backend/StockLab.sln
dotnet build backend/StockLab.sln --no-restore
dotnet run --project backend/StockLab.Api/StockLab.Api.csproj --no-build --launch-profile http
```

The HTTP launch profile selects Development and listens on
`http://localhost:5274`. The `https` profile also listens on
`https://localhost:7247` and requires a local development certificate.
HTTPS redirection remains enabled; the HTTP-only profile can log that no HTTPS
port is configured. Stop the API with Ctrl+C.

## Price Alerts API (#41)

All price-alert operations require `Authorization: Bearer <token>`. The JWT `sub`
claim supplies the user ID; every lookup is scoped to that user. Missing alerts
and alerts owned by another account return the same `404 alert_not_found`.

| Method | Route | Success | Other documented statuses |
| --- | --- | --- | --- |
| GET | `/api/alerts` | 200, array (empty for a new user) | 401, 500 |
| POST | `/api/alerts` | 201, created alert | 400, 401, 500 |
| PUT | `/api/alerts/{id}` | 200, updated alert | 400, 401, 404, 409, 500 |
| POST | `/api/alerts/{id}/disable` | 200, disabled alert | 401, 404, 409, 500 |
| DELETE | `/api/alerts/{id}` | 204, empty body | 401, 404, 409, 500 |

Create accepts exactly `symbol`, `condition`, and `targetPrice`. Symbols are trimmed,
uppercased, required, and limited to 32 characters after normalization. Conditions
are trimmed and case-insensitive at input, then stored as `Above` or `Below`.
Prices are rounded to four decimals using `MidpointRounding.AwayFromZero`; the
rounded value must be positive and at most `999999999999999.9999` (`decimal(19,4)`).
For example, `0.00005` becomes `0.0001`, while `0.000049` is rejected.
Invalid bodies return `400 validation_error`, including unsupported fields.

```http
POST /api/alerts
Authorization: Bearer <token>
Content-Type: application/json

{"symbol":"  aapl  ","condition":"above","targetPrice":250}
```

The response contains `id`, `symbol`, `currency`, `condition`, `targetPrice`,
`status`, `triggeredPrice`, `triggeredAtUtc`, `createdAtUtc`, and `updatedAtUtc`.
It omits user identifiers, navigation properties, and the internal rowversion.
Timestamps are UTC. Creation always uses USD, Active status, and null trigger
fields. The two initial timestamps come from the same `TimeProvider` instant.
Multiple alerts for one symbol are allowed.

```http
GET /api/alerts
Authorization: Bearer <token>
```

Lists use an untracked query ordered by creation time descending, then ID for
stable ties. To change an alert, supply only its condition and target price:

```http
PUT /api/alerts/<id>
Authorization: Bearer <token>
Content-Type: application/json

{"condition":"Below","targetPrice":180}
```

Updates preserve the symbol, currency, creation time, and status. Both Active and
Disabled alerts can be updated. Disable changes Active to Disabled and updates
the timestamp; disabling an already Disabled alert succeeds without a write or
timestamp change.

```http
POST /api/alerts/<id>/disable
Authorization: Bearer <token>
```

Triggered is terminal for editing and disabling: both return
`409 alert_already_triggered`. Create a new alert to rearm it. All owned alerts,
including Triggered alerts, can be deleted:

```http
DELETE /api/alerts/<id>
Authorization: Bearer <token>
```

EF uses the existing SQL Server rowversion for writes. A concurrent update,
disable, or delete returns `409 alert_update_conflict` with a controlled message.
No migration, polling, market-price lookup, automatic triggering, activation
endpoint, or frontend integration is included. Monitoring and triggering remain
scoped to #42/#43, with the frontend integration in #57.

`PriceAlertsApiTests` exercises real JWT authentication and relational SQLite
persistence, validation, user isolation, lifecycle, application restart, terminal
states, and stale-token concurrency. Default test runs do not access Azure SQL.
`PriceAlertsSqlServerApiTests` is opt-in using
`STOCKLAB_PRICE_ALERTS_SQL_CONNECTION`, supplied securely outside the repository.
It uses the existing schema without DDL or migrations, creates disposable users,
logs in through the API, runs create/list/update/disable/restart/list/delete, and
checks persisted SQL fields and real generated rowversion conflicts. Its cleanup
removes only the users, portfolios, and alerts created by that test fixture.

```powershell
# Supply STOCKLAB_PRICE_ALERTS_SQL_CONNECTION securely in the local process first.
dotnet test backend/StockLab.sln --no-build --filter FullyQualifiedName~PriceAlertsSqlServerApiTests
```

## Stock quote HTTP API

`GET /api/stocks/{symbol}/quote` delegates to `IMarketDataProvider` through
`StocksController`, passing the HTTP request cancellation token. The default DI
configuration selects the local mock. HTTP response DTOs live in `StockLab.Api/DTOs`.

```powershell
Invoke-RestMethod http://localhost:5274/api/stocks/AAPL/quote
```

The response contains only `symbol`, `price`, `change`, `changePercent` and `volume`.
Financial values are JSON numbers; unavailable optional values remain null.
Symbols are trimmed and uppercased. AAPL, MSFT and NVDA return 200 with simulated
fixture quotes. Unknown symbols return 404 with `error: stock_not_found` and a
message. Blank symbols matching the route are rejected before the action with
400 and `error: validation_error`.
Provider exceptions propagate to the global middleware. A URL missing the symbol
segment does not match this route and returns 404.

This route and its 200/400/404/500 response schemas appear in Development OpenAPI.
Search and history HTTP endpoints are described below; frontend integration is separate.

## User registration

`POST /api/auth/register` validates and creates a user account together with its
default paper-trading portfolio. For example, send:

```json
{
  "displayName": "Example User",
  "email": "example.user@example.com",
  "password": "example-only-password"
}
```

Success returns `201 Created` with the user's `id`, `displayName`, `email` and
`createdAtUtc`. Invalid input returns the shared `validation_error` response, and
an email already in use returns `409 Conflict` with `email_already_registered`.
The service stores only an ASP.NET Core password hash; plaintext passwords are not
persisted or returned. A successful registration atomically persists exactly one
portfolio with `USD` currency, `$100,000` initial capital and `$100,000` available
cash. It starts with no holdings or transactions. Portfolio details are not part
of the registration response, and this endpoint does not issue an authentication
token. Use `POST /api/auth/login` to authenticate after registration.

## User authentication

`POST /api/auth/login` accepts the email and password used for registration:

```json
{
  "email": "example.user@example.com",
  "password": "example-only-password"
}
```

A successful login returns `200 OK` with a Bearer access token, its UTC expiry,
and the user's public `id`, `displayName` and `email` fields. The token is signed
with HMAC SHA-256 and contains the user's immutable ID in `sub`. Unknown email
and wrong password both return `401 Unauthorized` with the same
`invalid_credentials` response. Invalid request data uses the shared
`validation_error` response.
Login attempts are limited to five requests per minute per client IP by default;
excess requests receive `429 Too Many Requests` with a `too_many_requests` error.
Configure `LoginRateLimit:PermitLimit` and `LoginRateLimit:Window` to adjust this
limit. The in-process limit is applied independently by each API instance. When
running behind a reverse proxy, configure its address under
`ForwardedHeaders:KnownProxies` (for example,
`ForwardedHeaders__KnownProxies__0=10.0.0.10`). The API then uses
`X-Forwarded-For` only when the immediate peer is on that trusted list; without
that configuration, the immediate peer address is used. Do not trust arbitrary
forwarded headers from untrusted clients.

Send the token to endpoints marked `[Authorize]` as
`Authorization: Bearer <token>`. Missing, malformed, expired, incorrectly signed,
or otherwise invalid tokens return the safe `unauthorized` response.
Current market-data routes, health checks, registration and login remain public.
The profile and portfolio routes require a valid access token. Access tokens expire after `Jwt:AccessTokenMinutes` (60 minutes by default); there is no
refresh-token flow yet. Frontend session storage and login-page integration are
separate work.

The token issuer, audience and lifetime are configured by `Jwt:Issuer`,
`Jwt:Audience` and `Jwt:AccessTokenMinutes`. Set the signing key only in User
Secrets or an environment variable; startup rejects missing or weak keys. See
the repository README's backend secrets section for local setup.

## Password change

`PUT /api/auth/password` requires a valid Bearer token and accepts
`currentPassword`, `newPassword`, and `confirmPassword`. The user ID comes only
from the authenticated JWT. The current password must match the stored hash;
the new password must contain at least eight characters, differ from the current
password, and match the confirmation. Passwords are never trimmed.

A `204 No Content` response is returned only after the new hash is saved.
Invalid input returns `400 validation_error`; a wrong current password returns
`400 invalid_current_password`; a missing account returns `404 user_not_found`.
Concurrent updates return `409 password_update_conflict`, and persistence failures
use the existing generic `500` response. The endpoint shares the configured login
rate limit and returns `429` when exhausted. No plaintext password is stored or
returned. Existing JWTs retain their configured expiry; this change does not add
a token revocation mechanism.

The frontend waits for `204` before showing success, renders safe localized
errors, blocks duplicate submissions, and discards responses after the sign-in
session changes. API tests use isolated SQLite persistence and the actual password
hasher/login services to verify that the old password is rejected and the new one
works. This test setup does not validate Azure SQL connectivity.

## User profile

The authenticated user's profile is available at <code>GET /api/profile</code> and
<code>PUT /api/profile</code>. Send the login token in the
<code>Authorization: Bearer &lt;access-token&gt;</code> header.

GET returns <code>id</code>, <code>displayName</code>, <code>email</code>,
<code>createdAtUtc</code> and <code>updatedAtUtc</code>. PUT accepts the complete
editable profile:

~~~http
PUT /api/profile
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "displayName": "Example User",
  "email": "example.user@example.com"
}
~~~

Only <code>displayName</code> and <code>email</code> can be changed here. The
authenticated user's ID comes from the JWT <code>sub</code> claim; clients never
send a user ID to select a profile. Email trimming and normalization match
registration. A duplicate email returns <code>409 Conflict</code> with
<code>email_already_registered</code>. These routes do not change passwords or
portfolio data, and they require no database migration.

## Portfolio API

Human trade responses (`POST /api/portfolio/trades`, recent transactions and
transaction history) serialize `totalAmount` as a plain decimal **JSON string**,
for example `"999999989999999.999900000001"`. History summary fields
`totalInvested` and `totalProceeds` also use decimal strings (including `"0"`).
These amounts retain up to twelve fractional digits; clients must preserve the
strings through parsing and formatting rather than convert them to JavaScript
`number`. `cashBalance` uses the same representation in trade responses and both
`GET /api/portfolio` and `GET /api/portfolio/performance`. Portfolio monetary
aggregates (`initialCapital`, `investedValue`, `positionsMarketValue`, `totalValue`,
`totalPnl`, and performance positions' `marketValue`/`pnl`) are decimal strings too,
so calculating a small gain beside a large balance remains lossless. Quantities,
unit prices and percentage fields retain their numeric JSON types. Deploy the API
and frontend together; numeric money responses are rejected instead of silently
accepting already rounded values. Order estimates normalize quantity to eight and
price to four decimals, then multiply exactly and display up to twelve decimals.

`GET /api/portfolio` returns the authenticated user's stored portfolio. Send
`Authorization: Bearer <token>`, using the access token from
`POST /api/auth/login`. The user ID comes exclusively from the JWT `sub` claim;
the endpoint accepts no user or portfolio selector. Reads use EF Core
`AsNoTracking()` and load only that portfolio and its holdings.

A newly registered account returns:

```json
{
  "cashBalance": "100000",
  "initialCapital": "100000",
  "investedValue": "0",
  "totalValue": "100000",
  "currency": "USD",
  "positions": []
}
```

Each position contains only `symbol`, `quantity` and `averageCost`, for example
`{ "symbol": "AAPL", "quantity": 10, "averageCost": 150 }`.
Financial values use decimal arithmetic:
`investedValue = sum(quantity * averageCost)` and
`totalValue = cashBalance + investedValue`. These are acquisition-cost values,
not current market valuations. Fractional quantities and stored cost precision are
preserved without rounding the totals; positions are sorted by symbol.

The service never creates or changes a portfolio. Missing portfolios return
`404` with `{ "error": "portfolio_not_found", "message": "The portfolio was not found." }`.
Missing/invalid authentication returns `401 unauthorized`; unexpected failures
use the shared safe `500 internal_server_error` response. OpenAPI documents
200/401/404/500 and the Bearer requirement.

User entities, password hashes, normalized emails, concurrency versions and
transactions are not exposed. No market-data provider is called. Live prices,
P&L and performance belong to #38; transaction history belongs to #39. This route
adds no BUY/SELL operation, migration or frontend integration.

`PortfolioApiTests` exercises the actual application, registration/login/JWT
pipeline and EF Core against an isolated SQLite database. It covers initial
balances, multiple/fractional positions, user isolation, invalid tokens, missing
portfolios, safe failures and the OpenAPI contract. SQLite does not replace
validation against Azure SQL.

## Watchlist API

The watchlist is one implicit list per user, persisted in the existing `Watchlists`
table. All endpoints require `Authorization: Bearer <access-token>` and use only
the JWT `sub` for ownership; request bodies and query strings cannot select a user.

| Method | Route | Success | Other documented responses |
| --- | --- | --- | --- |
| GET | `/api/watchlist` | 200, an array (empty for a new user) | 401, 500 |
| POST | `/api/watchlist` | 201, the added item | 400, 401, 409, 500 |
| DELETE | `/api/watchlist/{symbol}` | 204, no body | 400, 401, 404, 500 |

Add a symbol with `POST /api/watchlist`:

```json
{ "symbol": "AAPL" }
```

POST and each item in GET return only `symbol` and `createdAtUtc` (UTC). GET orders
by creation time descending, then symbol ascending for equal timestamps. Symbols
are trimmed and uppercased with invariant casing and must contain 1–32 characters
after trimming. Thus `" aapl "` and `"AAPL"` identify the same entry. Slash-containing
symbols, control characters (including NUL), and the exact dot segments `.` and
`..` are rejected so accepted entries fit the single-segment DELETE route and
survive URL parsing. The rule applies after trimming; ordinary dotted symbols
such as `BRK.B` remain valid.
Clients should URL-encode symbols in that route (for example, `BRK.B` and
`AAPL:NASDAQ` are supported); literal percent sequences are not decoded twice.

Duplicates return 409 with `watchlist_item_already_exists`. The existing unique
`(UserId, Symbol)` index also protects concurrent additions; the same symbol can
belong to different users. `DELETE /api/watchlist/AAPL` deletes only the caller's
entry, returning 404 with `watchlist_item_not_found` if absent or already removed
by a concurrent request. Invalid symbols
return the existing 400 validation envelope. Adding or removing entries makes no
market-provider calls and does not change portfolios, holdings, transactions or
price alerts. This feature needs no new migration.

The API tests use an isolated relational SQLite database, including simultaneous
inserts and a register/login/add/restart/read/delete persistence check. To verify
the configured Azure SQL environment, repeat that flow against the running API:
register a test account, log in, POST AAPL, GET the list, restart the backend,
GET with the same token, DELETE AAPL, and GET an empty array. Apply existing
migrations separately before testing; the API never changes the schema at startup.

## Global exception handling

`StockLab.Api/Middleware/ExceptionHandlingMiddleware.cs` wraps the HTTP pipeline
in every environment, including Development. Public errors use `ApiErrorResponse`
with the existing JSON shape `{ "error": "code", "message": "safe message" }`.

- `ArgumentException` (including `ArgumentOutOfRangeException`): 400, `invalid_request`, `The request is invalid.`
- `NotSupportedException`: 400, `unsupported_operation`, `The requested operation or interval is not supported.`
- Unexpected exceptions: 500, `internal_server_error`, `An unexpected error occurred.`
- `OperationCanceledException` with the HTTP request token cancelled: no response
  body is written; status 499 is set if headers have not been sent. A disconnected
  client generally cannot receive that status. Other cancellations are treated as
  unexpected failures, rather than silently hidden as client disconnects.
- An already-started response cannot be replaced safely: unexpected exceptions
  propagate to the host, without appending an error document.

Unexpected failures are logged at Error with exception type and request trace ID.
Exception objects, messages, stacks, request values and credentials are not logged
by this middleware. Expected invalid input and client cancellations are not logged
as server errors. Normal 404 results remain decisions of the controller.
The middleware remains a safety net for exceptions; normal request validation
uses MVC's automatic model-state filter described below.

## HTTP request validation

Controllers marked `[ApiController]` reject invalid model state before action
execution. `Validation/ApiValidation.cs` configures one
`InvalidModelStateResponseFactory` for annotation and model-binding failures.
The public JSON extends the existing error/message convention with field errors:

```json
{
  "error": "validation_error",
  "message": "The request contains invalid data.",
  "errors": {
    "symbol": ["The value is missing or invalid."]
  }
}
```

Field messages are controlled, generic text: raw binding errors, attempted values
and exceptions are never copied into the response. Use native `[Required]` for
missing, empty or whitespace strings, `[RegularExpression]`/`[StringLength]` for
formats and lengths, `[Range]` for bounds and `[EnumDataType]` for defined enum
values when an endpoint's contract requires them. Typed route/query parameters
also participate in model binding and use the same 400 format on conversion errors.

The quote action uses `[Required]` on its route symbol and no longer contains a
manual blank-input check. Its existing provider contract requires only a nonblank
symbol; no speculative ticker regex or length limit is imposed. Trimming and case
normalization remain unchanged. Missing route segments still produce 404 because
no action matches. Application/provider preconditions remain for non-HTTP callers.

`ApiValidationTests` runs MVC in an in-memory TestServer and verifies that invalid
requests never reach its counting provider. A test-only controller exercises
required fields, regex, range, enum and query-binding validation; none of those
probe routes or future search/history DTOs are added to the production API.

## Health and OpenAPI

While the API is running:

```powershell
Invoke-RestMethod http://localhost:5274/health
Invoke-RestMethod http://localhost:5274/openapi/v1.json
```

`GET /health` returns HTTP 200 and `Healthy` using ASP.NET Core Health Checks.
This is a process health endpoint only: no database or external dependencies are
registered yet, so it does not certify cloud connectivity.

OpenAPI JSON is available at `/openapi/v1.json` only in Development. There is no
Swagger UI configured. Outside Development the OpenAPI route returns 404.
The removed `/weatherforecast` sample returns 404. The `.http` file in the API
project includes health and OpenAPI requests.

## CORS and configuration

The named `Frontend` CORS policy reads `Cors:AllowedOrigins`. Development
configuration permits only `http://localhost:5173`, the local React frontend.
Methods and headers are allowed for that origin; credentials are not enabled.
Other origins receive no CORS permission. CORS controls browser access, not
authentication or authorization.

No origins are configured by default outside Development. Supply explicitly
approved origins through environment-specific configuration when needed, for
example `Cors__AllowedOrigins__0`. Do not use a wildcard origin.

ASP.NET Core's standard configuration pipeline supports JSON configuration,
environment variables and command-line overrides. Keep future secrets in local
user secrets or an appropriate secure environment configuration; never commit
keys, passwords or sensitive connection strings. No cloud credential placeholders
or resources are required by this foundation.

## Verification

Run all backend unit tests from the repository root with one command:

```powershell
dotnet test backend/StockLab.sln
```

`StockLab.UnitTests` uses xUnit and the .NET test SDK, references Api, Application and
Infrastructure, and groups tests under `Api/` and `MarketData/`. API tests cover
exception JSON, safe logging, client cancellation, started responses and the quote
controller's existing results. No deliberately failing endpoint is added. Production projects
do not reference the test project. Initial tests exercise the existing local mock:
quotes, searches, historical date boundaries and consistency, invalid input and
cancellation. They use fixed fixture dates without network calls, credentials,
cloud resources or wall-clock dependencies. Comprehensive Market Data coverage
and tests of future protection layers remain in issue #88.

For separate restore, build and test steps:

```powershell
dotnet restore backend/StockLab.sln
dotnet build backend/StockLab.sln --no-restore
dotnet test backend/StockLab.sln --no-build
```

For API changes, also check health, Development OpenAPI, absence of WeatherForecast
and CORS behavior with the API running.

## Stock search API

`GET /api/stocks/search?query=apple` searches the configured `IMarketDataProvider`
by ticker or company name. The local mock matches case-insensitively and trims
surrounding whitespace. Results use the HTTP `StockSearchResponse` DTO:

```json
[{"symbol":"AAPL","companyName":"Apple Inc.","exchange":"NASDAQ","currency":"USD"}]
```

A valid search without matches returns HTTP 200 with `[]`. Missing, empty or
whitespace-only `query` returns HTTP 400 using the existing `validation_error`
format before the provider is called. Unexpected exceptions remain handled by
the global middleware. The HTTP cancellation token is forwarded to the provider.
The Development OpenAPI document describes the required query and 200/400 responses.

## Stock history API

All parameters are required: symbol, from, to, interval. Each accepts a single value.
Minute/Hour require explicit ISO UTC datetimes (Z or +00:00); Day/Week/Month require
strict yyyy-MM-dd dates. Mixed formats, nonzero offsets, composite interval values,
and from >= to return HTTP 400 validation_error before the provider is called.

Calendar example:
GET /api/stocks/AAPL/history?from=2026-08-24&to=2026-08-29&interval=Day

Intraday example (TwelveData only):
GET /api/stocks/AAPL/history?from=2026-08-24T13:30:00Z&to=2026-08-24T15:30:00Z&interval=Minute

The Application request contains a comparable StockHistoryRange:
IntradayHistoryRange(FromUtc, ToUtc) or CalendarHistoryRange(FromDate, ToDate).
Minute/Hour must use the former; Day/Week/Month must use the latter.
Filtering is [from,to) in that temporal domain, with no date-to-instant conversion.
This is an intentional breaking correction to the initial daily UTC API contract.

Response fields: symbol, currency, interval and bars. A calendar bar is:
{"openTimeUtc":null,"periodDate":"2026-08-24","open":200,"high":201,"low":199.5,"close":200.5,"volume":20000000}

An intraday bar has openTimeUtc as a real UTC datetime and periodDate=null.
Bar constructors permit exactly one temporal field and reject inconsistent OHLCV.
OpenAPI describes both nullable response properties (date-time and date).
A partial calendar range from=2026-08-25 to=2026-08-27 returns two mock bars.
The mock supports only Day; valid requests for other intervals return 400 unsupported_operation.

## Market data cache

The API registers IMarketDataProvider as a singleton CachingMarketDataProvider
wrapping DeduplicatingMarketDataProvider, RateLimitedMarketDataProvider, then the selected singleton MockMarketDataProvider or TwelveDataProvider. Controllers remain unaware of these decorators.
Select the terminal using MarketData:Provider; decorators keep the same lifecycle and ordering.
Infrastructure uses the native Microsoft.Extensions.Caching.Memory package;
Application and Domain remain independent of cache and HTTP libraries.

MarketDataCache configuration in appsettings.json supplies absolute (not sliding) TTLs:

- QuoteTtl: 00:00:15 (15 seconds).
- SearchTtl: 00:05:00 (5 minutes).
- HistoryTtl: 00:15:00 (15 minutes).

Configuration is read at startup. Each TTL must be positive and at most 365 days;
invalid values fail startup rather than silently disabling expiration or overflowing
expiration calculations. Environment overrides use MarketDataCache__QuoteTtl, etc.
Restart the API after changing TTLs.

Keys use separate operation identifiers, trimmed uppercase symbols/search terms,
and the entire history request (symbol, range kind and both bounds, interval). Each decorator has
its own key namespace to prevent collisions when sharing an IMemoryCache. History
preconditions are validated before lookup, so an invalid offset cannot hit an
equivalent UTC cache entry.

Only completed successful non-null results are cached. Unknown quote/history symbols
are not cached and are retried on subsequent requests. Empty search results and known
histories with empty bars are cached for their respective TTLs. Collections are copied
into read-only snapshots; no exception or cancellation is converted into cached data.
Already-cancelled requests fail even on a hit. Misses forward the caller's token and
check cancellation again before insertion.

IMemoryCache is thread-safe and local to this process. Entries may be evicted and
are lost on restart. The cache itself allows independent concurrent misses; it
delegates those misses to the single-flight decorator described below.

Permanent cache tests use a counting provider and MemoryCache's native controllable
clock to verify call counts and expiration without delays. HTTP checks verify the
public contracts, not cache hits.

MarketDataCache:SizeLimit defaults to 8388608 accounting units (approximately 8 MiB). A dedicated keyed MemoryCache enforces this positive budget. Each entry accounts for fixed overhead, UTF-16 key strings and result strings/collection elements. This is an estimated retained-size budget, not an exact CLR heap limit. Empty results still consume units; oversized entries are returned without being cached. Other application caches are unaffected.

## Concurrent market data requests

Program.cs composes Cache -> Dedup -> RateLimit -> selected terminal behind IMarketDataProvider. Cache hits
bypass the deduplication layer. On a miss, DeduplicatingMarketDataProvider shares
one in-progress provider task per normalized key, separately for quote, search and
history. Keys use trimmed uppercase symbols/search terms and complete validated
history requests (symbol, range kind, both bounds, interval), matching cache equivalences.

ConcurrentDictionary atomically chooses one owner before the provider starts;
other callers await its TaskCompletionSource. No global provider lock, blocking
wait, rate limiting or quota logic is used. Distinct keys remain independent.

Shared provider calls use CancellationToken.None; individual callers use
Task.WaitAsync with their own cancellation token. Cancelling even the first caller
only abandons that caller's wait. Shared work continues even if all callers leave,
until the provider completes or fails. No caller CTS or registration is retained by
the decorator; WaitAsync manages its own registrations. TwelveDataProvider bounds its network I/O with HttpClient timeout. This implementation does not introduce a timeout
or cancellation policy for the underlying provider.

A finally block removes only the completed flight before notifying waiters, for
success, failure and provider cancellation, including synchronous completions.
Faults are observed even if no waiters remain, and still propagate to active callers.
Later calls can retry. Null and empty results are shared while in progress; the
outer cache keeps its existing null/empty caching rules. Successful results are
not retained by the deduplication dictionary after completion.

Tests use a counting provider blocked by TaskCompletionSource, guaranteeing ten
active waiters before release. Coverage includes thread-pool contention, distinct
keys, cancellation isolation, retry/cleanup, null/empty results and cache expiration
with a controllable clock. HTTP checks only verify public contract regressions.

## Outbound provider rate limiting

RateLimitedMarketDataProvider wraps the terminal provider after cache and dedup.
One singleton native FixedWindowRateLimiter supplies a shared budget for quote,
search and history. This protects outbound provider calls, not incoming HTTP requests
by IP or user. A cache hit uses no permit; simultaneous identical misses share one
flight and one permit. Distinct keys and operations consume the same global budget.

MarketDataRateLimit configures local StockLab defaults, not Twelve Data plan limits:

- PermitLimit: 30 attempts per fixed window.
- Window: 00:01:00, automatically renewed by the native limiter.
- QueueLimit: 10 waiting calls, oldest first; zero disables waiting.

Values are validated at startup: PermitLimit 1..10000, QueueLimit 0..1000, Window
1 millisecond..1 day. Invalid values fail explicitly. Configuration changes require
a restart. Fixed windows can allow bursts across a window boundary; this is not a
rolling-window or daily credit cap. Protection and queues are local to this process.
The DI container owns and disposes the native limiter and its renewal timer.

Every attempt needs one acquired permit before calling the terminal provider.
A full queue returns MarketDataRateLimitException, mapped by the existing global
handler to HTTP 429 with error market_data_rate_limited and message
"Market data requests are temporarily rate limited." A Warning is emitted only on
rejection, without symbol, query, payload, quota or credentials. No Retry-After is
invented. OpenAPI describes the 429 response on all three market data endpoints.

Cancellation while queued stops that wait without calling the provider or becoming
429. The received token is forwarded to the provider after admission. In the actual
pipeline, dedup supplies a shared cancellation token. One caller leaving does not
cancel work needed by other viewers. When the last caller leaves, the queued or
active operation is cancelled and a later caller starts a fresh flight. Abandoned
chart periods therefore cannot use a permit after the next renewal.

Provider failures propagate unchanged and still consume the window permit: an
attempt may already have spent external credits. Lease disposal is guaranteed but
does not refund a fixed-window permit. The next renewal restores capacity. This
layer neither caches failures nor implements retries.

Permanent tests use the native limiter with AutoReplenishment=false and explicit
TryReplenish (a minimal test-only window), plus a TaskCompletionSource-controlled
provider for the ten-caller pipeline test. No sleeps or long delays are needed.
They assert native statistics, provider counts, bounded queue/rejection, cancellation,
shared budget, null/empty behavior, provider failures and safe 429 JSON.


## Twelve Data REST provider

The committed default is MarketData:Provider=Mock. A clone runs without credentials
and makes no external market-data calls. TwelveData must be selected explicitly.
No startup or health probe contacts Twelve Data. Periodic price-alert monitoring
can request quotes for active rules through the existing market-data pipeline.

Both pipelines are Cache -> Dedup -> RateLimit -> terminal (Mock or TwelveData).
Controllers inject only IMarketDataProvider. Application exposes provider-neutral
DTOs and controlled failure categories; no Twelve Data SDK or ASP.NET dependency
enters Application/Domain. API composition selects the terminal once per process.

TwelveData uses a named IHttpClientFactory client with the fixed HTTPS base
https://api.twelvedata.com/, configurable TimeoutSeconds (default 10, allowed 1..60),
an 8 MiB response-buffer limit, and redirects disabled. Factory HTTP logging is
disabled. Authentication is exclusively an Authorization header using the apikey
scheme. Infrastructure logs only operation, controlled category and HTTP status,
never raw exceptions, bodies, URLs, headers or credentials.

Each operation makes at most one REST request. No retry, fallback, pagination or
batch mode is implemented. Quote/history reject comma-separated symbols before
sending anything. The local rate-limit settings are unchanged and are not a promise
about any Twelve Data plan; configure them separately for the subscription.

### Mapping and provider limitations

Official documentation checked:
- https://twelvedata.com/docs/market-data/quote
- https://twelvedata.com/docs/market-data/time-series
- https://twelvedata.com/docs/advanced (authentication, symbol_search and instrument types)
- https://support.twelvedata.com/en/articles/5656039-how-to-get-historical-prices
- https://support.twelvedata.com/en/articles/12682324-end-of-day-eod-pricing-market-data
- https://support.twelvedata.com/en/articles/5615854-credits

Quote uses /quote?symbol=...: close -> Price, change -> Change,
percent_change -> ChangePercent, volume -> nullable share count, currency -> Currency.
AsOfUtc uses last_quote_at (last minute candle), NOT timestamp (interval opening).
A missing observation time is a controlled invalid response, never DateTimeOffset.Now.
Numbers are parsed with invariant culture and decimal/long; absent optional values
remain null. Invalid or incoherent values fail without fabricated zeros.

Search uses /symbol_search?symbol=...; instrument_name maps to CompanyName.
The deliberately narrow allowlist is the documented Common Stock instrument_type.
Other types, including crypto, forex, funds and commodities, are excluded.
Relevance order is preserved. Symbols are uppercase and exchange-qualified when an
exchange is supplied, e.g. AAPL:NASDAQ; duplicate canonical symbols collapse.
Missing optional exchange/currency stays null. No matches returns [].

History uses /time_series with symbol, interval, start_date, end_date, adjust=none,
order=asc. Intervals map Minute/Hour/Day/Week/Month to 1min/1h/1day/1week/1month.
No outputsize is sent: official documentation says combined date bounds define the
range and outputsize would restrict it.
For intraday, timezone=UTC controls both query bounds and output. exchange_timezone
describes the venue, not this explicitly requested output timezone; applying its
offset again would corrupt the instant. There is no hardcoded exchange or DST offset.
For calendar intervals, timezone is omitted (the API ignores it); YYYY-MM-DD becomes
PeriodDate unchanged. Week/Month preserve the provider's bar date without asserting
a universal Monday/first-of-month rule or fabricating an opening instant.

Final bars are filtered to [from,to), sorted and unique. Identical duplicates
collapse; conflicting duplicates cause a controlled invalid response.
To guarantee the single-request limit conservatively, intraday accepts only ranges
whose floor(duration/interval)+2 is <=5000; calendar accepts at most 4999 calendar
days even for Week/Month. These bounds include room for endpoint rounding/inclusion.
Responses with >=5000 values are rejected rather than risking silent truncation.
The caller must request a smaller range; the server never paginates automatically.

### Controlled upstream failures

Local rejection is unchanged: HTTP 429 market_data_rate_limited.
Upstream 429 is HTTP 503 market_data_provider_rate_limited.
Upstream 400 / malformed response -> 502 market_data_provider_invalid_response.
Upstream 401/403/5xx, network failure or ambiguous data-unavailable 404 ->
503 market_data_provider_unavailable. Timeout -> 504 market_data_provider_timeout.
Oversized history -> 400 market_data_range_too_large.
Only a recognized instrument/symbol-not-found error yields null for quote/history
(and therefore API 404 stock_not_found). A generic 404 or ambiguous no-data message
does not prove the symbol is unknown and remains a safe provider failure.
A successful history with metadata and empty values returns an empty Bars list.
No raw provider messages reach the caller. No error triggers another HTTP request.

### Three-key separation and local configuration

Website serves normal quote/search/history through this backend.
Fallback is a manually selected emergency replacement; it is inactive by default.
The future Python ML runtime owns TWELVE_DATA_ML_API_KEY. The Website credential
resolver never reads that setting, and MachineLearning is not a selectable role.
No ML runtime or secret file is introduced here.

Only Website/Fallback are valid TwelveData:ActiveWebsiteKey values. Unknown
providers, invalid roles or timeout settings fail startup. A selected TwelveData
terminal also requires its selected credential at startup. Mock requires no key.
Only non-secret settings are committed; keys belong in local User Secrets or a
securely injected environment, never appsettings or frontend.

Placeholder commands (run locally; replace placeholders privately):

dotnet user-secrets set "TwelveData:Keys:Website" "<WEBSITE_API_KEY>" --project backend/StockLab.Api/StockLab.Api.csproj
dotnet user-secrets set "TwelveData:Keys:Fallback" "<FALLBACK_API_KEY>" --project backend/StockLab.Api/StockLab.Api.csproj

StockLab.Api declares a non-secret UserSecretsId. User Secrets live outside the
repository and are for local development; they are not a production vault.
Environment alternatives are TwelveData__Keys__Website and TwelveData__Keys__Fallback.
The future ML service convention is TWELVE_DATA_ML_API_KEY="<ML_API_KEY>"; do not set
it in the Website process.

To activate Website locally in PowerShell:

$env:MarketData__Provider = "TwelveData"
$env:TwelveData__ActiveWebsiteKey = "Website"
dotnet run --project backend/StockLab.Api/StockLab.Api.csproj --no-build --launch-profile http

For manual emergency selection, set TwelveData__ActiveWebsiteKey=Fallback and restart.
There is no automatic credential rotation or failover on 401/403/429/5xx/timeout.

### Offline tests and optional live smoke

All automated tests use local fake handlers, mock data and explicitly fake credentials.
Tests of the actual Program override credentials and replace the named HTTP handler.
They do not use development User Secrets. Controlled gates/clock advancement verify
deduplication and expiry; a blocked local handler verifies HttpClient timeout.

Only after restore/build/tests/security checks, an operator with a securely installed
Website credential may execute these three local API requests ONCE, with no retries:

Invoke-RestMethod http://localhost:5274/api/stocks/TSLA/quote
Invoke-RestMethod 'http://localhost:5274/api/stocks/search?query=tesla'
Invoke-RestMethod 'http://localhost:5274/api/stocks/TSLA/history?from=2026-08-28&to=2026-09-06&interval=Day'

This is at most three external requests. Never exercise real quotas, cache expiry,
Fallback or ML credentials as a smoke test. Availability and delay depend on the plan;
a successful response is not proof of a real-time entitlement.


## Market enrichment and frontend integration (#82)

Twelve Data remains the only source for quote prices, changes, volume, stock search
and OHLCV history. Quote metadata and optional open/high/low/previous close,
average volume, market status and 52-week bounds come from the **same** quote call.
No premium Twelve Data fundamentals, movers or logo calls are made.

`IMarketEnrichmentProvider` is separate from `IMarketDataProvider`. Its Alpha Vantage
implementation exposes these independent, optional resources:

| StockLab endpoint | Upstream operation | Default cache |
| --- | --- | --- |
| `/api/stocks/{symbol}/fundamentals` | `OVERVIEW` | 24 hours |
| `/api/stocks/{symbol}/logo` | `COMPANY_LOGO` | 30 days |
| `/api/stocks/{symbol}/earnings` | `EARNINGS_CALENDAR`, symbol, 12-month horizon | 24 hours |
| `/api/market/movers` | `TOP_GAINERS_LOSERS`, no entitlement parameter | 12 hours |

Fundamentals expose nullable financial decimals and analyst counts, without deriving
an AI recommendation or inventing a provider consensus. Dividend yield is a ratio
(e.g. 0.012 means 1.2%); mover changePercent is percentage points. Earnings are parsed
as CSV, including quoted fields, and select the next report date on/after the UTC
current date. No scheduled event returns null fields. Movers are latest available EOD
data, not realtime quotes. Missing logos return nullable PNG/SVG URLs and the UI uses
a ticker letter. Only HTTPS URLs on the official `cdn.alphavantage.co/logos/` path,
with the expected extension and no credentials/query/fragment, can reach the client.
The metadata response is size/content-type checked; images are public CDN references,
not fetched or proxied by this API. Image loading errors use the UI fallback.

Official references: [Alpha Vantage documentation](https://www.alphavantage.co/documentation/)
and [free quota support](https://www.alphavantage.co/support/). The public logo demo
returns `symbol`, `logo_url_png`, and `logo_url_svg`; OVERVIEW includes analyst counts.
Availability for the installed key is still subject to provider entitlement.
Advanced financial statements, news and earnings forecasts are deferred and never
prefetched. Alpha errors never trigger a Twelve Data fallback, or vice versa.

### Local configuration and quota protection

Configure the key externally, for example:

```powershell
dotnet user-secrets set "AlphaVantage:ApiKey" "<ALPHA_VANTAGE_API_KEY>" --project backend/StockLab.Api
```

`ALPHA_VANTAGE_API_KEY` is an environment alternative. No key setting belongs in
tracked appsettings or Vite configuration. Alpha uses the official apikey query
parameter upstream; its named HttpClient removes HTTP loggers and disables redirects.
Upstream URIs, bodies, request objects and exceptions are never logged or returned.
Twelve Data continues to use its separate Website Authorization header; no automatic
Fallback key selection and no ML credential sharing.

Defaults: `AlphaVantage:DailyRequestBudget=20`, `TimeoutSeconds=10`. The free plan
currently documents 25 requests/day. Budget is per application instance and UTC day,
**in memory**, and resets on restart: it is a safeguard, not the provider quota ledger.
Other applications using the key can consume the provider quota independently.
Only one Alpha transport request is active at once. Shared in-flight requests and
cache hits do not consume additional budget. A sent failed attempt does count.
`TimeoutSeconds` covers the entire operation, including semaphore wait and response
body reads. A queued timeout sends no HTTP request and consumes no budget. When the
last caller cancels, its queued or active work is cancelled; a surviving joined
caller keeps the shared operation alive. Caller cancellation is never cached.
No retries, polling or background refresh. Known failures have a protective cooldown
(12 hours for quota/entitlement, one minute for other failures) so repeated logo
requests cannot spend credits on the same outage. A spent local budget returns safe HTTP 429;
provider/entitlement failures 503, malformed data 502 and timeout 504. HTTP 200
Information/Note/Error Message envelopes are handled as failures, never data.

Cache TTL options: `OverviewTtl`, `LogoTtl`, `EarningsTtl`, `MoversTtl` under
`AlphaVantage`, using .NET TimeSpan strings. All are validated at startup as positive
and at most 365 days; budget is 1..25 and timeout 1..60 seconds. Earnings cache keys
include the UTC day so yesterday's event cannot become today's next earnings.

US canonical suffixes (NASDAQ/NYSE/NYSEAMERICAN/AMEX) are resolved explicitly for
Alpha upstream requests while preserving the StockLab response symbol. Unqualified
international dot suffixes are retained; unsupported exchange qualifiers are rejected
instead of silently querying a different security.


For a local Twelve Basic runtime, use these **non-secret** environment settings before
starting the API (committed generic/offline defaults remain unchanged):

```powershell
$env:MarketDataRateLimit__PermitLimit = "8"
$env:MarketDataRateLimit__QueueLimit = "8"
$env:MarketDataCache__QuoteTtl = "00:01:00"
```

[Twelve Basic](https://twelvedata.com/pricing) currently provides 8 credits/minute and
800/day. A 60-second quote cache supports navigation without spending five fresh credits
on each visit to Market. Up to eight requests wait for the next local minute window;
further requests receive a controlled 429. Cancelled periods leave this queue, while
rapid chart clicks are debounced for 300 ms and loaded periods use the frontend cache.
Its local minute boundary and other clients can still differ from the provider quota.
The frontend does not poll; active price-alert rules are monitored periodically by
the backend. No retries or fallback keys are used. Alpha has a separate 20/day best-effort
budget. UI logos load directly from Elbstream with attribution and consume no Alpha
credits; unavailable logos keep the ticker fallback.

Market providers cannot supply user holdings, cash, executions, alert rules, or StockLab
ML decisions. Those account sections now render unavailable/empty states in the frontend
until their own services exist. No portfolio or ML behavior is added by this integration.

### Company news

`GET /api/stocks/{symbol}/news` uses `NEWS_SENTIMENT`, `tickers`, `sort=LATEST`
and `limit=20`. It shares the Alpha transport deadline, semaphore, deduplication,
budget and safe logging. `AlphaVantage:NewsTtl` defaults to one hour. Output is at
most ten distinct HTTPS headline links with source and UTC publication date.
Articles must explicitly match the ticker with provider relevance >= 0.5; missing
feeds fail safely and an empty valid feed remains empty. The frontend additionally
filters headlines for the selected company identity. Article bodies are not copied.
Logos in the frontend now use the free Elbstream CDN with required attribution;
Alpha logo endpoints remain available but are not called by StockLogo.

## Price alert monitoring (#42)

`AddPriceAlertMonitoring` registers a scoped `IPriceAlertMonitoringService` and a
hosted worker. The worker waits for its first `PeriodicTimer` tick, creates an async
DI scope, and awaits the complete cycle before processing the next tick. Missed
ticks coalesce; they never produce concurrent cycles. Shutdown cancels timer and
provider waits, and disposes the cycle's scope. A failed database cycle is logged
without raw exception details and retried at the next tick.

Only `Active` alerts are projected from SQL with `AsNoTracking`. Alerts are grouped
across all users by symbol; each admitted group makes one `GetQuoteAsync` call to
the registered cache -> dedup -> rate-limit -> terminal pipeline. No active rules
means no quote calls. Lookups are sequential. Missing quotes and provider errors
skip their symbol; currency mismatches skip the affected rules with one warning
per symbol. Warning symbols replace control characters and Unicode line/paragraph
separators with `?`, preserving the original identifiers for quotes and matches.
Prices remain `decimal`; Above uses strict `>` and Below strict `<`.
Equality never matches.

`PriceAlertMonitoring:Interval` defaults to `00:01:00` and can also be supplied as
`PriceAlertMonitoring__Interval`. Startup validation requires a timer-supported
interval of 1..4294967294 milliseconds. Configuration changes require a restart.
With an external terminal, active rules can consume provider quota each cycle;
the existing cache and limiter still apply. The committed provider is `Mock`.

`PriceAlertMonitoring:DailyQuoteBudget` defaults to 200 and must be positive at
startup. It caps **all monitoring quote lookups per UTC day**, shared across users,
symbols, cycles and DI scopes. Every lookup reserves budget before entering the
market-data pipeline, including cache hits, missing quotes, failures and cancelled
attempts. Reservations are never refunded, so monitoring cannot spend more than
the configured number of outbound quote requests during one process's UTC day.
Foreground quotes/search/history do not use this monitoring-specific budget.

After the budget is spent, remaining symbols are counted as deferred and make no
provider calls until the next UTC day. Monitoring resumes after the last attempted
symbol, wrapping in ordinal order, so partial cycles do not always favor the first
symbols. A backward clock correction does not reset spent budget. Configure the
budget for the provider plan and desired foreground headroom. This safeguard is
in memory per backend instance; restarting resets it, and other instances or
clients can spend the provider's quota independently. It is not an account-wide
provider quota ledger. Rules remain Active while their evaluation is deferred.

`RunOnceAsync` returns `PriceAlertMonitoringResult` and `PriceAlertMatch` values
for #43 to consume. `ObservedAtUtc` comes from `StockQuote.AsOfUtc`. No alert is
updated: Status, TriggeredPrice, TriggeredAtUtc, UpdatedAtUtc and rowversion remain
unchanged. No migrations, HTTP endpoints or frontend changes are introduced.

One Information summary is emitted per completed cycle: active alerts, distinct
symbols, non-null quotes retrieved, matches, failures and deferred symbols. `QuoteCount` counts
non-null quotes; `FailedQuoteCount` counts null or failed lookups. Their sum is the
number of symbol lookups; adding `DeferredSymbolCount` gives the distinct-symbol
count for the cycle. Deferred symbols are not quote failures. A currency mismatch
still counts as a retrieved quote.

The offline API-host smoke test seeds AAPL Above, AAPL Below and MSFT Above,
uses the real configured Mock pipeline with a temporary SQLite database, and
advances a test clock by one interval. It verifies the worker's summary contains
3 active alerts, 2 distinct symbols and 2 quotes, and that all alerts stay Active:

```powershell
dotnet test backend/StockLab.sln --filter FullyQualifiedName~PriceAlertMonitoringCompositionTests
```
