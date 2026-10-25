using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Api.Operations;
using StockLab.Infrastructure.AiTraderMetadata;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;
using StockLab.UnitTests.Trading;

namespace StockLab.UnitTests.Api;

public sealed class AiTraderSetupCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Check_validates_both_real_readers_but_never_initializes_or_changes_the_database(bool initialized)
    {
        await using var database = await AiCurrentPositionsFixture.CreateAsync();
        if (initialized) await database.InitializeAsync();
        await using var services = Services(database, new AiTraderMetadataOptions
        {
            RegistryPath = FixturePath("registry"), BacktestReportPaths = [FixturePath("backtest")]
        });
        var before = await database.StateAsync();
        var output = new StringWriter();
        Assert.Equal(initialized ? 0 : 1, await AiTraderSetupCommand.RunAsync(services, Settings(null), false, output));
        Assert.Contains("models_ready: 1", output.ToString());
        Assert.Contains("backtests_ready: 1", output.ToString());
        Assert.Contains(initialized ? "portfolio_ready" : "portfolio_not_initialized", output.ToString());
        Assert.Equal(before, await database.StateAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Check_keeps_unavailable_and_invalid_catalogs_distinct_and_sanitizes_failures(bool invalid)
    {
        await using var database = await AiCurrentPositionsFixture.CreateAsync();
        await database.InitializeAsync();
        var file = Path.Combine(Path.GetTempPath(), $"stocklab-private-setup-{Guid.NewGuid():N}.json");
        try
        {
            if (invalid) await File.WriteAllTextAsync(file, "private invalid content");
            await using var services = Services(database, new AiTraderMetadataOptions
            {
                RegistryPath = file, BacktestReportPaths = [FixturePath("backtest")]
            });
            var before = await database.StateAsync();
            var output = new StringWriter();
            Assert.Equal(1, await AiTraderSetupCommand.RunAsync(services, Settings(null), false, output));
            Assert.Contains(invalid ? "models_invalid" : "models_unavailable", output.ToString());
            Assert.Contains("backtests_ready: 1", output.ToString());
            Assert.DoesNotContain(file, output.ToString());
            Assert.DoesNotContain("private invalid content", output.ToString());
            Assert.Equal(before, await database.StateAsync());
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public async Task Initialization_requires_the_named_database_and_never_writes_to_a_different_target()
    {
        await using var database = await AiCurrentPositionsFixture.CreateAsync();
        await using var services = Services(database);
        foreach (var expected in new string?[] { null, "different-database" })
        {
            var output = new StringWriter();
            Assert.Equal(1, await AiTraderSetupCommand.RunAsync(services, Settings(expected), true, output));
            Assert.Contains("database_target_mismatch", output.ToString());
            await using var reader = database.CreateDbContext();
            Assert.Empty(await reader.AiTraderPortfolios.ToArrayAsync());
        }
    }

    [LocalDbFact]
    public async Task Initialization_refuses_pending_migrations_without_creating_or_updating_schema()
    {
        await using var database = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await using var connection = database.CreateDbContext();
        var latest = (await connection.Database.GetAppliedMigrationsAsync()).Last();
        await connection.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM __EFMigrationsHistory WHERE MigrationId = {latest}");
        var applied = (await connection.Database.GetAppliedMigrationsAsync()).ToArray();
        await using var services = Services(database);
        var before = await database.StateAsync();
        var output = new StringWriter();
        Assert.Equal(1, await AiTraderSetupCommand.RunAsync(services, Settings(connection.Database.GetDbConnection().Database), true, output));
        Assert.Contains("migrations_pending", output.ToString());
        Assert.Equal(before, await database.StateAsync());
        await using var reader = database.CreateDbContext();
        Assert.Equal(applied, await reader.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Initialization_is_idempotent_preserves_cash_positions_and_user_portfolios_and_adds_one_baseline()
    {
        await using var database = await AiCurrentPositionsFixture.CreateAsync();
        await using var services = Services(database);
        var settings = Settings("main");
        var output = new StringWriter();
        Assert.Equal(0, await AiTraderSetupCommand.RunAsync(services, settings, true, output));
        Assert.Contains("portfolio_ready", output.ToString());
        await using (var writer = database.CreateDbContext())
        {
            var ai = await writer.AiTraderPortfolios.SingleAsync();
            ai.CashBalance = 75000m;
            writer.AiTraderPositions.Add(new AiTraderPosition
            {
                Id = Guid.NewGuid(), AiTraderPortfolioId = ai.Id, Symbol = "TEST", Quantity = 1.25m,
                AverageCost = 100m, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            });
            writer.Portfolios.Add(new Portfolio
            {
                Id = Guid.NewGuid(), CashBalance = 54321m,
                User = new User { Id = Guid.NewGuid(), DisplayName = "Test", Email = "setup@example.test",
                    NormalizedEmail = "SETUP@EXAMPLE.TEST", PasswordHash = "test-only" }
            });
            await writer.SaveChangesAsync();
        }
        var before = await database.StateAsync();
        Assert.Equal(0, await AiTraderSetupCommand.RunAsync(services, settings, true, new StringWriter()));
        Assert.Equal(before, await database.StateAsync());
        await using var reader = database.CreateDbContext();
        Assert.Single(await reader.AiTraderPortfolios.ToArrayAsync());
        Assert.True((await reader.AiTraderValuationSnapshots.SingleAsync()).IsInitialBaseline);
        Assert.Empty(await reader.AiTrades.ToArrayAsync());
        Assert.Empty(database.ValuationMarket.Calls);
    }

    private static IConfiguration Settings(string? database) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["AiTraderSetup:ExpectedDatabase"] = database }).Build();

    private static ServiceProvider Services(AiCurrentPositionsFixture database, AiTraderMetadataOptions? metadata = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<StockLabDbContext>>(database);
        services.AddScoped(_ => database.CreateDbContext());
        services.AddScoped<IAiTraderPortfolioService>(_ => new AiTraderPortfolioService(database, database.ValuationMarket, TimeProvider.System));
        services.Configure<AiTraderMetadataOptions>(options =>
        {
            options.RegistryPath = metadata?.RegistryPath;
            options.BacktestReportPaths = metadata?.BacktestReportPaths ?? [];
        });
        services.AddScoped<IAiModelCatalogReadService, JsonAiModelCatalogReadService>();
        services.AddScoped<IAiBacktestCatalogReadService, JsonAiBacktestCatalogReadService>();
        return services.BuildServiceProvider();
    }

    private static string FixturePath(string name, [System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "AiTraderMetadata", "Fixtures", $"test-only-{name}.json"));

    private sealed class LocalDbFactAttribute : FactAttribute
    {
        public LocalDbFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
}
