using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiCurrentPositionsServiceTests
{
    [Fact]
    public async Task Executed_buys_and_partial_and_full_sells_expose_only_committed_current_state()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await VerifyExecutionLifecycleAsync(f);
    }

    internal static async Task VerifyExecutionLifecycleAsync(AiCurrentPositionsFixture f)
    {
        await f.InitializeAsync();
        await f.ExecuteAsync();
        Assert.Equal(new AiCurrentPosition("AAPL", 10m, 100m, 120m, 1200m, 200m),
            Assert.Single(await f.Service().GetCurrentAsync()));

        await f.ExecuteAsync(price: 120m);
        f.ValuationMarket.Prices["AAPL"] = 130m;
        var added = Assert.Single(await f.Service().GetCurrentAsync());
        Assert.Equal(new AiCurrentPosition("AAPL", 20m, 110m, 130m, 2600m, 400m), added);
        await using (var db = f.CreateDbContext())
        {
            var position = await db.AiTraderPositions.AsNoTracking().SingleAsync();
            Assert.Equal(position.Quantity, added.Quantity);
            Assert.Equal(position.AverageCost, added.AveragePrice);
            Assert.Equal(2, await db.AiTrades.CountAsync());
        }

        await f.ExecuteAsync(quantity: 4m, price: 140m, signal: AiTradingSignal.Sell);
        f.ValuationMarket.Prices["AAPL"] = 100m;
        // Realized gains from the sold shares do not enter the remaining position's P&L.
        Assert.Equal(new AiCurrentPosition("AAPL", 16m, 110m, 100m, 1600m, -160m),
            Assert.Single(await f.Service().GetCurrentAsync()));

        await f.ExecuteAsync(quantity: 16m, price: 105m, signal: AiTradingSignal.Sell);
        f.ValuationMarket.Calls.Clear();
        var before = await f.StateAsync();
        Assert.Empty(await f.Service().GetCurrentAsync());
        Assert.Empty(f.ValuationMarket.Calls);
        Assert.Equal(before, await f.StateAsync());
        Assert.Equal(0, f.Observer.SaveCalls);
        await using var final = f.CreateDbContext();
        Assert.Empty(await final.AiTraderPositions.ToArrayAsync());
        Assert.Equal(4, await final.AiTrades.CountAsync());
    }

    [Fact]
    public async Task Query_maps_the_snapshot_valuation_without_recalculating_it()
    {
        var portfolio = new SnapshotPortfolio();
        var result = Assert.Single(await new AiCurrentPositionsService(portfolio).GetCurrentAsync());
        Assert.Equal(new AiCurrentPosition("AAPL", 3.125m, 80m, 100m, 1234.5678m, -987.6543m), result);
    }

    [Fact]
    public async Task Missing_portfolio_fails_without_initialization_quotes_or_saves()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        var before = await f.StateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service().GetCurrentAsync());
        Assert.Equal(before, await f.StateAsync());
        Assert.Empty(f.ValuationMarket.Calls);
        Assert.Equal(0, f.Observer.SaveCalls);
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiTraderPortfolios.ToArrayAsync());
    }

    [Fact]
    public async Task Empty_portfolio_returns_empty_without_quotes_or_writes()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        var before = await f.StateAsync();
        Assert.Empty(await f.Service().GetCurrentAsync());
        Assert.Empty(f.ValuationMarket.Calls);
        Assert.Equal(0, f.Observer.SaveCalls);
        Assert.Equal(before, await f.StateAsync());
    }

    [Theory]
    [InlineData(10, 100, 120, 1200, 200)]
    [InlineData(10, 100, 80, 800, -200)]
    [InlineData(10, 100, 100, 1000, 0)]
    [InlineData(3.125, 80, 100, 312.5, 62.5)]
    public async Task Executed_buy_returns_persisted_quantity_cost_and_current_unrealized_pnl(
        decimal quantity, decimal executionPrice, decimal currentPrice, decimal value, decimal pnl)
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        var execution = await f.ExecuteAsync(quantity: quantity, price: executionPrice);
        f.ValuationMarket.Prices["AAPL"] = currentPrice;
        var before = await f.StateAsync();
        Assert.Equal(new AiCurrentPosition("AAPL", quantity, executionPrice, currentPrice, value, pnl),
            Assert.Single(await f.Service().GetCurrentAsync()));
        await using var db = f.CreateDbContext();
        var persisted = await db.AiTraderPositions.SingleAsync();
        Assert.Equal(execution.PositionQuantity, persisted.Quantity);
        Assert.Equal(execution.AverageCost, persisted.AverageCost);
        Assert.Equal(before, await f.StateAsync());
        Assert.Equal(new[] { "AAPL" }, f.ValuationMarket.Calls);
        Assert.Equal(0, f.Observer.SaveCalls);
    }

    [Fact]
    public async Task Symbols_are_ordered_ordinally_and_each_gets_exactly_one_matching_quote()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync("NVDA", 3m, 50m);
        await f.ExecuteAsync("AAPL", 10m, 100m);
        await f.ExecuteAsync("MSFT", 2m, 200m);
        f.ValuationMarket.Prices["NVDA"] = 40m;
        f.ValuationMarket.Prices["MSFT"] = 250m;
        var result = await f.Service().GetCurrentAsync();
        Assert.Equal(new[]
        {
            new AiCurrentPosition("AAPL", 10m, 100m, 120m, 1200m, 200m),
            new AiCurrentPosition("MSFT", 2m, 200m, 250m, 500m, 100m),
            new AiCurrentPosition("NVDA", 3m, 50m, 40m, 120m, -30m)
        }, result);
        Assert.Equal(new[] { "AAPL", "MSFT", "NVDA" }, f.ValuationMarket.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("currency")]
    [InlineData("provider-error")]
    public async Task One_bad_quote_fails_the_whole_read_without_fallback_or_mutation(string failure)
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        await f.ExecuteAsync("MSFT", 2m, 100m);
        f.ValuationMarket.Quote = (symbol, _) => symbol == "AAPL"
            ? new(symbol, "USD", 120m, null, null, null, AiCurrentPositionsFixture.Now)
            : failure switch
            {
                "missing" => null,
                "provider-error" => throw new HttpRequestException("Market data unavailable."),
                _ => new(symbol, failure == "currency" ? "CAD" : "USD",
                    failure == "zero" ? 0m : failure == "negative" ? -1m : 100m,
                    null, null, null, AiCurrentPositionsFixture.Now)
            };
        var before = await f.StateAsync();
        var error = await Record.ExceptionAsync(() => f.Service().GetCurrentAsync());
        if (failure == "provider-error") Assert.IsType<HttpRequestException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(before, await f.StateAsync());
        Assert.Equal(new[] { "AAPL", "MSFT" }, f.ValuationMarket.Calls);
        Assert.Equal(0, f.Observer.SaveCalls);
    }

    [Theory]
    [InlineData(AiTradingSignal.Buy, null)]
    [InlineData(AiTradingSignal.Buy, AiRiskRejectionReason.LowConfidence)]
    [InlineData(AiTradingSignal.Hold, null)]
    [InlineData(AiTradingSignal.Hold, AiRiskRejectionReason.HoldSignal)]
    public async Task Raw_or_rejected_decisions_do_not_create_current_positions(
        AiTradingSignal signal, AiRiskRejectionReason? rejection)
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.RecordDecisionAsync(signal, rejection);
        var before = await f.StateAsync();
        Assert.Empty(await f.Service().GetCurrentAsync());
        Assert.Empty(f.ValuationMarket.Calls);
        Assert.Equal(before, await f.StateAsync());
    }

    [Fact]
    public async Task Personal_holdings_never_appear_in_ai_positions_or_queries()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await using var db = f.CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Email = "positions@example.com", NormalizedEmail = "POSITIONS@EXAMPLE.COM",
            PasswordHash = "test-hash", DisplayName = "Test", Version = new byte[8] };
        var portfolio = new Portfolio { Id = Guid.NewGuid(), User = user, Version = new byte[8] };
        db.Holdings.Add(new() { Id = Guid.NewGuid(), Portfolio = portfolio, Symbol = "AAPL", Quantity = 999m, AverageCost = 100m });
        await db.SaveChangesAsync();
        var before = await f.StateAsync();
        Assert.Empty(await f.Service().GetCurrentAsync());
        Assert.Empty(f.ValuationMarket.Calls);
        Assert.Equal(before, await f.StateAsync());
        Assert.All(f.Observer.Commands, sql =>
        {
            Assert.DoesNotContain("Holdings", sql);
            Assert.DoesNotContain("Users", sql);
        });
    }

    [Theory]
    [InlineData("before")]
    [InlineData("during")]
    [InlineData("after-quote")]
    public async Task Cancellation_propagates_without_partial_output_or_writes(string timing)
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        using var source = new CancellationTokenSource();
        if (timing == "before") source.Cancel();
        else f.ValuationMarket.Quote = (symbol, token) =>
        {
            source.Cancel();
            if (timing == "during") token.ThrowIfCancellationRequested();
            return new(symbol, "USD", 120m, null, null, null, AiCurrentPositionsFixture.Now);
        };
        var before = await f.StateAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service().GetCurrentAsync(source.Token));
        Assert.Equal(before, await f.StateAsync());
        Assert.Equal(0, f.Observer.SaveCalls);
        if (timing == "before") Assert.Empty(f.ValuationMarket.Calls);
        else Assert.Equal(source.Token, f.ValuationMarket.LastToken);
    }

    [Fact]
    public void Api_composition_can_resolve_a_scoped_current_positions_service()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:StockLab"] = "Server=localhost;Database=StockLabTestOnly;Integrated Security=true;TrustServerCertificate=true"
        }).Build();
        var services = new ServiceCollection();
        services.AddPersistence(configuration);
        services.AddSingleton<IMarketDataProvider>(new AiCurrentPositionsFixture.Market());
        services.AddAiTraderPortfolio();
        services.AddAiCurrentPositions();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var service = first.ServiceProvider.GetRequiredService<IAiCurrentPositionsService>();
        Assert.IsType<AiCurrentPositionsService>(service);
        Assert.Same(service, first.ServiceProvider.GetRequiredService<IAiCurrentPositionsService>());
        Assert.NotSame(service, second.ServiceProvider.GetRequiredService<IAiCurrentPositionsService>());
    }

    private sealed class SnapshotPortfolio : IAiTraderPortfolioService
    {
        public Task<AiTraderPortfolioSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default, bool initializeIfMissing = true)
        {
            Assert.False(initializeIfMissing);
            return Task.FromResult(new AiTraderPortfolioSnapshot(Guid.NewGuid(), "USD", 100000m, 99000m,
                1234.5678m, 100234.5678m, 234.5678m,
                [new("AAPL", 3.125m, 80m, 100m, 1234.5678m, -987.6543m)]));
        }
        public Task<AiTraderPortfolioState> GetOrCreateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiTraderPortfolioState> GetStateAsync(CancellationToken cancellationToken = default, bool initializeIfMissing = true) => throw new NotSupportedException();
    }
}
