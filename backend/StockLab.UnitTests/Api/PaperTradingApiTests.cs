using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Api;

public sealed class PaperTradingApiTests
{
    private const string TestIssuer = "StockLab.Api.Tests";
    private const string TestAudience = "StockLab.Tests";
    private const string TestSigningKey = "test-only-signing-key-at-least-32-bytes-long";

    [Fact]
    public async Task Authenticated_buy_returns_execution_and_updated_cash()
    {
        await using var fixture = await TradingApiFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "buy-api@example.com");
        var orderId = Guid.NewGuid();

        using var response = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId,
            side = "BUY",
            symbol = " aapl ",
            quantity = 2m,
            orderType = "market"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(orderId, body.RootElement.GetProperty("orderId").GetGuid());
        Assert.Equal("BUY", body.RootElement.GetProperty("side").GetString());
        Assert.Equal("AAPL", body.RootElement.GetProperty("symbol").GetString());
        Assert.Equal(2m, body.RootElement.GetProperty("quantity").GetDecimal());
        var executionPrice = body.RootElement.GetProperty("executionPrice").GetDecimal();
        Assert.True(executionPrice > 0m);
        Assert.Equal(decimal.Round(100_000m - 2m * executionPrice, 4),
            body.RootElement.GetProperty("cashBalance").GetDecimal());
        Assert.Equal(2m, body.RootElement.GetProperty("holdingQuantity").GetDecimal());
    }

    [Fact]
    public async Task Authenticated_sell_returns_execution_and_updated_holding()
    {
        await using var fixture = await TradingApiFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "sell-api@example.com");
        await SeedHoldingAsync(fixture, account.Id, "AAPL", 3m, 150m);

        using var response = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(),
            side = "SELL",
            symbol = "AAPL",
            quantity = 1m,
            orderType = "market"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SELL", body.RootElement.GetProperty("side").GetString());
        Assert.Equal(2m, body.RootElement.GetProperty("holdingQuantity").GetDecimal());
        Assert.True(body.RootElement.GetProperty("cashBalance").GetDecimal() > 100_000m);
    }

    [Fact]
    public async Task Insufficient_cash_is_returned_as_a_controlled_error()
    {
        await using var fixture = await TradingApiFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "cash-api@example.com");
        await SetCashAsync(fixture, account.Id, 100m);

        using var response = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(),
            side = "BUY",
            symbol = "AAPL",
            quantity = 10m,
            orderType = "market"
        });

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("insufficient_cash", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Trade_endpoint_requires_authentication()
    {
        await using var fixture = await TradingApiFixture.CreateAsync();

        using var response = await PostTradeAsync(fixture.Client, null, new
        {
            orderId = Guid.NewGuid(), side = "BUY", symbol = "AAPL", quantity = 1m, orderType = "market"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Limit_buy_is_rejected_until_the_quote_reaches_the_limit()
    {
        await using var fixture = await TradingApiFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "limit-api@example.com");

        using var response = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(), side = "BUY", symbol = "AAPL", quantity = 1m,
            orderType = "limit", limitPrice = 200m
        });

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("limit_not_reached", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Limit_buy_executes_at_the_current_quote_when_the_limit_is_reached()
    {
        await using var fixture = await TradingApiFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "limit-execution-api@example.com");

        using var response = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(), side = "BUY", symbol = "AAPL", quantity = 1m,
            orderType = "limit", limitPrice = 210m
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(204.5m, body.RootElement.GetProperty("executionPrice").GetDecimal());
    }

    [Fact]
    public async Task Limit_sell_is_rejected_when_the_quote_is_below_the_limit()
    {
        await using var fixture = await TradingApiFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "limit-sell-api@example.com");
        await SeedHoldingAsync(fixture, account.Id, "AAPL", 1m, 150m);

        using var response = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(), side = "SELL", symbol = "AAPL", quantity = 1m,
            orderType = "limit", limitPrice = 210m
        });

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("limit_not_reached", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Repeating_an_order_returns_the_committed_result_without_a_second_execution()
    {
        await using var fixture = await TradingApiFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "retry-api@example.com");
        var orderId = Guid.NewGuid();
        var payload = new
        {
            orderId, side = "BUY", symbol = "AAPL", quantity = 1m, orderType = "market"
        };

        using var firstResponse = await PostTradeAsync(fixture.Client, account.Token, payload);
        using var firstBody = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var retryResponse = await PostTradeAsync(fixture.Client, account.Token, payload);
        using var retryBody = JsonDocument.Parse(await retryResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal(firstBody.RootElement.GetProperty("transactionId").GetGuid(),
            retryBody.RootElement.GetProperty("transactionId").GetGuid());
        Assert.Equal(firstBody.RootElement.GetProperty("cashBalance").GetDecimal(),
            retryBody.RootElement.GetProperty("cashBalance").GetDecimal());
    }

    [Fact]
    public async Task Oversized_quantity_is_rejected_before_execution()
    {
        await using var fixture = await TradingApiFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "bounds-api@example.com");

        using var response = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(), side = "BUY", symbol = "AAPL",
            quantity = 100_000_000_000m, orderType = "market"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<(Guid Id, string Token)> CreateSignedInAccountAsync(
        TradingApiFixture fixture, string email)
    {
        using var registration = await fixture.Client.PostAsync("/api/auth/register", Json(new
        {
            displayName = "Trading API User", email, password = "correct horse battery staple"
        }));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var registrationBody = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        var id = registrationBody.RootElement.GetProperty("id").GetGuid();

        using var login = await fixture.Client.PostAsync("/api/auth/login", Json(new
        {
            email, password = "correct horse battery staple"
        }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return (id, loginBody.RootElement.GetProperty("accessToken").GetString()!);
    }

    private static async Task SeedHoldingAsync(TradingApiFixture fixture, Guid userId,
        string symbol, decimal quantity, decimal averageCost)
    {
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var portfolio = await context.Portfolios.SingleAsync(row => row.UserId == userId);
        context.Holdings.Add(new Holding
        {
            Id = Guid.NewGuid(), PortfolioId = portfolio.Id, Symbol = symbol,
            Quantity = quantity, AverageCost = averageCost, UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    private static async Task SetCashAsync(TradingApiFixture fixture, Guid userId, decimal cash)
    {
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var portfolio = await context.Portfolios.SingleAsync(row => row.UserId == userId);
        portfolio.CashBalance = cash;
        await context.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> PostTradeAsync(HttpClient client, string? token, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/portfolio/trades")
        {
            Content = Json(payload)
        };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetString()!;
    }

    private sealed class TradingApiFixture(
        SqliteConnection connection,
        WebApplicationFactory<Program> application,
        HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public IServiceScope CreateScope() => application.Services.CreateScope();

        public static async Task<TradingApiFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<StockLabDbContext>()
                .UseSqlite(connection).Options;
            var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:StockLab"] = "Server=localhost;Database=TradingTestsNeverUsed;Integrated Security=true;TrustServerCertificate=true",
                        ["Jwt:Issuer"] = TestIssuer,
                        ["Jwt:Audience"] = TestAudience,
                        ["Jwt:SigningKey"] = TestSigningKey,
                        ["MarketData:Provider"] = "Mock"
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                    services.AddLogging(logging => logging.ClearProviders());
                    services.RemoveAll<DbContextOptions<StockLabDbContext>>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<StockLabDbContext>();
                    services.RemoveAll<IDbContextFactory<StockLabDbContext>>();
                    services.AddSingleton(options);
                    services.AddScoped<StockLabDbContext, SqliteTradingDbContext>();
                    services.AddSingleton<IDbContextFactory<StockLabDbContext>>(
                        new SqliteContextFactory(options));
                });
            });
            try
            {
                var client = application.CreateClient();
                using var scope = application.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Database.EnsureCreatedAsync();
                return new TradingApiFixture(connection, application, client);
            }
            catch
            {
                await application.DisposeAsync();
                await connection.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class SqliteTradingDbContext(DbContextOptions<StockLabDbContext> options)
        : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().Property(user => user.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(portfolio => portfolio.Version).ValueGeneratedNever();
        }
    }

    private sealed class SqliteContextFactory(DbContextOptions<StockLabDbContext> options)
        : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => new SqliteTradingDbContext(options);
    }
}
