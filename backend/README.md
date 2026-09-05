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
| StockLab.Infrastructure | Future persistence, market-data and cloud implementations behind application interfaces. | Application, Domain |
| StockLab.Api | Controllers, HTTP configuration, OpenAPI, CORS, health checks and dependency injection composition root. | Application, Infrastructure |

Domain is the innermost layer. Application depends on Domain, and Infrastructure
depends on Application and Domain. The API references Application and
Infrastructure to compose the application. Domain and Application must not depend
on the API or Infrastructure. Controllers use application contracts rather than
concrete provider implementations.

`Program.cs` is the composition root and registers the available ASP.NET Core
services. Register future application interfaces and implementations here as their
issues introduce them. Application defines the market-data contracts below;
Infrastructure implements the local mock provider, while Domain remains a placeholder.
There are no EF Core, Azure, AWS, Twelve Data or authentication integrations.

## Market-data contracts

`StockLab.Application/Interfaces/IMarketDataProvider.cs` defines asynchronous
quote, ticker/company search and historical OHLCV retrieval. Every operation
accepts a cancellation token. DTOs live in `StockLab.Application/DTOs/MarketData`.
The API currently registers Infrastructure's `MockMarketDataProvider` as a singleton
implementation. Replacing that registration will select a future provider without
changing application contracts.

- Prices and price changes use `decimal`; volumes use nullable `long` share counts.
  Missing optional values stay null; zero means a measured zero. Stock prices must
  be positive and reported volumes nonnegative. Currency codes use ISO 4217.
- Timestamps use `DateTimeOffset` with UTC offset zero. Quote timestamps describe
  when the price was observed, not when the backend received it.
- History requests use an inclusive start and exclusive end, filtering by bar
  opening time. Intervals are provider-neutral enum values; unsupported intervals
  fail explicitly rather than being substituted. Bars are unadjusted, ordered
  chronologically and unique by timestamp, with no synthetic market-closure bars.
- Unknown symbols return null for quote/history. A known symbol with no historical
  data in the range returns an empty Bars list. Search with no matches returns an
  empty list. Invalid arguments, cancellation and retrieval failures are distinct
  from these normal absence results.

These DTOs define contracts rather than executable validation logic. Provider
implementations must enforce the documented preconditions and output invariants
and keep provider-specific types and failures behind the application boundary.

### Local mock provider

`StockLab.Infrastructure/MarketData/MockMarketDataProvider.cs` contains only local
simulated fixtures for AAPL (Apple Inc.), MSFT (Microsoft Corporation) and NVDA
(NVIDIA Corporation), all in USD on NASDAQ. There are no network calls or API keys.
Quotes are fixed at the simulated August 28, 2026 session close, not current prices.
Their prices, changes and volumes agree with the final daily historical bars.

The mock provides five unadjusted daily OHLCV bars, August 24-28, 2026, opening at
13:30 UTC. Only `StockHistoryInterval.Day` is supported. Other defined intervals
throw `NotSupportedException`; undefined enum values throw
`ArgumentOutOfRangeException`. A known symbol outside the fixture dates returns an
empty history. Date filtering uses `[FromUtc, ToUtc)` on bar opening times and
does not regenerate prices relative to the requested range.

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

## Stock quote HTTP API

`GET /api/stocks/{symbol}/quote` delegates to `IMarketDataProvider` through
`StocksController`, passing the HTTP request cancellation token. The current DI
registration supplies the local mock. HTTP response DTOs live in `StockLab.Api/DTOs`.

```powershell
Invoke-RestMethod http://localhost:5274/api/stocks/AAPL/quote
```

The response contains only `symbol`, `price`, `change`, `changePercent` and `volume`.
Financial values are JSON numbers; unavailable optional values remain null.
Symbols are trimmed and uppercased. AAPL, MSFT and NVDA return 200 with simulated
fixture quotes. Unknown symbols return 404 with `error: stock_not_found` and a
message. Blank symbols reaching the action, or argument errors from the provider,
return 400 with `error: invalid_symbol`. A URL missing the symbol segment does not
match this route and returns 404. Cancellation is allowed to propagate.

This route and its 200/400/404 response schemas appear in Development OpenAPI.
No search/history HTTP endpoints or frontend integration are included.

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

`StockLab.UnitTests` uses xUnit and the .NET test SDK, references Application and
Infrastructure, and groups tests by domain under `MarketData/`. Production projects
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
