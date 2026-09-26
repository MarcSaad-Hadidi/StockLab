using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Domain.Entities;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Trading;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Persistence;

public sealed class PersistenceIntegrationTests
{
    [Fact]
    public void Api_registers_sql_server_context_using_named_connection_string()
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:StockLab"] = "Server=localhost;Database=StockLabTestOnly;Integrated Security=true;TrustServerCertificate=true",
                    ["Jwt:Issuer"] = "StockLab.Api.Tests",
                    ["Jwt:Audience"] = "StockLab.Tests",
                    ["Jwt:SigningKey"] = "test-only-signing-key-at-least-32-bytes-long"
                })));
        using var scope = app.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
        Assert.Equal("StockLabTestOnly", context.Database.GetDbConnection().Database);
        Assert.IsType<AiTraderPortfolioService>(scope.ServiceProvider.GetRequiredService<IAiTraderPortfolioService>());
        Assert.IsType<AiRiskManager>(scope.ServiceProvider.GetRequiredService<IAiRiskManager>());
    }

    [Fact]
    public async Task Ef_can_add_read_and_update_a_holding_in_a_relational_test_database()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite(connection).Options;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var holdingId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var setup = new StockLabDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            // SQLite does not generate SQL Server rowversion values. Seed the two
            // parent rows with explicit tokens; all holding I/O below uses EF Core.
            await setup.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO Users (Id, DisplayName, Email, NormalizedEmail, PasswordHash, CreatedAtUtc, UpdatedAtUtc, Version) VALUES ({userId}, {"Test User"}, {"test@example.com"}, {"TEST@EXAMPLE.COM"}, {"test-hash"}, {now}, {now}, {new byte[8]})");
            await setup.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO Portfolios (Id, UserId, Currency, InitialCapital, CashBalance, CreatedAtUtc, Version) VALUES ({portfolioId}, {userId}, {"USD"}, {100000m}, {100000m}, {now}, {new byte[8]})");
        }

        await using (var writer = new StockLabDbContext(options))
        {
            writer.Holdings.Add(new Holding
            {
                Id = holdingId,
                PortfolioId = portfolioId,
                Symbol = "AAPL",
                Quantity = 1.25m,
                AverageCost = 180.50m,
                UpdatedAtUtc = now
            });
            await writer.SaveChangesAsync();
        }

        await using (var reader = new StockLabDbContext(options))
        {
            var holding = await reader.Holdings.SingleAsync(row => row.Id == holdingId);
            Assert.Equal(1.25m, holding.Quantity);
            Assert.Equal("AAPL", holding.Symbol);
            holding.Quantity = 2.5m;
            await reader.SaveChangesAsync();
        }

        await using (var verifier = new StockLabDbContext(options))
        {
            var holding = await verifier.Holdings.AsNoTracking().SingleAsync(row => row.Id == holdingId);
            Assert.Equal(2.5m, holding.Quantity);
        }
    }

    [Fact]
    public async Task Relational_check_constraint_rejects_a_zero_share_holding()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite(connection).Options;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var context = new StockLabDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO Users (Id, DisplayName, Email, NormalizedEmail, PasswordHash, CreatedAtUtc, UpdatedAtUtc, Version) VALUES ({userId}, {"Test User"}, {"test@example.com"}, {"TEST@EXAMPLE.COM"}, {"test-hash"}, {now}, {now}, {new byte[8]})");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO Portfolios (Id, UserId, Currency, InitialCapital, CashBalance, CreatedAtUtc, Version) VALUES ({portfolioId}, {userId}, {"USD"}, {100000m}, {100000m}, {now}, {new byte[8]})");

        context.Holdings.Add(new Holding
        {
            Id = Guid.NewGuid(),
            PortfolioId = portfolioId,
            Symbol = "AAPL",
            Quantity = 0m,
            AverageCost = 180.50m,
            UpdatedAtUtc = now
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(275, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
    }
}
