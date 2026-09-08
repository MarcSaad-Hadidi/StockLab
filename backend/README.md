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
pipeline, dedup deliberately supplies an independent token; individual HTTP callers
can abandon their own waits without cancelling shared queued work. This preserves #31.

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
No startup probe, health probe, timer or background request contacts Twelve Data.

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
