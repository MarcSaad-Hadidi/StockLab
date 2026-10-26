using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using StockLab.Api.Operations;
using StockLab.Infrastructure.Trading;
using StockLab.UnitTests.Api;

namespace StockLab.UnitTests.Trading;

[Trait("Category", "SqlServer")]
public sealed class AiDecisionPipelineSqlServerTests
{
    [LocalDbFact]
    public async Task Actual_entry_point_never_echoes_raw_SQL_logs_on_persistence_failure()
    {
        await using var db = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await using var reader = db.CreateDbContext();
        await reader.Database.ExecuteSqlRawAsync("EXEC sp_rename 'AiDecisions', 'AiDecisions_PRIVATE_LOG_MARKER'");
        using var python = new PythonAiDecisionInferenceProviderTests.ProcessFixture("""
            print('{"schema_version":1,"model_name":"random_forest_baseline","model_version":"v000001","decision_date":"2026-10-02","decisions":[{"symbol":"AAPL","signal":"HOLD","confidence":"0.9"}]}')
            """);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        foreach (var argument in new[] { typeof(AiDecisionPipelineCommand).Assembly.Location, "--run-ai-decisions",
            "--ConnectionStrings:StockLab=" + reader.Database.GetConnectionString(),
            "--AiDecisionPipeline:ExpectedDatabase=" + reader.Database.GetDbConnection().Database,
            "--AiDecisionPipeline:DecisionDate=2026-10-02", "--AiDecisionPipeline:PythonExecutable=" + python.Settings.PythonExecutable,
            "--AiDecisionPipeline:MlDirectory=" + python.Root, "--AiDecisionPipeline:ArtifactsDirectory=" + python.Settings.ArtifactsDirectory,
            "--AiDecisionPipeline:DatasetPath=" + python.Settings.DatasetPath, "--AiTraderMetadata:RegistryPath=" + Path.Combine(python.Root, "registry.json"),
            "--AiDecisionPipeline:ModelName=random_forest_baseline", "--AiDecisionPipeline:Symbols:0=AAPL",
            "--AiDecisionPipeline:TrustedArtifacts=true", "--Logging:LogLevel:Default=Information" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
            var output = await stdout + await stderr;
            Assert.Equal(1, process.ExitCode);
            Assert.Contains("decision_pipeline_failed", output);
            Assert.DoesNotContain("Microsoft.EntityFrameworkCore", output);
            Assert.DoesNotContain("SqlException", output);
            Assert.DoesNotContain("AiDecisions", output);
            Assert.DoesNotContain("Now listening on", output);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    [LocalDbFact]
    public async Task Concurrent_cycles_preserve_exact_confidence_and_one_original_per_identity()
    {
        await using var db = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        const decimal confidence = .1234567890123456789012345678m;
        var inference = new AiDecisionPipelineTests.Inference
        {
            Batch = AiDecisionPipelineTests.Batch() with
            {
                Decisions = [new("AAPL", StockLab.Application.DTOs.AiTrader.AiTradingSignal.Hold, confidence)]
            }
        };
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            new AiDecisionPipelineService(inference, new AiDecisionHistoryService(db, new AiCurrentPositionsFixture.Clock()),
                new AiCurrentPositionsFixture.Clock()).RunAsync(AiDecisionPipelineTests.Session)));
        Assert.All(results, records => Assert.Equal(results[0].Single(), records.Single()));
        await using var reader = db.CreateDbContext();
        Assert.Equal(confidence, (await reader.AiDecisions.SingleAsync()).Confidence);
        Assert.Empty(await reader.AiTrades.ToArrayAsync());
        Assert.Empty(await reader.Portfolios.ToArrayAsync());
    }

    [LocalDbFact]
    public async Task Pending_migrations_prevent_inference_without_applying_or_repairing_them()
    {
        await using var db = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await using var reader = db.CreateDbContext();
        var latest = (await reader.Database.GetAppliedMigrationsAsync()).Last();
        await reader.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM __EFMigrationsHistory WHERE MigrationId = {latest}");
        var inference = new AiDecisionPipelineTests.Inference();
        await using var services = AiDecisionPipelineCommandTests.Services(db, inference);
        var output = new StringWriter();
        Assert.Equal(1, await AiDecisionPipelineCommand.RunAsync(services,
            AiDecisionPipelineCommandTests.Settings(reader.Database.GetDbConnection().Database), output));
        Assert.Contains("migrations_pending", output.ToString());
        Assert.Equal(0, inference.Calls);
        Assert.Empty(await reader.AiDecisions.ToArrayAsync());
        Assert.DoesNotContain(latest, await reader.Database.GetAppliedMigrationsAsync());
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
