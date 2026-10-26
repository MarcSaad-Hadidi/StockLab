using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Api.Operations;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;
using StockLab.UnitTests.Trading;

namespace StockLab.UnitTests.Api;

public sealed class AiDecisionPipelineCommandTests
{
    [Fact]
    public async Task Operator_runs_the_real_pipeline_and_identical_retries_reuse_history()
    {
        await using var database = await AiCurrentPositionsFixture.CreateAsync();
        var inference = new AiDecisionPipelineTests.Inference();
        await using var services = Services(database, inference);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var output = new StringWriter();
            Assert.Equal(0, await AiDecisionPipelineCommand.RunAsync(services, Settings("main"), output));
            Assert.Contains("decisions_recorded: 3", output.ToString());
        }
        await using var reader = database.CreateDbContext();
        Assert.Equal(3, await reader.AiDecisions.CountAsync());
        Assert.Empty(await reader.AiTrades.ToArrayAsync());
        Assert.Empty(await reader.AiTraderPortfolios.ToArrayAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other-database")]
    public async Task Expected_database_guard_precedes_inference(string? expected)
    {
        await using var database = await AiCurrentPositionsFixture.CreateAsync();
        var inference = new AiDecisionPipelineTests.Inference();
        await using var services = Services(database, inference);
        var output = new StringWriter();
        Assert.Equal(1, await AiDecisionPipelineCommand.RunAsync(services, Settings(expected), output));
        Assert.Contains("database_target_mismatch", output.ToString());
        Assert.Equal(0, inference.Calls);
        await using var reader = database.CreateDbContext();
        Assert.Empty(await reader.AiDecisions.ToArrayAsync());
    }

    [Fact]
    public async Task Invalid_session_and_private_inference_errors_are_safe_console_failures()
    {
        await using var database = await AiCurrentPositionsFixture.CreateAsync();
        var inference = new AiDecisionPipelineTests.Inference { Failure = new InvalidOperationException("private marker and credentials") };
        await using var services = Services(database, inference);
        foreach (var date in new[] { "invalid-private-date", "2026-10-02" })
        {
            var output = new StringWriter();
            Assert.Equal(1, await AiDecisionPipelineCommand.RunAsync(services, Settings("main", date), output));
            Assert.DoesNotContain("private marker", output.ToString());
            Assert.DoesNotContain("invalid-private-date", output.ToString());
            Assert.DoesNotContain("credentials", output.ToString());
        }
        Assert.Equal(1, inference.Calls);
    }

    [Fact]
    public async Task Cancellation_is_reported_without_writes()
    {
        await using var database = await AiCurrentPositionsFixture.CreateAsync();
        var inference = new AiDecisionPipelineTests.Inference();
        await using var services = Services(database, inference);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var output = new StringWriter();
        Assert.Equal(1, await AiDecisionPipelineCommand.RunAsync(services, Settings("main"), output, cancel.Token));
        Assert.Contains("decision_pipeline_cancelled", output.ToString());
        Assert.Equal(0, inference.Calls);
    }

    internal static IConfiguration Settings(string? database, string date = "2026-10-02") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiDecisionPipeline:ExpectedDatabase"] = database, ["AiDecisionPipeline:DecisionDate"] = date
        }).Build();

    internal static ServiceProvider Services(AiCurrentPositionsFixture database, IAiDecisionInferenceProvider inference)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<StockLabDbContext>>(database);
        services.AddScoped(_ => database.CreateDbContext());
        services.AddSingleton<TimeProvider>(new AiCurrentPositionsFixture.Clock());
        services.AddSingleton(inference);
        services.AddScoped<IAiDecisionHistoryService, AiDecisionHistoryService>();
        services.AddScoped<IAiDecisionPipelineService, AiDecisionPipelineService>();
        return services.BuildServiceProvider();
    }
}
