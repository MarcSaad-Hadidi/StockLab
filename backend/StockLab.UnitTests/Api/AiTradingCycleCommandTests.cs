using System.Security.Cryptography;
using System.Diagnostics;
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

    [Fact]
    public Task Named_multi_asset_policy_executes_a_valid_score_and_rejects_a_different_policy_on_replay()
        => AssertNamedPolicyAsync(sql: false);

    [LocalDbFact]
    [Trait("Category", "SqlServer")]
    public Task Named_multi_asset_policy_preserves_real_sql_cash_and_replay_identity()
        => AssertNamedPolicyAsync(sql: true);

    private async Task AssertNamedPolicyAsync(bool sql)
    {
        await using var f = await AiRiskExecutionTests.Fixture(sql: sql);
        var inference = new AiDecisionPipelineTests.Inference();
        inference.Batch = inference.Batch with { Decisions = [
            new("AAPL", AiTradingSignal.Buy, .67m),
            new("MSFT", AiTradingSignal.Sell, .80m),
            new("NVDA", AiTradingSignal.Hold, .80m)] };
        await using var services = Services(f, inference, policy: new AiRiskOptions { MinimumConfidence = .65m });
        var settings = Settings();
        if (sql)
        {
            await using var context = f.CreateDbContext();
            settings["AiTradingCycle:ExpectedDatabase"] = context.Database.GetDbConnection().Database;
        }
        settings["AiTradingCycle:RiskPolicy"] = "daily-multi-asset-v1";
        Assert.Equal(0, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        await using var reader = f.CreateDbContext();
        Assert.Single(await reader.AiTrades.ToArrayAsync());
        var journalPath = Directory.GetFiles(Path.Combine(root, "state"), "session-*.json").Single();
        using var journal = JsonDocument.Parse(File.ReadAllText(journalPath));
        Assert.Equal(2, journal.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal("daily-multi-asset-v1", journal.RootElement.GetProperty("RiskPolicy").GetString());
        var before = await f.StateAsync();
        Assert.Equal(0, await AiTradingCycleCommand.RunAsync(services, settings, new StringWriter()));
        Assert.Equal(before, await f.StateAsync());
        settings["AiTradingCycle:RiskPolicy"] = null;
        await using var legacy = Services(f, inference);
        var output = new StringWriter();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(legacy, settings, output));
        Assert.Contains("JOURNAL", output.ToString());
        Assert.Equal(before, await f.StateAsync());
        Assert.Equal(1, inference.Calls);
    }

    [Theory]
    [InlineData(null, .65)]
    [InlineData("unknown", .65)]
    [InlineData("daily-multi-asset-v1", .64)]
    [InlineData("daily-multi-asset-v1", .70)]
    public async Task Unmatched_risk_policy_blocks_before_inference(string? profile, double confidence)
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var inference = new AiDecisionPipelineTests.Inference();
        await using var services = Services(f, inference, policy: new AiRiskOptions { MinimumConfidence = (decimal)confidence });
        var settings = Settings();
        settings["AiTradingCycle:RiskPolicy"] = profile;
        var before = await f.StateAsync();
        var output = new StringWriter();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(services, settings, output));
        Assert.Contains("POLICY", output.ToString());
        Assert.Equal(0, inference.Calls);
        Assert.Equal(before, await f.StateAsync());
    }

    [Theory]
    [InlineData("exposure")]
    [InlineData("allocation")]
    [InlineData("positions")]
    [InlineData("short")]
    public async Task Named_policy_rejects_changed_portfolio_limits(string changed)
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var inference = new AiDecisionPipelineTests.Inference();
        var policy = new AiRiskOptions { MinimumConfidence = .65m };
        if (changed == "exposure") policy.MaxPositionExposurePercent = .21m;
        if (changed == "allocation") policy.MaxCashAllocationPerTradePercent = .11m;
        if (changed == "positions") policy.MaxOpenPositions = 11;
        if (changed == "short") policy.AllowShortSelling = true;
        await using var services = Services(f, inference, policy: policy);
        var settings = Settings();
        settings["AiTradingCycle:RiskPolicy"] = "daily-multi-asset-v1";
        var before = await f.StateAsync();
        var output = new StringWriter();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(services, settings, output));
        Assert.Contains("POLICY", output.ToString());
        Assert.Equal(0, inference.Calls);
        Assert.Equal(before, await f.StateAsync());
    }

    [Theory]
    [InlineData("0.65", false)]
    [InlineData("0.70", true)]
    public async Task Explicit_baseline_wins_over_conflicting_layered_configuration(string confidence, bool ready)
    {
        var prefix = "STOCKLAB_POLICY_TEST_" + Guid.NewGuid().ToString("N") + "_";
        try
        {
            Environment.SetEnvironmentVariable(prefix + "AiTradingCycle__RiskPolicy", "daily-multi-asset-v1");
            Environment.SetEnvironmentVariable(prefix + "AiTrader__Risk__MinimumConfidence", confidence);
            var settings = new ConfigurationBuilder().AddInMemoryCollection(Settings().AsEnumerable())
                .AddEnvironmentVariables(prefix).AddCommandLine(["--AiTradingCycle:RiskPolicy=baseline-v1"]).Build();
            settings["AiTradingCycle:CheckOnly"] = "true";
            var policy = new AiRiskOptions();
            settings.GetSection(AiRiskOptions.SectionName).Bind(policy);
            await using var f = await AiRiskExecutionTests.Fixture();
            var inference = new AiDecisionPipelineTests.Inference();
            await using var services = Services(f, inference, policy: policy);
            var output = new StringWriter();
            Assert.Equal(ready ? 0 : 1, await AiTradingCycleCommand.RunAsync(services, settings, output));
            Assert.Contains(ready ? "READY" : "POLICY", output.ToString());
            Assert.Equal(0, inference.Calls);
        }
        finally
        {
            Environment.SetEnvironmentVariable(prefix + "AiTradingCycle__RiskPolicy", null);
            Environment.SetEnvironmentVariable(prefix + "AiTrader__Risk__MinimumConfidence", null);
        }
    }

    [LocalDbFact]
    [Trait("Category", "SqlServer")]
    public async Task Actual_entry_does_not_inherit_named_policy_when_command_line_does_not_opt_in()
    {
        await using var f = await AiRiskExecutionTests.Fixture(sql: true);
        await using var db = f.CreateDbContext();
        foreach (var confidence in new[] { "0.65", "0.70" })
        {
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["AiTradingCycle__RiskPolicy"] = "daily-multi-asset-v1";
            start.Environment["AiTrader__Risk__MinimumConfidence"] = confidence;
            foreach (var argument in new[] { typeof(AiTradingCycleCommand).Assembly.Location, "--run-ai-trader-cycle",
                "--ConnectionStrings:StockLab=" + db.Database.GetConnectionString(),
                "--AiTradingCycle:ExpectedDatabase=" + db.Database.GetDbConnection().Database,
                "--AiTradingCycle:CheckOnly=true", "--MarketData:Provider=TwelveData",
                "--AiTraderMetadata:RegistryPath=" + Path.Combine(root, "PRIVATE_MISSING_REGISTRY.json") })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
                var output = await stdout + await stderr;
                Assert.Equal(1, process.ExitCode);
                using var result = JsonDocument.Parse(output);
                Assert.Equal(confidence == "0.65" ? "POLICY" : "MODEL", result.RootElement.GetProperty("stage").GetString());
                Assert.DoesNotContain("PRIVATE_MISSING", output);
            }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_scope_cannot_take_over_matching_durable_decisions(bool completed)
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var inference = new AiDecisionPipelineTests.Inference();
        inference.Batch = inference.Batch with { Decisions = [
            new("AAPL", AiTradingSignal.Buy, completed ? .90m : .67m),
            new("MSFT", AiTradingSignal.Sell, .80m), new("NVDA", AiTradingSignal.Hold, .80m)] };
        await using var original = Services(f, inference, risk: completed ? null : new UnavailableExecution());
        var settings = Settings();
        Assert.Equal(completed ? 0 : 1, await AiTradingCycleCommand.RunAsync(original, settings, new StringWriter()));
        var before = await f.StateAsync();
        var other = Path.Combine(root, "other-state");
        settings["AiTradingCycle:StateDirectory"] = other;
        settings["AiTradingCycle:RiskPolicy"] = "daily-multi-asset-v1";
        await using var candidate = Services(f, inference, policy: new AiRiskOptions { MinimumConfidence = .65m });
        var output = new StringWriter();
        Assert.Equal(1, await AiTradingCycleCommand.RunAsync(candidate, settings, output));
        Assert.Contains("JOURNAL", output.ToString());
        Assert.Equal(before, await f.StateAsync());
        Assert.Equal(1, inference.Calls);
        if (Directory.Exists(other)) Assert.Empty(Directory.GetFiles(other, "session-*.json"));
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

    private sealed class UnavailableExecution : IAiRiskExecutionService
    {
        public Task<AiRiskExecutionResult> ExecuteAsync(Guid id, CancellationToken token = default)
            => Task.FromException<AiRiskExecutionResult>(new IOException("test-only interruption before execution"));
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
