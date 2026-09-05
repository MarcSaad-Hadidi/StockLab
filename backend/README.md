# StockLab backend

The backend targets .NET 10. Install the .NET 10 SDK before building locally.

## Architecture

```text
backend/
|-- StockLab.sln
|-- StockLab.Api/
|   |-- Controllers/
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
on the API or Infrastructure. Controllers will delegate use cases to Application.

`Program.cs` is the composition root and registers the available ASP.NET Core
services. Register future application interfaces and implementations here as their
issues introduce them. The library projects currently contain only project files
and tracked folder placeholders: no sample classes or business functionality.
There are no EF Core, Azure, AWS, Twelve Data or authentication integrations.

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

Restore and build the solution, then check health, Development OpenAPI and the
absence of WeatherForecast. Verify that the local frontend origin receives the
CORS header and an unlisted origin does not. There is no automated test project
yet; its creation belongs to issue #86, so `dotnet test` is not applicable here.
