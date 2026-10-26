using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Api.Operations;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;
using StockLab.UnitTests.Trading;

namespace StockLab.UnitTests.Api;

public sealed class AiRiskExecutionCommandTests
{
    [Fact]
    public async Task Explicit_decision_batch_executes_in_order_and_replays_without_new_mutations()
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var buy = await AiRiskExecutionTests.Record(f, AiTradingSignal.Buy);
        var hold = await AiRiskExecutionTests.Record(f, AiTradingSignal.Hold);
        var sell = await AiRiskExecutionTests.Record(f, AiTradingSignal.Sell);
        await using var services = Services(f);
        var settings = Settings("main", buy.Id.ToString(), hold.Id.ToString(), sell.Id.ToString());
        var output = new StringWriter();
        Assert.Equal(0, await AiRiskExecutionCommand.RunAsync(services, settings, output));
        Assert.Contains("executed=2", output.ToString());
        Assert.Contains("hold=1", output.ToString());
        var before = await f.StateAsync();
        f.ExecutionMarket.Quote = (_, _) => throw new InvalidOperationException("must not quote on retry");
        Assert.Equal(0, await AiRiskExecutionCommand.RunAsync(services, settings, new StringWriter()));
        Assert.Equal(before, await f.StateAsync());
        await using var db = f.CreateDbContext();
        Assert.Equal(2, await db.AiExecutionAttempts.CountAsync());
        Assert.Equal(2, await db.AiTrades.CountAsync());
        Assert.Equal(100000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
        Assert.Empty(await db.Portfolios.ToArrayAsync());
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("malformed")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("database")]
    public async Task Invalid_batch_or_database_is_rejected_before_any_attempt(string invalid)
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var raw = await AiRiskExecutionTests.Record(f, AiTradingSignal.Buy);
        var ids = invalid switch
        {
            "empty" => Array.Empty<string>(), "malformed" => [raw.Id.ToString(), "private-invalid-id"],
            "duplicate" => [raw.Id.ToString(), raw.Id.ToString()], "unknown" => [raw.Id.ToString(), Guid.NewGuid().ToString()],
            _ => new[] { raw.Id.ToString() }
        };
        await using var services = Services(f);
        var before = await f.StateAsync();
        var output = new StringWriter();
        Assert.Equal(1, await AiRiskExecutionCommand.RunAsync(services, Settings(invalid == "database" ? "other" : "main", ids), output));
        Assert.DoesNotContain("private-invalid-id", output.ToString());
        Assert.Equal(before, await f.StateAsync());
        Assert.Empty(f.ExecutionMarket.Calls);
    }

    [Fact]
    public async Task Technical_failure_is_journaled_and_returns_nonzero_without_hiding_other_outcomes()
    {
        await using var f = await AiRiskExecutionTests.Fixture();
        var buy = await AiRiskExecutionTests.Record(f, AiTradingSignal.Buy);
        var hold = await AiRiskExecutionTests.Record(f, AiTradingSignal.Hold);
        f.ExecutionMarket.Quote = (_, _) => null;
        await using var services = Services(f);
        var output = new StringWriter();
        Assert.Equal(1, await AiRiskExecutionCommand.RunAsync(services, Settings("main", buy.Id.ToString(), hold.Id.ToString()), output));
        Assert.Contains("failed=1", output.ToString());
        Assert.Contains("hold=1", output.ToString());
        await using var db = f.CreateDbContext();
        Assert.Equal("FAILED", (await db.AiExecutionAttempts.SingleAsync()).Status);
        Assert.Empty(await db.AiTrades.ToArrayAsync());
    }

    [Fact]
    public async Task Uninitialized_portfolio_and_cancellation_do_not_initialize_or_execute()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        var raw = await AiRiskExecutionTests.Record(f, AiTradingSignal.Buy);
        await using var services = Services(f);
        var output = new StringWriter();
        Assert.Equal(1, await AiRiskExecutionCommand.RunAsync(services, Settings("main", raw.Id.ToString()), output));
        Assert.Contains("portfolio_not_initialized", output.ToString());
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        output = new StringWriter();
        Assert.Equal(1, await AiRiskExecutionCommand.RunAsync(services, Settings("main", raw.Id.ToString()), output, cancel.Token));
        Assert.Contains("risk_execution_cancelled", output.ToString());
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiTraderPortfolios.ToArrayAsync());
        Assert.Empty(await db.AiExecutionAttempts.ToArrayAsync());
    }

    internal static IConfiguration Settings(string database, params string[] ids)
    {
        var values = new Dictionary<string, string?> { ["AiRiskExecution:ExpectedDatabase"] = database };
        for (var i = 0; i < ids.Length; i++) values[$"AiRiskExecution:DecisionIds:{i}"] = ids[i];
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    internal static ServiceProvider Services(AiCurrentPositionsFixture f)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => f.CreateDbContext());
        services.AddScoped<IAiTraderPortfolioService>(_ => new AiTraderPortfolioService(f, f.ValuationMarket, new AiCurrentPositionsFixture.Clock()));
        services.AddScoped<IAiRiskExecutionService>(_ => AiRiskExecutionTests.Service(f));
        return services.BuildServiceProvider();
    }
}
