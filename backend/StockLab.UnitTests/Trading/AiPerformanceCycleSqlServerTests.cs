using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StockLab.Api.Operations;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

[Trait("Category", "SqlServer")]
public sealed class AiPerformanceCycleSqlServerTests
{
    [LocalDbFact]
    public async Task Concurrent_workers_commit_one_slot_and_return_its_immutable_result()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        await f.ExecuteAsync();
        var gated = new GatedQuotes(f.ValuationMarket);
        var history = new AiTraderValuationHistoryService(f, new AiTraderPortfolioService(f, gated, new AiCurrentPositionsFixture.Clock()));
        await using var db = f.CreateDbContext();
        var service = AiPerformanceCycleTests.Service(f, target: db.Database.GetDbConnection().Database, history: history);
        async Task<object> Run()
        {
            try { return await service.RunOnceAsync(); }
            catch (AiTraderPerformanceException error) { return error; }
        }
        var results = await Task.WhenAll(Run(), Run()).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.NotEmpty(results.OfType<AiPerformanceCycleResult>());
        Assert.All(results.OfType<AiTraderPerformanceException>(), error => Assert.Equal(AiTraderPerformanceFailure.ConcurrencyConflict, error.Category));
        var replay = await service.RunOnceAsync();
        Assert.True(replay.IsReplay);
        Assert.All(results.OfType<AiPerformanceCycleResult>(), result => Assert.Equal(replay.Cycle.Valuation.Id, result.Cycle.Valuation.Id));
        Assert.Equal(2, await db.AiTraderValuationSnapshots.CountAsync());
        Assert.Single((await service.GetByIdAsync(replay.Cycle.CaptureId))!.Trades);
    }

    [LocalDbFact]
    public async Task Migration_preserves_existing_observations_and_manual_capture_contract()
    {
        const string previous = "20261026041947_AddAiRiskExecutionAttempts";
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database=StockLabCycleUpgrade_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true";
        await using var f = new AiCurrentPositionsFixture(connection, null);
        await using var db = f.CreateDbContext();
        await db.GetService<IMigrator>().MigrateAsync(previous);
        var portfolioId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var now = AiCurrentPositionsFixture.Now.UtcDateTime;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AiPortfolios (Id,Name,InitialCapital,CashBalance,Currency,CreatedAtUtc,UpdatedAtUtc) VALUES ({portfolioId},'AI_TRADER',100000,100000,'USD',{now},{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AiPortfolioSnapshots (Id,AiPortfolioId,CaptureId,ValuedAtUtc,CashBalance,PositionsValue,Currency,IsInitialBaseline,PortfolioVersion,QuoteObservationsJson) VALUES ({id},{portfolioId},{captureId},{now},100000,0,'USD',0,{Array.Empty<byte>()},'[]')");
        await db.Database.MigrateAsync();
        var point = await db.AiTraderValuationSnapshots.SingleAsync();
        Assert.Equal(id, point.Id);
        Assert.Null(point.CycleSlotUtc);
        Assert.Null(point.TradeContextJson);
        await db.GetService<IMigrator>().MigrateAsync(previous);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM AiPortfolioSnapshots").SingleAsync());
        await db.Database.MigrateAsync();
        Assert.Null((await db.AiTraderValuationSnapshots.AsNoTracking().SingleAsync()).CycleSlotUtc);
    }

    [LocalDbFact]
    public async Task Pending_migrations_prevent_quotes_and_do_not_apply_schema_implicitly()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        await using var db = f.CreateDbContext();
        var latest = (await db.Database.GetAppliedMigrationsAsync()).Last();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM __EFMigrationsHistory WHERE MigrationId = {latest}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => AiPerformanceCycleTests.Service(f,
            target: db.Database.GetDbConnection().Database).RunOnceAsync());
        Assert.Empty(f.ValuationMarket.Calls);
        Assert.Single(await db.AiTraderValuationSnapshots.ToArrayAsync());
    }

    [LocalDbFact]
    public async Task Actual_operator_captures_once_replays_and_sanitizes_database_failures()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        await f.ExecuteAsync();
        var first = await RunCommand(f);
        Assert.Equal(0, first.Code);
        Assert.Contains("replay=False", first.Output);
        Assert.DoesNotContain("Now listening on", first.Output);
        var before = await f.StateAsync();
        var second = await RunCommand(f);
        Assert.Equal(0, second.Code);
        Assert.Contains("replay=True", second.Output);
        Assert.Equal(before, await f.StateAsync());
        await using var db = f.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("EXEC sp_rename 'AiPortfolioSnapshots', 'AiPortfolioSnapshots_PRIVATE'");
        var failed = await RunCommand(f);
        Assert.Equal(1, failed.Code);
        Assert.Contains("performance_cycle_failed", failed.Output);
        Assert.DoesNotContain("SqlException", failed.Output);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", failed.Output);
        Assert.DoesNotContain("PRIVATE", failed.Output);
    }

    private static async Task<(int Code, string Output)> RunCommand(AiCurrentPositionsFixture f)
    {
        await using var db = f.CreateDbContext();
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        foreach (var value in new[] { typeof(AiPerformanceCycleCommand).Assembly.Location, "--capture-ai-performance",
            "--ConnectionStrings:StockLab=" + db.Database.GetConnectionString(), "--MarketData:Provider=Mock",
            "--AiPerformanceCycle:ExpectedDatabase=" + db.Database.GetDbConnection().Database, "--AiPerformanceCycle:Interval=1.00:00:00" })
            start.ArgumentList.Add(value);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            return (process.ExitCode, await stdout + await stderr);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }

    private sealed class GatedQuotes(IMarketDataProvider inner) : IMarketDataProvider
    {
        private int calls;
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == 2) release.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return await inner.GetQuoteAsync(symbol, cancellationToken);
        }
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class LocalDbFactAttribute : FactAttribute
    {
        public LocalDbFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
}
