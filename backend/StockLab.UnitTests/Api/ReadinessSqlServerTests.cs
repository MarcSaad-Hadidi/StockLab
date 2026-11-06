using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Api;

[Trait("Category", "SqlServer")]
public sealed class ReadinessSqlServerTests
{
    [LocalDbTheory]
    [InlineData("Mock")]
    [InlineData("TwelveData")]
    public async Task Readiness_tracks_database_loss_and_recovery_without_writes_or_external_calls(string provider)
    {
        await using var fixture = new DatabaseFixture();
        var external = new RejectExternalRequests();
        using var app = ReadinessApiTests.CreateApp(services =>
        {
            services.AddHttpClient("TwelveData").ConfigurePrimaryHttpMessageHandler(() => external);
            services.AddHttpClient("AlphaVantage").ConfigurePrimaryHttpMessageHandler(() => external);
        }, connection: fixture.Connection).WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MarketData:Provider"] = provider, ["TwelveData:Keys:Website"] = "TEST-ONLY-WEBSITE-KEY"
            })));
        using var client = ReadinessApiTests.Client(app);

        await AssertProbe(client, "/health/ready", HttpStatusCode.ServiceUnavailable, "Unhealthy");
        Assert.False(await fixture.ExistsAsync()); // A probe must not create or migrate the database.
        await AssertProbe(client, "/health", HttpStatusCode.OK, "Healthy");

        await using (var db = fixture.Context()) await db.Database.MigrateAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.GetAsync("/health/ready")));
        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
            }
        }
        await using (var db = fixture.Context())
        {
            Assert.Empty(await db.Users.ToListAsync());
            Assert.Empty(await db.Portfolios.ToListAsync());
            Assert.Empty(await db.Transactions.ToListAsync());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            await db.Database.EnsureDeletedAsync();
        }
        await AssertProbe(client, "/health/ready", HttpStatusCode.ServiceUnavailable, "Unhealthy");
        await AssertProbe(client, "/health", HttpStatusCode.OK, "Healthy");
        await using (var db = fixture.Context()) await db.Database.MigrateAsync();
        await AssertProbe(client, "/health/ready", HttpStatusCode.OK, "Healthy");
        Assert.Equal(0, external.Calls);
    }

    [LocalDbFact]
    public async Task Pending_migrations_and_an_unavailable_authentication_schema_are_not_ready()
    {
        await using var fixture = new DatabaseFixture();
        await using (var db = fixture.Context())
            await db.GetService<IMigrator>().MigrateAsync("20261031191523_AddUserPreferredLanguage");
        using var app = ReadinessApiTests.CreateApp(connection: fixture.Connection);
        using var client = ReadinessApiTests.Client(app);
        await AssertProbe(client, "/health/ready", HttpStatusCode.ServiceUnavailable, "Unhealthy");
        await using (var db = fixture.Context())
        {
            Assert.Contains("20261031205259_AddUserSessionVersion", await db.Database.GetPendingMigrationsAsync());
            await db.Database.MigrateAsync();
        }
        await AssertProbe(client, "/health/ready", HttpStatusCode.OK, "Healthy");
        await using (var db = fixture.Context())
            await db.Database.ExecuteSqlRawAsync("EXEC sp_rename N'dbo.Users', N'UsersUnavailableForReadinessTest'");
        await AssertProbe(client, "/health/ready", HttpStatusCode.ServiceUnavailable, "Unhealthy");
        await AssertProbe(client, "/health", HttpStatusCode.OK, "Healthy");
        await using (var db = fixture.Context())
        {
            Assert.Empty(await db.Database.GetPendingMigrationsAsync()); // History alone cannot prove the auth query works.
            await db.Database.ExecuteSqlRawAsync("EXEC sp_rename N'dbo.UsersUnavailableForReadinessTest', N'Users'");
        }
        await AssertProbe(client, "/health/ready", HttpStatusCode.OK, "Healthy");
    }

    private static async Task AssertProbe(HttpClient client, string path, HttpStatusCode status, string body)
    {
        using var response = await client.GetAsync(path).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
    }

    private sealed class DatabaseFixture : IAsyncDisposable
    {
        private readonly string name = $"StockLabReadinessTests_{Guid.NewGuid():N}";
        public string Connection => $"Server=(localdb)\\MSSQLLocalDB;Database={name};Integrated Security=true;TrustServerCertificate=true;Connect Timeout=2";
        public StockLabDbContext Context() => new(new DbContextOptionsBuilder<StockLabDbContext>().UseSqlServer(Connection).Options);
        public async Task<bool> ExistsAsync()
        {
            var builder = new SqlConnectionStringBuilder(Connection) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT DB_ID(@name)";
            command.Parameters.AddWithValue("@name", name);
            return await command.ExecuteScalarAsync() is not DBNull;
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = Context();
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class RejectExternalRequests : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("Health probes must not spend external provider credits.");
        }
    }
    private sealed class LocalDbFactAttribute : FactAttribute
    {
        public LocalDbFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
    private sealed class LocalDbTheoryAttribute : TheoryAttribute
    {
        public LocalDbTheoryAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
}
