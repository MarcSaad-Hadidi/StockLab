using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Portfolios;

namespace StockLab.UnitTests.PortfolioApi;

public sealed class PortfolioServiceTests
{
    [Fact]
    public async Task Get_returns_only_the_authenticated_users_portfolio_and_calculates_values()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var service = new PortfolioService(fixture.Context);

        var summary = await service.GetAsync(fixture.UserId, CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal(fixture.PortfolioId, summary!.PortfolioId);
        Assert.Equal(fixture.UserId, summary.UserId);
        Assert.Equal("USD", summary.Currency);
        Assert.Equal(100_000m, summary.InitialCapital);
        Assert.Equal(96_250m, summary.CashAvailable);
        Assert.Equal(2_350m, summary.InvestedValue);
        Assert.Equal(98_600m, summary.TotalValue);
        Assert.Collection(summary.Positions,
            position =>
            {
                Assert.Equal("AAPL", position.Symbol);
                Assert.Equal(10m, position.Quantity);
                Assert.Equal(125m, position.AveragePrice);
                Assert.Equal(1_250m, position.Value);
            },
            position =>
            {
                Assert.Equal("MSFT", position.Symbol);
                Assert.Equal(5m, position.Quantity);
                Assert.Equal(220m, position.AveragePrice);
                Assert.Equal(1_100m, position.Value);
            });
    }

    [Fact]
    public async Task Get_returns_null_for_an_unknown_user_without_leaking_another_portfolio()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var service = new PortfolioService(fixture.Context);

        var summary = await service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(summary);
    }

    [Fact]
    public async Task Get_returns_cash_as_total_value_when_the_portfolio_has_no_positions()
    {
        await using var fixture = await PortfolioFixture.CreateAsync(includeHoldings: false);
        var service = new PortfolioService(fixture.Context);

        var summary = await service.GetAsync(fixture.UserId, CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal(96_250m, summary!.CashAvailable);
        Assert.Equal(0m, summary.InvestedValue);
        Assert.Equal(96_250m, summary.TotalValue);
        Assert.Empty(summary.Positions);
    }

    private sealed class PortfolioFixture(SqliteConnection connection, StockLabDbContext context,
        Guid userId, Guid portfolioId) : IAsyncDisposable
    {
        public StockLabDbContext Context { get; } = context;
        public Guid UserId { get; } = userId;
        public Guid PortfolioId { get; } = portfolioId;

        public static async Task<PortfolioFixture> CreateAsync(bool includeHoldings = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<StockLabDbContext>()
                .UseSqlite(connection).Options;
            var context = new PortfolioDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var userId = Guid.NewGuid();
            var portfolioId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                DisplayName = "Portfolio owner",
                Email = "owner@example.com",
                NormalizedEmail = "OWNER@EXAMPLE.COM",
                PasswordHash = "test-hash",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Version = new byte[8]
            };
            var portfolio = new Portfolio
            {
                Id = portfolioId,
                UserId = userId,
                User = user,
                Currency = "USD",
                InitialCapital = 100_000m,
                CashBalance = 96_250m,
                CreatedAtUtc = DateTime.UtcNow,
                Version = new byte[8]
            };
            context.Users.Add(user);
            context.Portfolios.Add(portfolio);
            if (includeHoldings)
            {
                context.Holdings.AddRange(
                    new Holding
                    {
                        Id = Guid.NewGuid(), PortfolioId = portfolioId, Portfolio = portfolio,
                        Symbol = "AAPL", Quantity = 10m, AverageCost = 125m, UpdatedAtUtc = DateTime.UtcNow
                    },
                    new Holding
                    {
                        Id = Guid.NewGuid(), PortfolioId = portfolioId, Portfolio = portfolio,
                        Symbol = "MSFT", Quantity = 5m, AverageCost = 220m, UpdatedAtUtc = DateTime.UtcNow
                    });
            }

            await context.SaveChangesAsync();
            return new PortfolioFixture(connection, context, userId, portfolioId);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class PortfolioDbContext(DbContextOptions<StockLabDbContext> options)
        : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().Property(user => user.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(portfolio => portfolio.Version).ValueGeneratedNever();
        }
    }
}
