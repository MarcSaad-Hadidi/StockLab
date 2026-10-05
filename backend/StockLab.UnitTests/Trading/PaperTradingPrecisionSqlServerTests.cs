using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StockLab.Application.DTOs.Trading;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.MarketData;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class PaperTradingPrecisionSqlServerTests
{
    private const decimal InitialCapital = 999_999_999_999_999.9999m;

    [LocalDbTheory]
    [InlineData("1", "1.2345", "0.5", "1.2345")]
    [InlineData("0.00000002", "0.0001", "0.00000001", "0.000000000002")]
    [InlineData("0.99999999", "999999999999999.9999", "0.33333333", "999999989999999.999900000001")]
    public async Task Sql_server_preserves_fractional_amounts_and_cash_through_sales_and_replays(
        string quantityText, string priceText, string sellSizeText, string amountText)
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var quantity = decimal.Parse(quantityText, CultureInfo.InvariantCulture);
        var price = decimal.Parse(priceText, CultureInfo.InvariantCulture);
        var sellSize = decimal.Parse(sellSizeText, CultureInfo.InvariantCulture);
        var expectedAmount = decimal.Parse(amountText, CultureInfo.InvariantCulture);
        var engine = new PaperTradingEngine(fixture, TimeProvider.System, new MockMarketDataProvider());
        var buyRequest = new PaperTradeRequest(Guid.NewGuid(), "BUY", "AAPL:NASDAQ", quantity, price);
        var bought = await engine.ExecuteAsync(fixture.UserId, fixture.PortfolioId, buyRequest);
        Assert.Equal(expectedAmount, bought.TotalAmount);
        Assert.Equal(InitialCapital - expectedAmount, bought.CashBalance);

        var buyReplay = await engine.ExecuteAsync(fixture.UserId, fixture.PortfolioId,
            buyRequest with { ExecutionPrice = 1m });
        Assert.Equal(bought, buyReplay);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(expectedAmount, (await db.Transactions.SingleAsync()).TotalAmount);
            Assert.Equal(InitialCapital - expectedAmount, (await db.Portfolios.SingleAsync()).CashBalance);
        }

        for (var remaining = quantity; remaining > 0m; remaining -= sellSize)
        {
            var request = new PaperTradeRequest(Guid.NewGuid(), "SELL", "AAPL:NASDAQ", sellSize, price);
            var sold = await engine.ExecuteAsync(fixture.UserId, fixture.PortfolioId, request);
            Assert.Equal(InitialCapital, sold.CashBalance + sold.HoldingQuantity * price);
            Assert.Equal(sold, await engine.ExecuteAsync(fixture.UserId, fixture.PortfolioId, request));
            await using var db = fixture.CreateDbContext();
            var portfolio = await db.Portfolios.Include(row => row.Holdings).SingleAsync();
            Assert.Equal(InitialCapital, portfolio.CashBalance + portfolio.Holdings.Sum(row => row.Quantity * price));
        }

        await using var verifier = fixture.CreateDbContext();
        Assert.Empty(await verifier.Holdings.ToListAsync());
        Assert.Equal(InitialCapital, (await verifier.Portfolios.SingleAsync()).CashBalance);
        var amounts = await verifier.Transactions.GroupBy(row => row.Side)
            .Select(group => new { Side = group.Key, Total = group.Sum(row => row.TotalAmount) }).ToListAsync();
        Assert.Equal(expectedAmount, Assert.Single(amounts, row => row.Side == "BUY").Total);
        Assert.Equal(expectedAmount, Assert.Single(amounts, row => row.Side == "SELL").Total);
    }

    [LocalDbFact]
    public async Task Precision_migration_preserves_existing_cash_and_transaction_amounts()
    {
        await using var fixture = await SqlFixture.CreateAsync(previousSchema: true);
        var orderId = Guid.NewGuid();
        await using (var db = fixture.CreateDbContext())
        {
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(), PortfolioId = fixture.PortfolioId, OrderId = orderId,
                Side = "BUY", Symbol = "AAPL:NASDAQ", Quantity = 1m, ExecutionPrice = 1.2345m,
                TotalAmount = 1.2345m, ExecutedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            await db.Database.MigrateAsync();
        }

        await using var verifier = fixture.CreateDbContext();
        Assert.Equal(InitialCapital, (await verifier.Portfolios.SingleAsync()).CashBalance);
        var transaction = await verifier.Transactions.SingleAsync();
        Assert.Equal(orderId, transaction.OrderId);
        Assert.Equal(1.2345m, transaction.TotalAmount);
        Assert.False(verifier.Database.HasPendingModelChanges());
    }

    private sealed class SqlFixture(string connection) : IDbContextFactory<StockLabDbContext>, IAsyncDisposable
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PortfolioId { get; } = Guid.NewGuid();
        public StockLabDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<StockLabDbContext>().UseSqlServer(connection).Options);

        public static async Task<SqlFixture> CreateAsync(bool previousSchema = false)
        {
            // Each test owns a disposable LocalDB database and never uses application credentials.
            var fixture = new SqlFixture($"Server=(localdb)\\MSSQLLocalDB;Database=StockLabPaperPrecisionTests_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true");
            try
            {
                await using var db = fixture.CreateDbContext();
                if (previousSchema)
                    await db.GetService<IMigrator>().MigrateAsync("20261004011946_IndexAiTradeHistory");
                else
                    await db.Database.MigrateAsync();
                db.Users.Add(new User
                {
                    Id = fixture.UserId, DisplayName = "Precision Test", Email = "precision@example.com",
                    NormalizedEmail = "PRECISION@EXAMPLE.COM", PasswordHash = "test-only-hash",
                    CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
                });
                db.Portfolios.Add(new Portfolio
                {
                    Id = fixture.PortfolioId, UserId = fixture.UserId, Currency = "USD",
                    InitialCapital = InitialCapital, CashBalance = InitialCapital, CreatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateDbContext();
            await db.Database.EnsureDeletedAsync();
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

    private sealed class LocalDbTheoryAttribute : TheoryAttribute
    {
        public LocalDbTheoryAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
}
