using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiTraderPortfolioServiceTests
{
    [Theory]
    [InlineData(AiTradingSignal.Buy)]
    [InlineData(AiTradingSignal.Sell)]
    public async Task Risk_evaluation_never_initializes_a_missing_portfolio(AiTradingSignal signal)
    {
        await using var fixture = await Fixture.CreateAsync();
        var manager = new AiRiskManager(fixture.Service(), Options.Create(new AiRiskOptions()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.EvaluateAsync(new AiRiskRequest("AAPL", signal, 0.8m, 100m)));
        Assert.Contains("initialized", error.Message);
        await using var reader = fixture.CreateDbContext();
        Assert.Empty(await reader.AiTraderPortfolios.ToListAsync());
        Assert.Empty(await reader.AiTraderPositions.ToListAsync());
        Assert.Empty(await reader.Transactions.ToListAsync());
        Assert.Empty(fixture.Market.Calls);
    }

    [Fact]
    public async Task Risk_uses_only_ai_cash_and_leaves_all_database_state_unchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(2500m, ("AAPL", 12.5m, 50m), ("OTHER", 962.5m, 20m));
        fixture.Market.Quote = symbol => Quote(symbol, 100m);
        await using var context = fixture.CreateDbContext();
        var userPortfolio = new Portfolio
        {
            Id = Guid.NewGuid(), CashBalance = 1000000m,
            User = new User { Id = Guid.NewGuid(), DisplayName = "Test", Email = "risk@example.com",
                NormalizedEmail = "RISK@EXAMPLE.COM", PasswordHash = "test-hash" }
        };
        context.Portfolios.Add(userPortfolio);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var before = System.Text.Json.JsonSerializer.Serialize(new
        {
            Portfolio = await context.AiTraderPortfolios.AsNoTracking().SingleAsync(),
            Positions = await context.AiTraderPositions.AsNoTracking().OrderBy(p => p.Symbol).ToArrayAsync()
        });
        var manager = new AiRiskManager(fixture.Service(), Options.Create(new AiRiskOptions()));
        var buy = await manager.EvaluateAsync(new("AAPL", AiTradingSignal.Buy, 0.8m, 100m));
        Assert.True(buy.Approved);
        Assert.Equal(25m, buy.ApprovedQuantity);
        var sell = await manager.EvaluateAsync(new("AAPL", AiTradingSignal.Sell, 0.8m, 100m));
        Assert.True(sell.Approved);
        Assert.Equal(12.5m, sell.ApprovedQuantity);
        Assert.False((await manager.EvaluateAsync(new("AAPL", AiTradingSignal.Buy, 0.6m, 100m))).Approved);
        Assert.False((await manager.EvaluateAsync(new("MISSING", AiTradingSignal.Sell, 0.8m, 100m))).Approved);
        Assert.False((await manager.EvaluateAsync(new("AAPL", AiTradingSignal.Hold, 0.8m, 100m))).Approved);
        var after = System.Text.Json.JsonSerializer.Serialize(new
        {
            Portfolio = await context.AiTraderPortfolios.AsNoTracking().SingleAsync(),
            Positions = await context.AiTraderPositions.AsNoTracking().OrderBy(p => p.Symbol).ToArrayAsync()
        });
        Assert.Equal(before, after); // Includes cash, quantity, cost, timestamps and rowversion.
        Assert.Equal(1000000m, (await context.Portfolios.AsNoTracking().SingleAsync()).CashBalance);
        Assert.Empty(await context.Transactions.ToListAsync());
        Assert.Empty(await context.Holdings.ToListAsync());
        Assert.Equal(["AAPL", "OTHER"], fixture.Market.Calls);
    }

    [Fact]
    public async Task Initialization_is_idempotent_and_a_new_service_preserves_cash_and_positions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service().GetOrCreateAsync();
        Assert.Equal(100000m, first.InitialCapital);
        Assert.Equal(100000m, first.CashBalance);
        Assert.Equal("USD", first.Currency);
        Assert.Empty(first.Positions);
        await using (var writer = fixture.CreateDbContext())
        {
            var row = await writer.AiTraderPortfolios.SingleAsync();
            Assert.Equal("AI_TRADER", row.PortfolioKey);
            Assert.Equal(Fixture.Now.UtcDateTime, row.CreatedAtUtc);
            Assert.Equal(row.CreatedAtUtc, row.UpdatedAtUtc);
            row.CashBalance = 75000m;
            writer.AiTraderPositions.Add(Position(row.Id, "BRK.B", 1.23456789m, 123.4567m));
            await writer.SaveChangesAsync();
        }
        var second = await fixture.Service().GetOrCreateAsync();
        Assert.Equal(first.PortfolioId, second.PortfolioId);
        Assert.Equal(75000m, second.CashBalance);
        Assert.Equal(100000m, second.InitialCapital);
        var position = Assert.Single(second.Positions);
        Assert.Equal("BRK.B", position.Symbol);
        Assert.Equal(1.23456789m, position.Quantity);
        Assert.Equal(123.4567m, position.AverageCost);
        await using var reader = fixture.CreateDbContext();
        Assert.Equal(1, await reader.AiTraderPortfolios.CountAsync());
        Assert.Empty(await reader.Users.ToListAsync());
        Assert.Empty(await reader.Portfolios.ToListAsync());
    }

    [Fact]
    public async Task Deleting_a_user_and_their_portfolio_does_not_change_ai_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var ai = await fixture.Service().GetOrCreateAsync();
        await using var context = fixture.CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(), DisplayName = "Test", Email = "test@example.com",
            NormalizedEmail = "TEST@EXAMPLE.COM", PasswordHash = "test-hash"
        };
        var portfolio = new Portfolio { Id = Guid.NewGuid(), User = user };
        context.Portfolios.Add(portfolio);
        await context.SaveChangesAsync();
        Assert.NotEqual(ai.PortfolioId, portfolio.Id);
        context.Portfolios.Remove(portfolio);
        await context.SaveChangesAsync();
        context.Users.Remove(user);
        await context.SaveChangesAsync();
        Assert.Equal(ai.PortfolioId, (await context.AiTraderPortfolios.SingleAsync()).Id);
        Assert.Equal(100000m, (await fixture.Service().GetStateAsync()).CashBalance);
    }

    [Fact]
    public async Task Empty_snapshot_has_initial_value_and_never_requests_quotes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var snapshot = await fixture.Service().GetSnapshotAsync();
        Assert.Equal(100000m, snapshot.InitialCapital);
        Assert.Equal(100000m, snapshot.CashBalance);
        Assert.Equal(0m, snapshot.PositionsMarketValue);
        Assert.Equal(100000m, snapshot.TotalValue);
        Assert.Equal(0m, snapshot.PnL);
        Assert.Empty(snapshot.Positions);
        Assert.Empty(fixture.Market.Calls);
    }

    [Fact]
    public async Task Snapshot_uses_current_prices_and_does_not_persist_valuation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(98000m, ("AAPL", 10m, 100m));
        fixture.Market.Quote = symbol => Quote(symbol, 120m);
        var snapshot = await fixture.Service().GetSnapshotAsync();
        var position = Assert.Single(snapshot.Positions);
        Assert.Equal(120m, position.CurrentPrice);
        Assert.Equal(1200m, position.MarketValue);
        Assert.Equal(200m, position.UnrealizedPnL);
        Assert.Equal(1200m, snapshot.PositionsMarketValue);
        Assert.Equal(99200m, snapshot.TotalValue);
        Assert.Equal(-800m, snapshot.PnL);
        fixture.Market.Quote = symbol => Quote(symbol, 130m);
        Assert.Equal(99300m, (await fixture.Service().GetSnapshotAsync()).TotalValue);
        var state = await fixture.Service().GetStateAsync();
        Assert.Equal(98000m, state.CashBalance);
        Assert.Equal(100m, Assert.Single(state.Positions).AverageCost);
        Assert.Equal(["AAPL", "AAPL"], fixture.Market.Calls);
    }

    [Fact]
    public async Task Multiple_positions_keep_symbols_and_sum_decimal_values_with_one_quote_each()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(97000m, ("BRK.B", 2.5m, 100m), ("TSLA:NASDAQ", 3m, 200m));
        fixture.Market.Quote = symbol => Quote(symbol, symbol == "BRK.B" ? 120.1234m : 190m);
        var snapshot = await fixture.Service().GetSnapshotAsync();
        Assert.Equal(870.30850m, snapshot.PositionsMarketValue);
        Assert.Equal(97870.30850m, snapshot.TotalValue);
        Assert.Equal(-2129.69150m, snapshot.PnL);
        Assert.Equal(50.30850m, snapshot.Positions.Single(p => p.Symbol == "BRK.B").UnrealizedPnL);
        Assert.Equal(-30m, snapshot.Positions.Single(p => p.Symbol == "TSLA:NASDAQ").UnrealizedPnL);
        Assert.Equal(["BRK.B", "TSLA:NASDAQ"], fixture.Market.Calls.Order());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("currency")]
    [InlineData("zero")]
    [InlineData("negative")]
    public async Task Invalid_quote_fails_the_whole_snapshot_but_state_stays_available(string failure)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(98000m, ("AAPL", 10m, 100m), ("MSFT", 1m, 200m));
        fixture.Market.Quote = symbol => symbol == "AAPL" ? Quote(symbol, 120m) : failure switch
        {
            "missing" => null,
            "currency" => Quote(symbol, 120m) with { Currency = "CAD" },
            "zero" => Quote(symbol, 0m),
            _ => Quote(symbol, -1m)
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service().GetSnapshotAsync());
        Assert.Contains("MSFT", error.Message);
        var state = await fixture.Service().GetStateAsync();
        Assert.Equal(98000m, state.CashBalance);
        Assert.Equal(2, state.Positions.Count);
    }

    [Fact]
    public async Task Provider_failure_and_cancellation_propagate_without_fallback()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(98000m, ("AAPL", 10m, 100m));
        var failure = new HttpRequestException("Quote unavailable");
        fixture.Market.Quote = _ => throw failure;
        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Service().GetSnapshotAsync()));
        using var cancellation = new CancellationTokenSource();
        fixture.Market.Quote = _ => { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); return null; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service().GetSnapshotAsync(cancellation.Token));
        Assert.Equal(cancellation.Token, fixture.Market.LastToken);
        Assert.Equal(98000m, (await fixture.Service().GetStateAsync()).CashBalance);
    }

    [Theory]
    [InlineData("cash", -1)]
    [InlineData("capital", 0)]
    [InlineData("capital", 90000)]
    [InlineData("quantity", 0)]
    [InlineData("quantity", -1)]
    [InlineData("cost", 0)]
    [InlineData("cost", -1)]
    public async Task Database_rejects_invalid_persisted_values(string field, int value)
    {
        await using var fixture = await Fixture.CreateAsync();
        var state = await fixture.Service().GetOrCreateAsync();
        await using var context = fixture.CreateDbContext();
        var row = await context.AiTraderPortfolios.SingleAsync();
        if (field == "cash") row.CashBalance = value;
        else if (field == "capital") row.InitialCapital = value;
        else context.AiTraderPositions.Add(Position(state.PortfolioId, "AAPL",
            field == "quantity" ? value : 1m, field == "cost" ? value : 100m));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(275, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Database_rejects_blank_portfolio_names(string name)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var context = fixture.CreateDbContext();
        context.AiTraderPortfolios.Add(new AiTraderPortfolio { Id = Guid.NewGuid(), PortfolioKey = name });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(275, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task Database_rejects_duplicate_portfolios_and_positions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var state = await fixture.Service().GetOrCreateAsync();
        await using var context = fixture.CreateDbContext();
        context.AiTraderPortfolios.Add(new AiTraderPortfolio { Id = Guid.NewGuid() });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(2067, Assert.IsType<SqliteException>(duplicate.InnerException).SqliteExtendedErrorCode);
        context.ChangeTracker.Clear();
        context.AiTraderPositions.AddRange(Position(state.PortfolioId, "AAPL", 1m, 100m), Position(state.PortfolioId, "AAPL", 2m, 200m));
        duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(2067, Assert.IsType<SqliteException>(duplicate.InnerException).SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task Unrelated_database_failure_is_not_swallowed_even_if_another_creator_succeeded()
    {
        await using var fixture = await Fixture.CreateAsync();
        var failure = new DbUpdateException("Unrelated save failure");
        var interceptor = new BeforeSaveInterceptor(async () =>
        {
            await fixture.Service().GetOrCreateAsync();
            throw failure;
        });
        var options = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite(fixture.Connection)
            .AddInterceptors(interceptor).Options;
        var service = new AiTraderPortfolioService(new SqliteFactory(options), fixture.Market, new FixedClock());
        Assert.Same(failure, await Assert.ThrowsAsync<DbUpdateException>(() => service.GetOrCreateAsync()));
    }

    private static StockQuote Quote(string symbol, decimal price) => new(symbol, "USD", price, null, null, null, Fixture.Now);

    private static AiTraderPosition Position(Guid id, string symbol, decimal quantity, decimal cost) => new()
    {
        Id = Guid.NewGuid(), AiTraderPortfolioId = id, Symbol = symbol, Quantity = quantity, AverageCost = cost,
        CreatedAtUtc = Fixture.Now.UtcDateTime, UpdatedAtUtc = Fixture.Now.UtcDateTime
    };

    private sealed class Fixture(SqliteConnection connection, DbContextOptions<StockLabDbContext> options) : IAsyncDisposable
    {
        public static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        public SqliteConnection Connection => connection;
        public FakeMarket Market { get; } = new();
        public StockLabDbContext CreateDbContext() => new SqliteContext(options);
        public AiTraderPortfolioService Service() => new(new SqliteFactory(options), Market, new FixedClock());

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection, new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite(connection).Options);
            await using var context = fixture.CreateDbContext();
            await context.Database.EnsureCreatedAsync();
            return fixture;
        }

        public async Task SeedAsync(decimal cash, params (string Symbol, decimal Quantity, decimal Cost)[] positions)
        {
            var state = await Service().GetOrCreateAsync();
            await using var context = CreateDbContext();
            (await context.AiTraderPortfolios.SingleAsync()).CashBalance = cash;
            context.AiTraderPositions.AddRange(positions.Select(p => Position(state.PortfolioId, p.Symbol, p.Quantity, p.Cost)));
            await context.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class SqliteContext(DbContextOptions<StockLabDbContext> options) : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // SQLite has no SQL Server rowversion generator; production mapping is tested separately.
            modelBuilder.Entity<AiTraderPortfolio>().Property(p => p.Version).HasDefaultValueSql("randomblob(8)");
            modelBuilder.Entity<User>().Property(p => p.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(p => p.Version).ValueGeneratedNever();
        }
    }

    private sealed class SqliteFactory(DbContextOptions<StockLabDbContext> options) : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => new SqliteContext(options);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Fixture.Now;
    }

    private sealed class FakeMarket : IMarketDataProvider
    {
        public Func<string, StockQuote?> Quote { get; set; } = _ => throw new InvalidOperationException("Unexpected quote request");
        public List<string> Calls { get; } = [];
        public CancellationToken LastToken { get; private set; }
        public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            Calls.Add(symbol);
            LastToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Quote(symbol));
        }
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class BeforeSaveInterceptor(Func<Task> beforeSave) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await beforeSave();
            return result;
        }
    }
}
