using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using StockLab.Api.DTOs.Portfolio;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.MarketData;
using StockLab.Infrastructure.Persistence;
using StockLab.UnitTests.MarketData;

namespace StockLab.UnitTests.Api;

public sealed class PaperTradingApiTests
{
    private const string TestIssuer = "StockLab.Api.Tests";
    private const string TestAudience = "StockLab.Tests";
    private const string TestSigningKey = "test-only-signing-key-at-least-32-bytes-long";

    private static string ListingQuote(string exchange) =>
        TwelveDataProviderTests.QuoteJson.Replace("\"currency\":\"USD\"", $"\"currency\":\"USD\",\"exchange\":\"{exchange}\"");

    [Theory]
    [InlineData("AAPL", "AAPL:NASDAQ", true)]
    [InlineData("AAPL:NASDAQ", "AAPL", true)]
    [InlineData("AAPL", "AAPL:NASDAQ", false)]
    [InlineData("AAPL:NASDAQ", "AAPL", false)]
    public async Task Security_aliases_share_one_position_for_buys_and_sells(
        string firstSymbol, string alias, bool useTwelveData)
    {
        using var provider = new TwelveDataProviderTests.Fixture(ListingQuote("NASDAQ"));
        await using var fixture = await TradingApiFixture.CreateAsync(useTwelveData ? provider.Provider : null);
        var account = await CreateSignedInAccountAsync(fixture, "aliases@example.com");

        using var buy = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(), side = "BUY", symbol = firstSymbol, quantity = 2m, orderType = "market"
        });
        Assert.Equal(HttpStatusCode.OK, buy.StatusCode);
        using var sell = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(), side = "SELL", symbol = alias, quantity = 1m, orderType = "market"
        });
        Assert.Equal(HttpStatusCode.OK, sell.StatusCode);
        using var secondBuy = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(), side = "BUY", symbol = alias, quantity = 1m, orderType = "market"
        });
        Assert.Equal(HttpStatusCode.OK, secondBuy.StatusCode);

        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var holding = Assert.Single(await context.Holdings.ToListAsync());
        Assert.Equal("AAPL:NASDAQ", holding.Symbol);
        Assert.Equal(2m, holding.Quantity);
        Assert.Equal(204.5m, holding.AverageCost);
        Assert.Equal(99_591m, await context.Portfolios.Select(row => row.CashBalance).SingleAsync());
        var transactions = await context.Transactions.ToListAsync();
        Assert.Equal(3, transactions.Count);
        Assert.All(transactions, transaction => Assert.Equal("AAPL:NASDAQ", transaction.Symbol));
    }

    [Theory]
    [InlineData("")]
    [InlineData("EXCHANGE-NAME-LONGER-THAN-27-CHARACTERS")]
    public async Task Unidentifiable_or_oversized_listings_are_rejected_without_changes(string exchange)
    {
        using var provider = new TwelveDataProviderTests.Fixture(ListingQuote(exchange));
        await using var fixture = await TradingApiFixture.CreateAsync(provider.Provider);
        var account = await CreateSignedInAccountAsync(fixture, "invalid-listing@example.com");

        using var response = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(), side = "BUY", symbol = "AAPL", quantity = 1m, orderType = "market"
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("invalid_order", await ErrorCodeAsync(response));
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        Assert.Equal(100_000m, await context.Portfolios.Select(row => row.CashBalance).SingleAsync());
        Assert.Empty(await context.Holdings.ToListAsync());
        Assert.Empty(await context.Transactions.ToListAsync());
    }

    [Theory]
    [InlineData("AAPL", "AAPL:NASDAQ")]
    [InlineData("AAPL:NASDAQ", "AAPL")]
    public async Task Canonical_trade_retries_replay_aliases_and_original_requests_during_provider_failure(
        string originalSymbol, string alias)
    {
        using var provider = new TwelveDataProviderTests.Fixture(ListingQuote("NASDAQ"));
        await using var fixture = await TradingApiFixture.CreateAsync(provider.Provider);
        var account = await CreateSignedInAccountAsync(fixture, "alias-retry@example.com");
        var orderId = Guid.NewGuid();
        object Order(string symbol) => new { orderId, side = "BUY", symbol, quantity = 2m, orderType = "market" };
        using var buy = await PostTradeAsync(fixture.Client, account.Token, Order(originalSymbol));
        Assert.Equal(HttpStatusCode.OK, buy.StatusCode);
        var committedResult = await buy.Content.ReadFromJsonAsync<PaperTradeResponse>();
        Assert.NotNull(committedResult);
        using var aliasRetry = await PostTradeAsync(fixture.Client, account.Token, Order(alias));
        Assert.Equal(HttpStatusCode.OK, aliasRetry.StatusCode);
        Assert.Equal(committedResult, await aliasRetry.Content.ReadFromJsonAsync<PaperTradeResponse>());

        var callsBeforeFailure = provider.Handler.Requests.Count;
        provider.Handler.Respond = (_, _) => Task.FromResult(TwelveDataProviderTests.Response("{}", HttpStatusCode.ServiceUnavailable));
        using var originalRetry = await PostTradeAsync(fixture.Client, account.Token, Order(originalSymbol));
        Assert.Equal(HttpStatusCode.OK, originalRetry.StatusCode);
        Assert.Equal(committedResult, await originalRetry.Content.ReadFromJsonAsync<PaperTradeResponse>());
        Assert.Equal(callsBeforeFailure, provider.Handler.Requests.Count);
        using var scope = fixture.CreateScope();
        var transaction = Assert.Single(await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Transactions.ToListAsync());
        Assert.Equal(originalSymbol, transaction.RequestedSymbol);
    }

    [Fact]
    public async Task Same_ticker_on_different_exchanges_keeps_separate_positions_and_rejects_changed_retries()
    {
        using var provider = new TwelveDataProviderTests.Fixture(ListingQuote("NASDAQ"));
        provider.Handler.Respond = (request, _) => Task.FromResult(TwelveDataProviderTests.Response(
            ListingQuote(Uri.UnescapeDataString(request.RequestUri!.Query).Contains(":NYSE") ? "NYSE" : "NASDAQ")));
        await using var fixture = await TradingApiFixture.CreateAsync(provider.Provider);
        var account = await CreateSignedInAccountAsync(fixture, "listings@example.com");
        var orderId = Guid.NewGuid();
        using var buy = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId, side = "BUY", symbol = "AAPL:NYSE", quantity = 1m, orderType = "market"
        });
        Assert.Equal(HttpStatusCode.OK, buy.StatusCode);
        using var changedRetry = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId, side = "BUY", symbol = "AAPL", quantity = 1m, orderType = "market"
        });
        Assert.Equal(HttpStatusCode.Conflict, changedRetry.StatusCode);
        Assert.Equal("duplicate_order", await ErrorCodeAsync(changedRetry));
        using var otherBuy = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(), side = "BUY", symbol = "AAPL", quantity = 2m, orderType = "market"
        });
        Assert.Equal(HttpStatusCode.OK, otherBuy.StatusCode);
        using var sell = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(), side = "SELL", symbol = "AAPL:NYSE", quantity = 2m, orderType = "market"
        });
        Assert.Equal((HttpStatusCode)422, sell.StatusCode);
        Assert.Equal("insufficient_holdings", await ErrorCodeAsync(sell));
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var holdings = await context.Holdings.OrderBy(row => row.Symbol).ToListAsync();
        Assert.Equal(new[] { "AAPL:NASDAQ", "AAPL:NYSE" }, holdings.Select(row => row.Symbol));
        Assert.Equal(new[] { 2m, 1m }, holdings.Select(row => row.Quantity));
        Assert.Equal(2, await context.Transactions.CountAsync());
    }

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
        Assert.Equal("AAPL:NASDAQ", body.RootElement.GetProperty("symbol").GetString());
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
        await SeedHoldingAsync(fixture, account.Id, "AAPL:NASDAQ", 3m, 150m);

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
        await SeedHoldingAsync(fixture, account.Id, "AAPL:NASDAQ", 1m, 150m);

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

        using var changedTermsResponse = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId, side = "BUY", symbol = "AAPL", quantity = 1m,
            orderType = "limit", limitPrice = 210m
        });
        Assert.Equal(HttpStatusCode.Conflict, changedTermsResponse.StatusCode);
        Assert.Equal("duplicate_order", await ErrorCodeAsync(changedTermsResponse));
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

    [Fact]
    public async Task Quote_currency_must_match_the_portfolio_currency()
    {
        await using var fixture = await TradingApiFixture.CreateAsync(new CurrencyMarketDataProvider("CAD"));
        var account = await CreateSignedInAccountAsync(fixture, "currency-api@example.com");

        using var response = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(), side = "BUY", symbol = "AAPL", quantity = 1m, orderType = "market"
        });

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("currency_mismatch", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("BUY")]
    [InlineData("SELL")]
    public async Task Post_trade_capacity_overflow_returns_a_controlled_error_without_changes(string side)
    {
        await using var fixture = await TradingApiFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "capacity-api@example.com");
        var cash = side == "SELL" ? 999_999_999_999_998.9999m : 100_000m;
        var heldQuantity = side == "BUY" ? 99_999_999_998.99999999m : 1m;
        await SetCashAsync(fixture, account.Id, cash);
        await SeedHoldingAsync(fixture, account.Id, "AAPL:NASDAQ", heldQuantity, 150m);

        using var response = await PostTradeAsync(fixture.Client, account.Token, new
        {
            orderId = Guid.NewGuid(), side, symbol = "AAPL", quantity = side == "BUY" ? 2m : 1m,
            orderType = "market"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_order", await ErrorCodeAsync(response));
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        Assert.Equal(cash, await context.Portfolios.Select(row => row.CashBalance).SingleAsync());
        var holding = await context.Holdings.SingleAsync();
        Assert.Equal(heldQuantity, holding.Quantity);
        Assert.Equal(150m, holding.AverageCost);
        Assert.Empty(await context.Transactions.ToListAsync());
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

        public static async Task<TradingApiFixture> CreateAsync(IMarketDataProvider? marketDataProvider = null)
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
                    services.RemoveAll<IMarketDataProvider>();
                    services.AddSingleton(options);
                    services.AddScoped<StockLabDbContext, SqliteTradingDbContext>();
                    services.AddSingleton<IDbContextFactory<StockLabDbContext>>(
                        new SqliteContextFactory(options));
                    services.AddSingleton<IMarketDataProvider>(marketDataProvider ?? new MockMarketDataProvider());
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
            // Keep the SQL Server decimal boundary values exact in this SQLite fixture.
            modelBuilder.Entity<Portfolio>().Property(portfolio => portfolio.CashBalance).HasColumnType("TEXT");
            modelBuilder.Entity<Holding>().Property(holding => holding.Quantity).HasColumnType("TEXT");
        }
    }

    private sealed class SqliteContextFactory(DbContextOptions<StockLabDbContext> options)
        : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => new SqliteTradingDbContext(options);
    }

    private sealed class CurrencyMarketDataProvider(string currency) : IMarketDataProvider
    {
        private readonly MockMarketDataProvider inner = new();

        public async Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var quote = await inner.GetQuoteAsync(symbol, cancellationToken);
            return quote is null ? null : quote with { Currency = currency };
        }

        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(
            string query, CancellationToken cancellationToken = default) =>
            inner.SearchStocksAsync(query, cancellationToken);

        public Task<StockHistory?> GetHistoryAsync(
            StockHistoryRequest request, CancellationToken cancellationToken = default) =>
            inner.GetHistoryAsync(request, cancellationToken);
    }
}
