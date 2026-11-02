using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Api.Health;

/// <summary>Read-only readiness for the global account/authentication dependency.</summary>
public sealed class SqlServerReadinessHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Resolve inside the failure boundary: missing/invalid SQL settings are not ready,
            // rather than an exception from the framework's check activation factory.
            await using var scope = scopes.CreateAsyncScope();
            var configured = scope.ServiceProvider.GetRequiredService<DbContextOptions<StockLabDbContext>>();
            var options = new DbContextOptionsBuilder<StockLabDbContext>(configured)
                .UseLoggerFactory(NullLoggerFactory.Instance)
                .EnableSensitiveDataLogging(false).EnableDetailedErrors(false).Options;
            // Suppress raw provider diagnostics only for this probe; ordinary request logging is unchanged.
            await using var db = new StockLabDbContext(options);
            db.Database.SetCommandTimeout(ProbeTimeout);
            await db.Database.OpenConnectionAsync(cancellationToken);
            if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                return HealthCheckResult.Unhealthy("Database migrations are pending.");

            // An open connection/migration history alone cannot establish that authentication can read its schema.
            // This bounded projection contains no account identity or password data, including on an empty database.
            await db.Users.AsNoTracking().Select(user => user.SessionVersion).Take(1).ToArrayAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The framework distinguishes its check timeout from caller cancellation. Never forward provider details.
            throw new OperationCanceledException("Readiness check was canceled.", cancellationToken);
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("SQL Server is not ready.");
        }
    }
}
