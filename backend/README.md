# StockLab backend

ASP.NET Core API targeting .NET 10. Install the .NET 10 SDK to work locally.

## Structure

| Directory | Responsibility |
| --- | --- |
| `StockLab.Api/Controllers` | HTTP endpoints and translation between requests, responses and services. |
| `StockLab.Api/Services` | Business operations, registered with the built-in dependency injection container when introduced. |
| `StockLab.Api/Domain` | Business entities and domain rules. |
| `StockLab.Api/DTOs` | Request and response contracts at API boundaries. |
| `StockLab.Api/Infrastructure` | Persistence and external service implementations. |

The folders are intentionally empty at this stage. Add implementations only in
their corresponding issues. `Program.cs` registers controllers and OpenAPI using
the built-in service container and maps controller routes. There are currently no
business endpoints, database connections or external service integrations.

## Build and run

Run from the repository root:

```powershell
dotnet restore backend/StockLab.Api/StockLab.Api.csproj
dotnet build backend/StockLab.Api/StockLab.Api.csproj --no-restore
dotnet run --project backend/StockLab.Api/StockLab.Api.csproj --no-build --launch-profile http
```

The HTTP development profile listens on `http://localhost:5274`.
In Development, `GET /openapi/v1.json` returns the API description with no business
paths yet. `GET /weatherforecast` returns 404 because the generated sample has
been removed. The `.http` file provides a manual OpenAPI request.

The `https` launch profile also listens on `https://localhost:7247` and requires a
local development certificate. HTTPS redirection remains enabled. With the
HTTP-only profile, ASP.NET Core may log that no HTTPS port is configured.
OpenAPI is not exposed outside Development.

No automated test project exists yet; its setup belongs to issue #86.
Startup and HTTP smoke checks accompany this initial setup.
