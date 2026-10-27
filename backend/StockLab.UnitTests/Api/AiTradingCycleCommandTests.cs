using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StockLab.Api.Operations;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;
using StockLab.UnitTests.Trading;

namespace StockLab.UnitTests.Api;

public sealed class AiTradingCycleCommandTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "stocklab-cycle-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Complete_cycle_records_real_outcomes_and_retry_does_not_reinfer_or_reexecute()
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var inference = new AiDecisionPipelineTests.Inference();
        await using var services = Services(f, inference);
        var settings = Settings();
        var output = new StringWriter();
        Assert.Equal(0, await AiTradingCycleCommand.RunAsync(services, settings, output));
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("COMPLETED", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, result.RootElement.GetProperty("executed").GetInt32());
        Assert.Equal(1, result.RootElement.GetProperty("rejected").GetInt32());
        Assert.Equal(1, result.RootElement.GetProperty("hold").GetInt32());
        await using var reader = f.CreateDbContext();
        Assert.Equal(3, await reader.AiDecisions.CountAsync());
        Assert.Single(await reader.AiTrades.ToArrayAsync());
        Assert.Equal(90000m, (await reader.AiTraderPortfolios.SingleAsync()).CashBalance);
        var snapshots = await reader.AiTraderValuationSnapshots.CountAsync();
        var before = await f.StateAsync();
        var calls = f.ExecutionMarket.Calls.Count + f.ValuationMarket.Calls.Count;
        inference.Failure = new InvalidOperationException("must not infer again");
        Assert.Equal(0, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        Assert.Equal(1, inference.Calls);
        Assert.Equal(calls, f.ExecutionMarket.Calls.Count + f.ValuationMarket.Calls.Count);
        Assert.Equal(snapshots, await reader.AiTraderValuationSnapshots.CountAsync());
        Assert.Equal(before, await f.StateAsync());
    }

    [LocalDbFact]
    [Trait("Category", "SqlServer")]
    public async Task Sql_server_cycle_settles_and_captures_once_with_the_real_services()
    {
        await using var f = await AiRiskExecutionTests.Fixture(sql: true);
        var inference = new AiDecisionPipelineTests.Inference();
        await using var services = Services(f, inference);
        var settings = Settings();
        await using var reader = f.CreateDbContext();
        settings["AiTradingCycle:ExpectedDatabase"] = reader.Database.GetDbConnection().Database;
        Assert.Equal(0, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        var before = await f.StateAsync();
        inference.Failure = new InvalidOperationException("must reuse persisted decisions");
        Assert.Equal(0, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        Assert.Equal(before, await f.StateAsync());
        Assert.Equal(1, inference.Calls);
        Assert.Single(await reader.AiTrades.ToArrayAsync());
        Assert.Equal(90000m, (await reader.AiTraderPortfolios.SingleAsync()).CashBalance);
    }

    [Fact]
    public async Task Interrupted_after_committed_trade_resumes_the_original_ids_and_preserves_cash()
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var inference = new AiDecisionPipelineTests.Inference();
        var interrupted = new InterruptAfterTrade(AiRiskExecutionTests.Service(f));
        await using var services = Services(f, inference, interrupted);
        var settings = Settings();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        await using var reader = f.CreateDbContext();
        Assert.Single(await reader.AiTrades.ToArrayAsync());
        inference.Failure = new InvalidOperationException("saved decisions must be reused");
        Assert.Equal(0, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        Assert.Equal(1, inference.Calls);
        Assert.Single(await reader.AiTrades.ToArrayAsync());
        Assert.Equal(90000m, (await reader.AiTraderPortfolios.SingleAsync()).CashBalance);
    }

    [Theory]
    [InlineData("database")]
    [InlineData("mock")]
    [InlineData("prepared_date")]
    [InlineData("hash")]
    [InlineData("version")]
    public async Task Invalid_prerequisites_fail_before_any_decision_or_order(string bad)
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var inference = new AiDecisionPipelineTests.Inference();
        await using var services = Services(f, inference);
        var settings = Settings();
        settings[bad switch {
            "database" => "AiTradingCycle:ExpectedDatabase", "mock" => "MarketData:Provider",
            "prepared_date" => "AiTradingCycle:PreparedUtcDate", "hash" => "AiTradingCycle:DatasetSha256",
            _ => "AiTradingCycle:ModelVersion" }] = bad == "mock" ? "Mock" : "private-invalid-value";
        var before = await f.StateAsync();
        var output = new StringWriter();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(services, settings, output));
        Assert.DoesNotContain("private-invalid-value", output.ToString());
        if (bad == "database")
        {
            using var failure = JsonDocument.Parse(output.ToString());
            Assert.Equal("DATABASE_TARGET", failure.RootElement.GetProperty("stage").GetString());
        }
        Assert.Equal(0, inference.Calls);
        Assert.Equal(before, await f.StateAsync());
    }

    [Fact]
    public async Task Interrupted_after_capture_reuses_the_persisted_observation_without_revaluing()
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var inference = new AiDecisionPipelineTests.Inference();
        var history = new AiTraderValuationHistoryService(f,
            new AiTraderPortfolioService(f, f.ValuationMarket, new AiCurrentPositionsFixture.Clock()));
        await using var services = Services(f, inference, valuation: new InterruptAfterCapture(history));
        var settings = Settings();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        await using var reader = f.CreateDbContext();
        var snapshots = await reader.AiTraderValuationSnapshots.CountAsync();
        var calls = f.ExecutionMarket.Calls.Count + f.ValuationMarket.Calls.Count;
        Assert.Equal(0, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        Assert.Equal(snapshots, await reader.AiTraderValuationSnapshots.CountAsync());
        Assert.Equal(calls, f.ExecutionMarket.Calls.Count + f.ValuationMarket.Calls.Count);
        Assert.Single(await reader.AiTrades.ToArrayAsync());
    }

    [Fact]
    public async Task Cancellation_precedes_any_new_history_or_journal()
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var inference = new AiDecisionPipelineTests.Inference();
        await using var services = Services(f, inference);
        var settings = Settings();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var before = await f.StateAsync();
        var output = new StringWriter();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(services, settings, output, cancelled.Token));
        Assert.Contains("CANCELLED", output.ToString());
        Assert.Equal(0, inference.Calls);
        Assert.Equal(before, await f.StateAsync());
        Assert.False(Directory.Exists(Path.Combine(root, "state")));
    }

    [Fact]
    public async Task Effective_confidence_below_seventy_percent_blocks_preflight_and_inference()
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var inference = new AiDecisionPipelineTests.Inference();
        await using var services = Services(f, inference, policy: new AiRiskOptions { MinimumConfidence = .50m });
        var settings = Settings();
        settings["AiTradingCycle:CheckOnly"] = "true";
        var output = new StringWriter();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(services, settings, output));
        using var failure = JsonDocument.Parse(output.ToString());
        Assert.Equal("POLICY", failure.RootElement.GetProperty("stage").GetString());
        Assert.Equal(0, inference.Calls);
        await using var reader = f.CreateDbContext();
        Assert.Empty(await reader.AiTrades.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Old_or_future_execution_quotes_never_settle_a_trade(bool future)
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        f.ExecutionMarket.Quote = (symbol, _) => new(symbol, "USD", 100, null, null, null,
            future ? AiCurrentPositionsFixture.Now.AddHours(1) : AiCurrentPositionsFixture.Now.AddDays(-2));
        await using var services = Services(f, new AiDecisionPipelineTests.Inference());
        var settings = Settings();
        await using var reader = f.CreateDbContext();
        var captures = await reader.AiTraderValuationSnapshots.CountAsync();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        Assert.Empty(await reader.AiTrades.ToArrayAsync());
        Assert.Equal(100000m, (await reader.AiTraderPortfolios.SingleAsync()).CashBalance);
        Assert.Equal(captures, await reader.AiTraderValuationSnapshots.CountAsync());
    }

    [Fact]
    public async Task Old_valuation_quote_blocks_capture_and_a_fresh_retry_reconciles_the_committed_trade()
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        f.ValuationMarket.Quote = (symbol, _) => new(symbol, "USD", 100, null, null, null,
            AiCurrentPositionsFixture.Now.AddDays(-2));
        await using var services = Services(f, new AiDecisionPipelineTests.Inference());
        var settings = Settings();
        await using var reader = f.CreateDbContext();
        var captures = await reader.AiTraderValuationSnapshots.CountAsync();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        Assert.Single(await reader.AiTrades.ToArrayAsync());
        Assert.Equal(captures, await reader.AiTraderValuationSnapshots.CountAsync());
        f.ValuationMarket.Quote = null;
        Assert.Equal(0, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        Assert.Single(await reader.AiTrades.ToArrayAsync());
        Assert.Equal(captures + 1, await reader.AiTraderValuationSnapshots.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restart_on_a_new_session_reconciles_old_commits_without_executing_stale_signals(bool afterCapture)
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var history = new AiTraderValuationHistoryService(f,
            new AiTraderPortfolioService(f, f.ValuationMarket, new AiCurrentPositionsFixture.Clock()));
        await using var first = Services(f, new AiDecisionPipelineTests.Inference(),
            risk: afterCapture ? null : new InterruptAfterTrade(AiRiskExecutionTests.Service(f)),
            valuation: afterCapture ? new InterruptAfterCapture(history) : null);
        var settings = Settings();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(first, settings, new StringWriter()));
        var nextTime = AiCurrentPositionsFixture.Now.AddDays(3);
        f.ExecutionMarket.Quote = f.ValuationMarket.Quote = (symbol, _) => new(symbol, "USD", 100, null, null, null, nextTime);
        var batch = AiDecisionPipelineTests.Batch();
        var inference = new AiDecisionPipelineTests.Inference { Batch = batch with {
            DecisionDate = new(2026, 10, 5), Decisions = batch.Decisions.Select(d => d with { Signal = AiTradingSignal.Hold }).ToArray() } };
        await using var next = Services(f, inference, clock: new FixedClock(nextTime), quoteSession: new(2026, 10, 5));
        settings["AiTradingCycle:DecisionDate"] = "2026-10-05";
        settings["AiTradingCycle:PreparedUtcDate"] = "2026-10-06";
        Assert.Equal(0, await AiTradingCycleCommand.RunAsync(next, settings, new StringWriter()));
        using var old = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "state", "session-2026-10-02.json")));
        Assert.Equal(afterCapture ? "COMPLETED" : "ABANDONED", old.RootElement.GetProperty("Stage").GetString());
        var captureId = old.RootElement.GetProperty("CaptureId").GetGuid();
        await using var reader = f.CreateDbContext();
        Assert.True(await reader.AiTraderValuationSnapshots.AnyAsync(p => p.CaptureId == captureId));
        Assert.Single(await reader.AiTrades.ToArrayAsync());
        Assert.Equal(6, await reader.AiDecisions.CountAsync());
        Assert.Equal(90000m, (await reader.AiTraderPortfolios.SingleAsync()).CashBalance);
        Assert.Equal(afterCapture ? 2 : 1, await reader.AiExecutionAttempts.CountAsync());
    }

    [Fact]
    public async Task Failed_reconciliation_blocks_new_session_inference()
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        await using var first = Services(f, new AiDecisionPipelineTests.Inference(),
            risk: new InterruptAfterTrade(AiRiskExecutionTests.Service(f)));
        var settings = Settings();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(first, settings, new StringWriter()));
        var nextTime = AiCurrentPositionsFixture.Now.AddDays(3);
        f.ValuationMarket.Quote = (symbol, _) => new(symbol, "USD", 100, null, null, null, nextTime.AddDays(-2));
        var inference = new AiDecisionPipelineTests.Inference();
        await using var next = Services(f, inference, clock: new FixedClock(nextTime), quoteSession: new(2026, 10, 5));
        settings["AiTradingCycle:DecisionDate"] = "2026-10-05";
        settings["AiTradingCycle:PreparedUtcDate"] = "2026-10-06";
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(next, settings, new StringWriter()));
        Assert.Equal(0, inference.Calls);
        await using var reader = f.CreateDbContext();
        Assert.Equal(3, await reader.AiDecisions.CountAsync());
        Assert.Single(await reader.AiTrades.ToArrayAsync());
    }

    [Fact]
    public async Task Changed_input_and_corrupt_journal_do_not_overwrite_or_repair_history()
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var inference = new AiDecisionPipelineTests.Inference();
        await using var services = Services(f, inference);
        var settings = Settings();
        Assert.Equal(0, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        var before = await f.StateAsync();
        File.AppendAllText(settings["AiDecisionPipeline:DatasetPath"]!, "changed");
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        File.WriteAllText(settings["AiDecisionPipeline:DatasetPath"]!, "synthetic-test-input");
        var journal = Directory.GetFiles(Path.Combine(root, "state"), "session-*.json").Single();
        File.WriteAllText(journal, "{\"private-corrupt\":true}");
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        Assert.Equal("{\"private-corrupt\":true}", File.ReadAllText(journal));
        Assert.Equal(before, await f.StateAsync());
    }

    [Fact]
    public async Task Concurrent_owner_and_preflight_never_start_inference()
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var inference = new AiDecisionPipelineTests.Inference();
        await using var services = Services(f, inference);
        var settings = Settings();
        settings["AiTradingCycle:CheckOnly"] = "true";
        Assert.Equal(0, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        settings["AiTradingCycle:CheckOnly"] = "false";
        Directory.CreateDirectory(Path.Combine(root, "state"));
        using var lease = new FileStream(Path.Combine(root, "state", "cycle.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var output = new StringWriter();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(services, settings, output));
        Assert.Equal(0, inference.Calls);
        await using var reader = f.CreateDbContext();
        Assert.Empty(await reader.AiDecisions.ToArrayAsync());
    }

    private IConfiguration Settings()
    {
        Directory.CreateDirectory(root);
        var dataset = Path.Combine(root, "input.csv");
        File.WriteAllText(dataset, "synthetic-test-input");
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["AiTradingCycle:ExpectedDatabase"] = "main", ["AiTradingCycle:DecisionDate"] = "2026-10-02",
            ["AiTradingCycle:PreparedUtcDate"] = "2026-10-03", ["AiTradingCycle:StateDirectory"] = Path.Combine(root, "state"),
            ["AiTradingCycle:DatasetSha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(dataset))),
            ["AiTradingCycle:ModelName"] = "random_forest_baseline", ["AiTradingCycle:ModelVersion"] = "v000001",
            ["AiDecisionPipeline:DatasetPath"] = dataset, ["MarketData:Provider"] = "TwelveData"
        }).Build();
    }

    private static ServiceProvider Services(AiCurrentPositionsFixture f, IAiDecisionInferenceProvider inference,
        IAiRiskExecutionService? risk = null, IAiTraderValuationHistoryService? valuation = null, AiRiskOptions? policy = null,
        TimeProvider? clock = null, DateOnly? quoteSession = null)
    {
        clock ??= new AiCurrentPositionsFixture.Clock();
        f.ExecutionMarket.Prices["MSFT"] = 100m;
        var executionQuotes = new AiTradingCycleMarketDataProvider(f.ExecutionMarket, clock, quoteSession ?? AiDecisionPipelineTests.Session);
        var valuationQuotes = new AiTradingCycleMarketDataProvider(f.ValuationMarket, clock, quoteSession ?? AiDecisionPipelineTests.Session);
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<StockLabDbContext>>(f);
        services.AddScoped(_ => f.CreateDbContext());
        services.AddSingleton(clock);
        services.AddSingleton(inference);
        services.AddSingleton<IOptions<AiRiskOptions>>(Options.Create(policy ?? new AiRiskOptions()));
        services.AddSingleton<IOptions<AiDecisionPipelineOptions>>(Options.Create(new AiDecisionPipelineOptions {
            Symbols = ["AAPL", "MSFT", "NVDA"] }));
        services.AddSingleton<IAiModelCatalogReadService>(new Catalog());
        services.AddScoped<IAiDecisionHistoryService, AiDecisionHistoryService>();
        services.AddScoped<IAiDecisionOutcomeReadService, AiDecisionOutcomeReadService>();
        services.AddScoped<IAiDecisionPipelineService, AiDecisionPipelineService>();
        services.AddSingleton<IAiRiskExecutionService>(risk ?? AiRiskExecutionTests.Service(f, policy, quotes: executionQuotes, timeProvider: clock));
        services.AddSingleton<IAiTraderPortfolioService>(new AiTraderPortfolioService(f, valuationQuotes, clock));
        services.AddSingleton<IAiTraderValuationHistoryService>(valuation ?? new AiTraderValuationHistoryService(f,
            new AiTraderPortfolioService(f, valuationQuotes, clock)));
        return services.BuildServiceProvider();
    }

    private sealed class Catalog : IAiModelCatalogReadService
    {
        public Task<IReadOnlyList<AiModelCatalogItem>> GetAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<AiModelCatalogItem>>([new("random_forest_baseline", "v000001", "random_forest",
                Guid.Parse("00000000-0000-0000-0000-000000000001"), AiCurrentPositionsFixture.Now.AddDays(-10),
                "Active", .6, .6, .6, .6, new(new(2025, 1, 1), new(2025, 12, 31)), new(new(2026, 1, 1), new(2026, 9, 30)))]);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InterruptAfterTrade(IAiRiskExecutionService inner) : IAiRiskExecutionService
    {
        private bool interrupted;
        public async Task<AiRiskExecutionResult> ExecuteAsync(Guid id, CancellationToken token = default)
        {
            var result = await inner.ExecuteAsync(id, token);
            if (!interrupted && result.Status == "EXECUTED") { interrupted = true; throw new IOException("private interrupted response"); }
            return result;
        }
    }

    private sealed class InterruptAfterCapture(IAiTraderValuationHistoryService inner) : IAiTraderValuationHistoryService
    {
        private bool interrupted;
        public Task<AiTraderEquityObservation> CaptureCurrentAsync(Guid id, CancellationToken token = default) => inner.CaptureCurrentAsync(id, token);
        public Task<IReadOnlyList<AiTraderEquityObservation>> GetHistoryAsync(DateTime? start = null, DateTime? end = null,
            int limit = 200, CancellationToken token = default) => inner.GetHistoryAsync(start, end, limit, token);
        public async Task<AiTraderEquityObservation> CaptureOnceAsync(Guid id, CancellationToken token = default)
        {
            var observation = await inner.CaptureOnceAsync(id, token);
            if (!interrupted) { interrupted = true; throw new IOException("private interrupted capture response"); }
            return observation;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            Directory.Delete(root, recursive: true);
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
