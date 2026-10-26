using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using StockLab.Api.Operations;
using StockLab.UnitTests.Api;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

[Trait("Category", "SqlServer")]
public sealed class AiRiskExecutionSqlServerTests
{
    [LocalDbFact]
    public async Task Identical_refusal_after_another_worker_commits_is_attributed_as_a_replay()
    {
        await using var f = await AiRiskExecutionTests.Fixture(sql: true);
        var raw = await AiRiskExecutionTests.Record(f, AiTradingSignal.Buy);
        var quotes = new ConcurrentQuotes();
        var policy = new AiRiskOptions { MinimumConfidence = .99m };
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new DelayedRisk(new AiRiskManager(new AiTraderPortfolioService(f, quotes,
            new AiCurrentPositionsFixture.Clock()), Options.Create(policy)), finished.Task);
        async Task<AiRiskExecutionResult> First()
        {
            try { return await AiRiskExecutionTests.Service(f, policy, quotes: quotes).ExecuteAsync(raw.Id); }
            finally { finished.TrySetResult(); }
        }
        var results = await Task.WhenAll(First(), AiRiskExecutionTests.Service(f, policy, risk: delayed, quotes: quotes)
            .ExecuteAsync(raw.Id)).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.All(results, result => Assert.Equal("REJECTED", result.Status));
        Assert.False(results[0].IsReplay);
        Assert.True(results[1].IsReplay);
        await using var db = f.CreateDbContext();
        Assert.Single(await db.AiRejectedDecisions.ToArrayAsync());
        var attempts = await db.AiExecutionAttempts.ToArrayAsync();
        Assert.Equal(2, attempts.Length);
        Assert.Single(attempts, a => a.IsReplay);
    }

    [LocalDbFact]
    public async Task Actual_operator_entry_executes_once_with_mock_prices_and_replays_without_changes()
    {
        await using var f = await AiRiskExecutionTests.Fixture(sql: true);
        var raw = await AiRiskExecutionTests.Record(f, AiTradingSignal.Buy);
        var first = await RunCommand(f, raw.Id);
        Assert.Equal(0, first.Code);
        Assert.Contains("executed=1", first.Output);
        Assert.DoesNotContain("Now listening on", first.Output);
        var before = await f.StateAsync();
        var second = await RunCommand(f, raw.Id);
        Assert.Equal(0, second.Code);
        Assert.Contains("replayed=1", second.Output);
        Assert.Equal(before, await f.StateAsync());
        await using var db = f.CreateDbContext();
        Assert.Single(await db.AiTrades.ToArrayAsync());
        Assert.Single(await db.AiExecutionAttempts.ToArrayAsync());
        Assert.Empty(await db.Portfolios.ToArrayAsync());
        Assert.Empty(await db.Transactions.ToArrayAsync());
    }

    [LocalDbFact]
    public async Task Actual_entry_sanitizes_provider_logs_when_the_journal_table_is_unavailable()
    {
        await using var f = await AiRiskExecutionTests.Fixture(sql: true);
        var raw = await AiRiskExecutionTests.Record(f, AiTradingSignal.Buy);
        await using var db = f.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("EXEC sp_rename 'AiExecutionAttempts', 'AiExecutionAttempts_PRIVATE_LOG_MARKER'");
        var result = await RunCommand(f, raw.Id);
        Assert.Equal(1, result.Code);
        Assert.Contains("risk_execution_failed", result.Output);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", result.Output);
        Assert.DoesNotContain("SqlException", result.Output);
        Assert.DoesNotContain("AiExecutionAttempts", result.Output);
        Assert.Empty(await db.AiTrades.ToArrayAsync());
    }

    [LocalDbFact]
    public async Task Pending_migrations_block_the_operator_without_implicit_schema_changes()
    {
        await using var f = await AiRiskExecutionTests.Fixture(sql: true);
        var raw = await AiRiskExecutionTests.Record(f, AiTradingSignal.Buy);
        await using var db = f.CreateDbContext();
        var latest = (await db.Database.GetAppliedMigrationsAsync()).Last();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM __EFMigrationsHistory WHERE MigrationId = {latest}");
        await using var services = AiRiskExecutionCommandTests.Services(f);
        var output = new StringWriter();
        Assert.Equal(1, await AiRiskExecutionCommand.RunAsync(services,
            AiRiskExecutionCommandTests.Settings(db.Database.GetDbConnection().Database, raw.Id.ToString()), output));
        Assert.Contains("migrations_pending", output.ToString());
        Assert.Empty(f.ExecutionMarket.Calls);
        Assert.Empty(await db.AiExecutionAttempts.ToArrayAsync());
        Assert.DoesNotContain(latest, await db.Database.GetAppliedMigrationsAsync());
    }

    [LocalDbFact]
    public async Task Concurrent_workers_commit_one_trade_and_reconcile_open_attempts_on_replay()
    {
        await using var f = await AiRiskExecutionTests.Fixture(sql: true);
        var raw = await AiRiskExecutionTests.Record(f, AiTradingSignal.Buy);
        var quotes = new ConcurrentQuotes();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { return (object)await AiRiskExecutionTests.Service(f, quotes: quotes).ExecuteAsync(raw.Id); }
            catch (AiTradeExecutionException error) { return error; }
        })).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Contains(results, r => r is AiRiskExecutionResult { Status: "EXECUTED" });
        foreach (var error in results.OfType<AiTradeExecutionException>())
            Assert.Contains(error.Category, new[] { AiTradeExecutionFailure.PersistenceFailure, AiTradeExecutionFailure.ConcurrencyConflict });
        var replay = await AiRiskExecutionTests.Service(f).ExecuteAsync(raw.Id);
        Assert.Equal("EXECUTED", replay.Status);
        Assert.True(replay.IsReplay);
        await using var db = f.CreateDbContext();
        Assert.Single(await db.AiTrades.ToArrayAsync());
        Assert.Empty(await db.AiRejectedDecisions.ToArrayAsync());
        Assert.Equal(90000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
        Assert.DoesNotContain(await db.AiExecutionAttempts.ToArrayAsync(), a => a.Status == "STARTED");
        Assert.Empty(await db.Portfolios.ToArrayAsync());
    }

    [LocalDbFact]
    public async Task Concurrent_refusal_and_execution_have_one_authoritative_terminal_outcome()
    {
        await using var f = await AiRiskExecutionTests.Fixture(sql: true);
        var raw = await AiRiskExecutionTests.Record(f, AiTradingSignal.Buy);
        var quotes = new ConcurrentQuotes();
        var results = await Task.WhenAll(new[] { .7m, .99m }.Select(async minimum =>
        {
            try { return (object)await AiRiskExecutionTests.Service(f,
                new AiRiskOptions { MinimumConfidence = minimum }, quotes: quotes).ExecuteAsync(raw.Id); }
            catch (AiTradeExecutionException error) { return error; }
        })).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Contains(results, r => r is AiRiskExecutionResult { Status: "EXECUTED" or "REJECTED" });
        foreach (var error in results.OfType<AiTradeExecutionException>())
            Assert.Contains(error.Category, new[] { AiTradeExecutionFailure.PersistenceFailure, AiTradeExecutionFailure.ConcurrencyConflict });
        var final = await AiRiskExecutionTests.Service(f).ExecuteAsync(raw.Id);
        await using var db = f.CreateDbContext();
        var trades = await db.AiTrades.CountAsync();
        var refusals = await db.AiRejectedDecisions.CountAsync();
        Assert.Equal(1, trades + refusals);
        Assert.Equal(trades == 1 ? "EXECUTED" : "REJECTED", final.Status);
        Assert.Equal(trades == 1 ? 90000m : 100000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
        Assert.DoesNotContain(await db.AiExecutionAttempts.ToArrayAsync(), a => a.Status == "STARTED");
    }

    [LocalDbFact]
    public async Task Migrated_journal_rejects_incoherent_statuses_null_completion_and_half_approvals()
    {
        await using var f = await AiRiskExecutionTests.Fixture(sql: true);
        var raw = await AiRiskExecutionTests.Record(f, AiTradingSignal.Buy);
        foreach (var invalid in new[] { "status", "completion", "approval", "trade" })
        {
            await using var db = f.CreateDbContext();
            var row = new AiExecutionAttempt { Id = Guid.NewGuid(), DecisionId = raw.Id,
                StartedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime };
            if (invalid == "status") row.Status = "started";
            if (invalid == "completion") { row.Status = "FAILED"; row.FailureCode = "QuoteUnavailable"; }
            if (invalid == "approval") row.RequestedPrice = 100m;
            if (invalid == "trade") { row.Status = "EXECUTED"; row.CompletedAtUtc = row.StartedAtUtc; }
            db.AiExecutionAttempts.Add(row);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using var reader = f.CreateDbContext();
        Assert.Empty(await reader.AiExecutionAttempts.ToArrayAsync());
    }

    private sealed class ConcurrentQuotes : IMarketDataProvider
    {
        private int calls;
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref calls);
            if (count <= 2)
            {
                if (count == 2) release.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return new(symbol, "USD", 100m, null, null, null, AiCurrentPositionsFixture.Now);
        }
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DelayedRisk(IAiRiskManager inner, Task gate) : IAiRiskManager
    {
        public async Task<AiRiskDecision> EvaluateAsync(AiRiskRequest request, CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return await inner.EvaluateAsync(request, cancellationToken);
        }
    }

    private static async Task<(int Code, string Output)> RunCommand(AiCurrentPositionsFixture f, Guid id)
    {
        await using var db = f.CreateDbContext();
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        foreach (var argument in new[] { typeof(AiRiskExecutionCommand).Assembly.Location, "--execute-ai-decisions",
            "--ConnectionStrings:StockLab=" + db.Database.GetConnectionString(),
            "--AiRiskExecution:ExpectedDatabase=" + db.Database.GetDbConnection().Database,
            "--AiRiskExecution:DecisionIds:0=" + id.ToString("D"), "--MarketData:Provider=Mock", "--Logging:LogLevel:Default=Information" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            return (process.ExitCode, await stdout + await stderr);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
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
}
