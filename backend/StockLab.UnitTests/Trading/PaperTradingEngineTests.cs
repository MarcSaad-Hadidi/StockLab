using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.Trading;
using StockLab.Application.Exceptions;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class PaperTradingEngineTests
{
    [Fact]
    public async Task Buy_decreases_cash_adds_holding_and_records_transaction()
    {
        await using var fixture = await TradingFixture.CreateAsync();
        var engine = fixture.CreateEngine();

        var result = await engine.ExecuteAsync(fixture.PortfolioId,
            new PaperTradeRequest(Guid.NewGuid(), "BUY", " aapl ", 10m, 125m));

        Assert.Equal("BUY", result.Side);
        Assert.Equal("AAPL", result.Symbol);
        Assert.Equal(1_250m, result.TotalAmount);
        Assert.Equal(98_750m, result.CashBalance);
        Assert.Equal(10m, result.HoldingQuantity);
        Assert.Equal(125m, result.AverageCost!.Value);

        var portfolio = await fixture.Context.Portfolios.AsNoTracking()
            .Include(row => row.Holdings)
            .SingleAsync(row => row.Id == fixture.PortfolioId);
        Assert.Equal(98_750m, portfolio.CashBalance);
        Assert.Single(portfolio.Holdings);
        Assert.Equal(1, await fixture.Context.Transactions.CountAsync());
    }

    [Fact]
    public async Task Buy_rejects_an_order_that_exceeds_cash_without_persisting_changes()
    {
        await using var fixture = await TradingFixture.CreateAsync(initialCapital: 1_000m);
        var engine = fixture.CreateEngine();

        var error = await Assert.ThrowsAsync<PaperTradingException>(() => engine.ExecuteAsync(
            fixture.PortfolioId, new PaperTradeRequest(Guid.NewGuid(), "BUY", "AAPL", 11m, 100m)));

        Assert.Equal(PaperTradingFailure.InsufficientCash, error.Category);
        var portfolio = await fixture.Context.Portfolios.AsNoTracking()
            .SingleAsync(row => row.Id == fixture.PortfolioId);
        Assert.Equal(1_000m, portfolio.CashBalance);
        Assert.Equal(0, await fixture.Context.Holdings.CountAsync());
        Assert.Equal(0, await fixture.Context.Transactions.CountAsync());
    }

    [Fact]
    public async Task Sell_increases_cash_and_removes_a_fully_sold_holding()
    {
        await using var fixture = await TradingFixture.CreateAsync();
        await fixture.AddHoldingAsync("MSFT", 10m, 200m);
        var engine = fixture.CreateEngine();

        var result = await engine.ExecuteAsync(fixture.PortfolioId,
            new PaperTradeRequest(Guid.NewGuid(), "SELL", "MSFT", 10m, 240m));

        Assert.Equal("SELL", result.Side);
        Assert.Equal(102_400m, result.CashBalance);
        Assert.Equal(0m, result.HoldingQuantity);
        Assert.Null(result.AverageCost);
        Assert.Equal(0, await fixture.Context.Holdings.CountAsync());
        Assert.Equal(1, await fixture.Context.Transactions.CountAsync());
    }

    [Fact]
    public async Task Sell_rejects_a_quantity_above_the_current_holding()
    {
        await using var fixture = await TradingFixture.CreateAsync();
        await fixture.AddHoldingAsync("NVDA", 2m, 100m);
        var engine = fixture.CreateEngine();

        var error = await Assert.ThrowsAsync<PaperTradingException>(() => engine.ExecuteAsync(
            fixture.PortfolioId, new PaperTradeRequest(Guid.NewGuid(), "SELL", "NVDA", 2.01m, 110m)));

        Assert.Equal(PaperTradingFailure.InsufficientHoldings, error.Category);
        var holding = await fixture.Context.Holdings.AsNoTracking().SingleAsync();
        Assert.Equal(2m, holding.Quantity);
        Assert.Equal(100_000m, await fixture.Context.Portfolios.Select(row => row.CashBalance).SingleAsync());
        Assert.Equal(0, await fixture.Context.Transactions.CountAsync());
    }

    [Fact]
    public async Task Multiple_buys_update_the_weighted_average_cost()
    {
        await using var fixture = await TradingFixture.CreateAsync(initialCapital: 2_000m);
        var engine = fixture.CreateEngine();

        await engine.ExecuteAsync(fixture.PortfolioId,
            new PaperTradeRequest(Guid.NewGuid(), "BUY", "AAPL", 10m, 100m));
        var result = await engine.ExecuteAsync(fixture.PortfolioId,
            new PaperTradeRequest(Guid.NewGuid(), "BUY", "AAPL", 5m, 130m));

        Assert.Equal(15m, result.HoldingQuantity);
        Assert.Equal(110m, result.AverageCost!.Value);
        Assert.Equal(350m, result.CashBalance);

        var holding = await fixture.Context.Holdings.AsNoTracking().SingleAsync();
        Assert.Equal(15m, holding.Quantity);
        Assert.Equal(110m, holding.AverageCost);
    }

    [Fact]
    public async Task Repeating_the_same_order_is_idempotent_but_a_changed_retry_is_rejected()
    {
        await using var fixture = await TradingFixture.CreateAsync();
        var engine = fixture.CreateEngine();
        var orderId = Guid.NewGuid();
        var request = new PaperTradeRequest(orderId, "BUY", "AAPL", 2m, 100m);

        var first = await engine.ExecuteAsync(fixture.PortfolioId, request);
        var retry = await engine.ExecuteAsync(fixture.PortfolioId, request);

        Assert.Equal(first.TransactionId, retry.TransactionId);
        Assert.Equal(99_800m, retry.CashBalance);
        Assert.Equal(1, await fixture.Context.Transactions.CountAsync());

        var error = await Assert.ThrowsAsync<PaperTradingException>(() => engine.ExecuteAsync(
            fixture.PortfolioId, request with { Quantity = 3m }));
        Assert.Equal(PaperTradingFailure.DuplicateOrder, error.Category);
    }

    private sealed class TradingFixture(SqliteConnection connection, SqliteTradingDbContext context,
        Guid portfolioId) : IAsyncDisposable
    {
        private static readonly DateTime FixedUtcNow = new(2026, 9, 24, 18, 0, 0, DateTimeKind.Utc);

        public SqliteTradingDbContext Context { get; } = context;
        public Guid PortfolioId { get; } = portfolioId;

        public static async Task<TradingFixture> CreateAsync(decimal initialCapital = 100_000m)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<StockLabDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new SqliteTradingDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var userId = Guid.NewGuid();
            var portfolioId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                DisplayName = "Test User",
                Email = "test@example.com",
                NormalizedEmail = "TEST@EXAMPLE.COM",
                PasswordHash = "test-hash",
                CreatedAtUtc = FixedUtcNow,
                UpdatedAtUtc = FixedUtcNow,
                Version = new byte[8]
            };
            context.Users.Add(user);
            context.Portfolios.Add(new Portfolio
            {
                Id = portfolioId,
                UserId = userId,
                User = user,
                Currency = "USD",
                InitialCapital = initialCapital,
                CashBalance = initialCapital,
                CreatedAtUtc = FixedUtcNow,
                Version = new byte[8]
            });
            await context.SaveChangesAsync();
            return new TradingFixture(connection, context, portfolioId);
        }

        public PaperTradingEngine CreateEngine() => new(Context, new FixedTimeProvider(FixedUtcNow));

        public async Task AddHoldingAsync(string symbol, decimal quantity, decimal averageCost)
        {
            Context.Holdings.Add(new Holding
            {
                Id = Guid.NewGuid(),
                PortfolioId = PortfolioId,
                Symbol = symbol,
                Quantity = quantity,
                AverageCost = averageCost,
                UpdatedAtUtc = FixedUtcNow
            });
            await Context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class SqliteTradingDbContext(DbContextOptions<StockLabDbContext> options) : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().Property(user => user.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(portfolio => portfolio.Version).ValueGeneratedNever();
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
