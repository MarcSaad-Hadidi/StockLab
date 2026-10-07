using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.MarketData;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Api;

public sealed class PortfolioApiTests
{
    private const string TestIssuer = "StockLab.Api.Tests";
    private const string TestAudience = "StockLab.Tests";
    private const string TestSigningKey = "test-only-signing-key-at-least-32-bytes-long";

    [Theory]
    [InlineData("99999.999999999999")]
    [InlineData("999999999999999.999899999999")]
    public async Task Portfolio_and_performance_preserve_exact_cash_and_derived_amounts(string cash)
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "exact-cash@example.com");
        await SeedPortfolioAsync(fixture, account.Id, decimal.Parse(cash, CultureInfo.InvariantCulture), "USD");
        foreach (var path in new[] { "/api/portfolio", "/api/portfolio/performance" })
        {
            using var response = await GetWithTokenAsync(fixture.Client, account.Token, path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(cash, body.RootElement.GetProperty("cashBalance").GetString());
            Assert.Equal(cash, body.RootElement.GetProperty("totalValue").GetString());
            if (path.EndsWith("performance"))
                Assert.Equal(decimal.Parse(cash, CultureInfo.InvariantCulture) - 100_000m,
                    decimal.Parse(body.RootElement.GetProperty("totalPnl").GetString()!, CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData("999999989999999.999900000001")]
    [InlineData("10000.000000000001")]
    [InlineData("0.000000000001")]
    [InlineData("204.5000")]
    public async Task History_and_recent_activity_serialize_ledger_amounts_as_exact_decimal_strings(string amount)
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "exact-json@example.com");
        var portfolioId = await SeedPortfolioAsync(fixture, account.Id, 100_000m, "USD");
        await SeedTransactionAsync(fixture, portfolioId, "AAPL", DateTime.UtcNow,
            decimal.Parse(amount, CultureInfo.InvariantCulture));

        foreach (var path in new[] { "/api/portfolio/transactions", "/api/portfolio/transactions/history" })
        {
            using var response = await GetWithTokenAsync(fixture.Client, account.Token, path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = body.RootElement;
            var row = Assert.Single((root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("items")).EnumerateArray());
            Assert.Equal(decimal.Parse(amount, CultureInfo.InvariantCulture),
                decimal.Parse(row.GetProperty("totalAmount").GetString()!, CultureInfo.InvariantCulture));
            if (root.ValueKind == JsonValueKind.Object)
            {
                Assert.Equal(decimal.Parse(amount, CultureInfo.InvariantCulture),
                    decimal.Parse(root.GetProperty("summary").GetProperty("totalInvested").GetString()!, CultureInfo.InvariantCulture));
                Assert.Equal(0m, decimal.Parse(root.GetProperty("summary").GetProperty("totalProceeds").GetString()!, CultureInfo.InvariantCulture));
            }
        }
    }

    [Fact]
    public async Task Get_portfolio_requires_authentication()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();

        using var response = await fixture.Client.GetAsync("/api/portfolio");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("invalid_signature")]
    [InlineData("expired")]
    [InlineData("missing_sub")]
    [InlineData("invalid_sub")]
    public async Task Invalid_tokens_cannot_read_a_portfolio(string scenario)
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var token = scenario switch
        {
            "malformed" => "not-a-jwt",
            "invalid_signature" => CreateSignedToken(Guid.NewGuid().ToString(),
                signingKey: "another-test-only-signing-key-at-least-32-bytes"),
            "expired" => CreateSignedToken(Guid.NewGuid().ToString(), expired: true),
            "missing_sub" => CreateSignedToken(null),
            _ => CreateSignedToken("not-a-guid")
        };

        using var response = await GetWithTokenAsync(fixture.Client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Registered_user_receives_the_initial_portfolio_after_login()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "new@example.com");

        using var response = await GetWithTokenAsync(fixture.Client, account.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertBalances(body.RootElement, 100_000m, 100_000m, 0m, 100_000m, "USD");
        Assert.Empty(body.RootElement.GetProperty("positions").EnumerateArray());
        AssertPublicFields(body.RootElement);
    }

    [Fact]
    public async Task Multiple_holdings_return_quantities_costs_and_cost_based_totals()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "positions@example.com");
        await SeedPortfolioAsync(fixture, account.Id, 97_000m, "USD",
            ("MSFT", 5m, 300m), ("AAPL", 10m, 150m));

        using var response = await GetWithTokenAsync(fixture.Client, account.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertBalances(body.RootElement, 97_000m, 100_000m, 3_000m, 100_000m, "USD");
        var positions = body.RootElement.GetProperty("positions").EnumerateArray().ToArray();
        Assert.Equal(2, positions.Length);
        AssertPosition(positions.Single(p => p.GetProperty("symbol").GetString() == "AAPL"), "AAPL", 10m, 150m);
        AssertPosition(positions.Single(p => p.GetProperty("symbol").GetString() == "MSFT"), "MSFT", 5m, 300m);
        AssertPublicFields(body.RootElement);
    }

    [Fact]
    public async Task Recent_transactions_are_limited_sorted_and_scoped_to_the_authenticated_portfolio()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var accountA = await CreateSignedInAccountAsync(fixture, "transactions-a@example.com");
        var accountB = await CreateSignedInAccountAsync(fixture, "transactions-b@example.com");
        var portfolioA = await SeedPortfolioAsync(fixture, accountA.Id, 100_000m, "USD");
        var portfolioB = await SeedPortfolioAsync(fixture, accountB.Id, 100_000m, "USD");
        await SeedTransactionAsync(fixture, portfolioA, "AAPL", DateTime.UtcNow.AddMinutes(-2));
        await SeedTransactionAsync(fixture, portfolioA, "MSFT", DateTime.UtcNow.AddMinutes(-1));
        await SeedTransactionAsync(fixture, portfolioB, "NVDA", DateTime.UtcNow);

        using var response = await GetWithTokenAsync(fixture.Client, accountA.Token,
            "/api/portfolio/transactions?limit=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var transaction = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal("MSFT", transaction.GetProperty("symbol").GetString());
        Assert.Equal("BUY", transaction.GetProperty("side").GetString());
        Assert.EndsWith("Z", transaction.GetProperty("executedAtUtc").GetString());
    }

    [Fact]
    public async Task Fractional_positions_preserve_decimal_precision_without_rounding_totals()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "fractional@example.com");
        await SeedPortfolioAsync(fixture, account.Id, 9_876.5432m, "CAD",
            ("AAPL", 1.25m, 180.50m), ("MSFT", 2.5m, 12.3456m), ("NVDA", 0.12345678m, 100m));

        using var response = await GetWithTokenAsync(fixture.Client, account.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertBalances(body.RootElement, 9_876.5432m, 100_000m, 268.834678m, 10_145.377878m, "CAD");
        var positions = body.RootElement.GetProperty("positions").EnumerateArray().ToArray();
        Assert.Equal(3, positions.Length);
        AssertPosition(positions.Single(p => p.GetProperty("symbol").GetString() == "NVDA"), "NVDA", 0.12345678m, 100m);
        AssertPosition(positions.Single(p => p.GetProperty("symbol").GetString() == "MSFT"), "MSFT", 2.5m, 12.3456m);
    }

    [Fact]
    public async Task Performance_uses_current_quotes_for_position_and_total_metrics()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "performance@example.com");
        await SeedPortfolioAsync(fixture, account.Id, 97_000m, "USD",
            ("AAPL", 10m, 180m), ("MSFT", 5m, 300m));

        using var response = await GetWithTokenAsync(fixture.Client, account.Token,
            "/api/portfolio/performance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(97_000m, decimal.Parse(root.GetProperty("cashBalance").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(100_000m, decimal.Parse(root.GetProperty("initialCapital").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(3_300m, decimal.Parse(root.GetProperty("investedValue").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(4_067.5m, decimal.Parse(root.GetProperty("positionsMarketValue").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(101_067.5m, decimal.Parse(root.GetProperty("totalValue").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1_067.5m, decimal.Parse(root.GetProperty("totalPnl").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1.0675m, root.GetProperty("returnPercent").GetDecimal());
        Assert.Equal("USD", root.GetProperty("currency").GetString());

        var positions = root.GetProperty("positions").EnumerateArray().ToArray();
        var apple = Assert.Single(positions, position => position.GetProperty("symbol").GetString() == "AAPL");
        Assert.Equal(204.5m, apple.GetProperty("currentPrice").GetDecimal());
        Assert.Equal(2_045m, decimal.Parse(apple.GetProperty("marketValue").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(245m, decimal.Parse(apple.GetProperty("pnl").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(13.611111111111111111111111110m, apple.GetProperty("pnlPercent").GetDecimal());

        var microsoft = Assert.Single(positions, position => position.GetProperty("symbol").GetString() == "MSFT");
        Assert.Equal(404.5m, microsoft.GetProperty("currentPrice").GetDecimal());
        Assert.Equal(2_022.5m, decimal.Parse(microsoft.GetProperty("marketValue").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(522.5m, decimal.Parse(microsoft.GetProperty("pnl").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(34.833333333333333333333333330m, microsoft.GetProperty("pnlPercent").GetDecimal());
    }

    [Fact]
    public async Task Performance_rejects_unavailable_or_mismatched_quotes_without_cost_fallback()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "unavailable-performance@example.com");
        await SeedPortfolioAsync(fixture, account.Id, 98_000m, "CAD", ("AAPL", 10m, 200m));

        using var response = await GetWithTokenAsync(fixture.Client, account.Token,
            "/api/portfolio/performance");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("portfolio_performance_unavailable", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Performance_requires_authentication_and_returns_empty_metrics_for_empty_portfolio()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        using var anonymous = await fixture.Client.GetAsync("/api/portfolio/performance");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var account = await CreateSignedInAccountAsync(fixture, "empty-performance@example.com");
        using var response = await GetWithTokenAsync(fixture.Client, account.Token,
            "/api/portfolio/performance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(100_000m, decimal.Parse(root.GetProperty("totalValue").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0m, decimal.Parse(root.GetProperty("positionsMarketValue").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0m, decimal.Parse(root.GetProperty("totalPnl").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0m, root.GetProperty("returnPercent").GetDecimal());
        Assert.Empty(root.GetProperty("positions").EnumerateArray());
    }

    [Fact]
    public async Task Performance_rejects_portfolios_over_the_per_request_quote_budget()
    {
        var marketData = new CountingMarketDataProvider();
        await using var fixture = await PortfolioFixture.CreateAsync(marketData);
        var account = await CreateSignedInAccountAsync(fixture, "large-performance@example.com");
        var positions = Enumerable.Range(0, 21)
            .Select(index => ($"TEST{index}", 1m, 100m))
            .ToArray();
        await SeedPortfolioAsync(fixture, account.Id, 97_900m, "USD", positions);

        using var response = await GetWithTokenAsync(fixture.Client, account.Token,
            "/api/portfolio/performance");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("portfolio_performance_unavailable", await ErrorCodeAsync(response));
        Assert.Equal(0, marketData.QuoteCalls);
    }

    [Fact]
    public async Task Performance_uses_the_configured_rate_limit_when_bounding_quote_lookups()
    {
        var marketData = new CountingMarketDataProvider();
        await using var fixture = await PortfolioFixture.CreateAsync(marketData, marketDataPermitLimit: 8);
        var account = await CreateSignedInAccountAsync(fixture, "low-rate-performance@example.com");
        var positions = Enumerable.Range(0, 9)
            .Select(index => ($"LOW{index}", 1m, 100m))
            .ToArray();
        await SeedPortfolioAsync(fixture, account.Id, 99_100m, "USD", positions);

        using var response = await GetWithTokenAsync(fixture.Client, account.Token,
            "/api/portfolio/performance");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("portfolio_performance_unavailable", await ErrorCodeAsync(response));
        Assert.Equal(0, marketData.QuoteCalls);
    }

    [Fact]
    public async Task Full_history_pages_old_trades_and_keeps_account_totals_currency_and_ownership()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "history@example.com");
        var other = await CreateSignedInAccountAsync(fixture, "history-other@example.com");
        var portfolioId = await SeedPortfolioAsync(fixture, account.Id, 100_000m, "CAD");
        var otherId = await SeedPortfolioAsync(fixture, other.Id, 100_000m, "USD");
        using (var scope = fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            for (var i = 0; i < 65; i++)
                context.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(), OrderId = Guid.NewGuid(), PortfolioId = portfolioId,
                    Side = "BUY", Symbol = "AAPL:NASDAQ", Quantity = 2m,
                    ExecutionPrice = 100m, TotalAmount = 200m,
                    ExecutedAtUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc).AddDays(i)
                });
            context.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(), OrderId = Guid.NewGuid(), PortfolioId = portfolioId,
                Side = "SELL", Symbol = "MSFT:NASDAQ", Quantity = 0.25m,
                ExecutionPrice = 12.3456m, TotalAmount = 3.0864m,
                ExecutedAtUtc = new DateTime(2026, 1, 1, 23, 59, 59, DateTimeKind.Utc)
            });
            await context.SaveChangesAsync();
        }
        await SeedTransactionAsync(fixture, otherId, "PRIVATE", DateTime.UtcNow);

        var ids = new HashSet<Guid>();
        for (var page = 1; page <= 7; page++)
        {
            using var response = await GetWithTokenAsync(fixture.Client, account.Token,
                $"/api/portfolio/transactions/history?page={page}&pageSize=10&userId={other.Id}&portfolioId={otherId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = body.RootElement;
            Assert.Equal(page, root.GetProperty("page").GetInt32());
            Assert.Equal(66, root.GetProperty("totalCount").GetInt32());
            Assert.Equal("CAD", root.GetProperty("currency").GetString());
            Assert.Equal(66, root.GetProperty("summary").GetProperty("totalTrades").GetInt32());
            Assert.Equal(13_000m, decimal.Parse(root.GetProperty("summary").GetProperty("totalInvested").GetString()!, CultureInfo.InvariantCulture));
            Assert.Equal("3.0864", root.GetProperty("summary").GetProperty("totalProceeds").GetString());
            var rows = root.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(page == 7 ? 6 : 10, rows.Length);
            foreach (var row in rows)
            {
                Assert.True(ids.Add(row.GetProperty("id").GetGuid()));
                Assert.NotEqual("PRIVATE", row.GetProperty("symbol").GetString());
                Assert.EndsWith("Z", row.GetProperty("executedAtUtc").GetString());
            }
        }
        Assert.Equal(66, ids.Count);

        using var filtered = await GetWithTokenAsync(fixture.Client, account.Token,
            "/api/portfolio/transactions/history?search=msft&side=SELL&from=2026-01-01&to=2026-01-01&page=2147483647&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        using var filteredBody = JsonDocument.Parse(await filtered.Content.ReadAsStringAsync());
        var result = filteredBody.RootElement;
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, result.GetProperty("page").GetInt32());
        Assert.Equal(66, result.GetProperty("summary").GetProperty("totalTrades").GetInt32());
        var oldTrade = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal(0.25m, oldTrade.GetProperty("quantity").GetDecimal());
        Assert.Equal(12.3456m, oldTrade.GetProperty("executionPrice").GetDecimal());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=51")]
    [InlineData("side=HOLD")]
    [InlineData("from=2026-02-31")]
    [InlineData("from=2026-02-02&to=2026-02-01")]
    public async Task History_rejects_invalid_filters(string query)
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "invalid-history@example.com");
        using var response = await GetWithTokenAsync(fixture.Client, account.Token,
            $"/api/portfolio/transactions/history?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task History_distinguishes_empty_portfolio_missing_portfolio_and_unauthorized()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        using var anonymous = await fixture.Client.GetAsync("/api/portfolio/transactions/history");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var unknown = await GetWithTokenAsync(fixture.Client, CreateSignedToken(Guid.NewGuid().ToString()),
            "/api/portfolio/transactions/history");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var account = await CreateSignedInAccountAsync(fixture, "empty-history@example.com");
        using var empty = await GetWithTokenAsync(fixture.Client, account.Token,
            "/api/portfolio/transactions/history?to=9999-12-31&page=50");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        using var body = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
        Assert.Empty(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("0", body.RootElement.GetProperty("summary").GetProperty("totalInvested").GetString());
    }

    [Fact]
    public async Task Jwt_subject_isolates_cash_and_holdings_even_when_another_users_ids_are_supplied()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var accountA = await CreateSignedInAccountAsync(fixture, "a@example.com");
        var accountB = await CreateSignedInAccountAsync(fixture, "b@example.com");
        var portfolioA = await SeedPortfolioAsync(fixture, accountA.Id, 800m, "USD", ("AAPL", 1m, 200m));
        var portfolioB = await SeedPortfolioAsync(fixture, accountB.Id, 500m, "CAD", ("MSFT", 5m, 300m));

        using var responseA = await GetWithTokenAsync(fixture.Client, accountA.Token,
            $"/api/portfolio?userId={accountB.Id}&portfolioId={portfolioB}");
        using var responseB = await GetWithTokenAsync(fixture.Client, accountB.Token,
            $"/api/portfolio?userId={accountA.Id}&portfolioId={portfolioA}");

        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        using var bodyA = JsonDocument.Parse(await responseA.Content.ReadAsStringAsync());
        using var bodyB = JsonDocument.Parse(await responseB.Content.ReadAsStringAsync());
        AssertBalances(bodyA.RootElement, 800m, 100_000m, 200m, 1_000m, "USD");
        AssertBalances(bodyB.RootElement, 500m, 100_000m, 1_500m, 2_000m, "CAD");
        AssertPosition(Assert.Single(bodyA.RootElement.GetProperty("positions").EnumerateArray()), "AAPL", 1m, 200m);
        AssertPosition(Assert.Single(bodyB.RootElement.GetProperty("positions").EnumerateArray()), "MSFT", 5m, 300m);

        using var byPortfolioId = await GetWithTokenAsync(fixture.Client, accountA.Token, $"/api/portfolio/{portfolioB}");
        using var byUserId = await GetWithTokenAsync(fixture.Client, accountA.Token, $"/api/portfolio/{accountB.Id}");
        Assert.Equal(HttpStatusCode.NotFound, byPortfolioId.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byUserId.StatusCode);
    }

    [Fact]
    public async Task Missing_portfolio_returns_not_found_without_creating_one()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "missing@example.com");
        using (var scope = fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            context.Portfolios.Remove(await context.Portfolios.SingleAsync(p => p.UserId == account.Id));
            await context.SaveChangesAsync();
        }

        using var response = await GetWithTokenAsync(fixture.Client, account.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("portfolio_not_found", await ErrorCodeAsync(response));
        using var verification = fixture.CreateScope();
        var database = verification.ServiceProvider.GetRequiredService<StockLabDbContext>();
        Assert.True(await database.Users.AnyAsync(user => user.Id == account.Id));
        Assert.Empty(await database.Portfolios.ToArrayAsync());
    }

    [Fact]
    public async Task Unknown_subject_returns_not_found_instead_of_another_users_portfolio()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        await CreateSignedInAccountAsync(fixture, "existing@example.com");

        using var response = await GetWithTokenAsync(fixture.Client, CreateSignedToken(Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("portfolio_not_found", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Database_failure_returns_safe_internal_server_error()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();
        var account = await CreateSignedInAccountAsync(fixture, "failure@example.com");
        using (var scope = fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            await context.Database.ExecuteSqlRawAsync("DROP TABLE Holdings");
        }

        using var response = await GetWithTokenAsync(fixture.Client, account.Token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("internal_server_error", await ErrorCodeAsync(response));
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Sqlite", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Holdings", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenApi_documents_bearer_authentication_statuses_and_public_response_fields()
    {
        await using var fixture = await PortfolioFixture.CreateAsync();

        using var response = await fixture.Client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("paths").TryGetProperty("/api/portfolio", out var path));
        var operation = path.GetProperty("get");
        foreach (var status in new[] { "200", "401", "404", "500" })
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        Assert.Contains(operation.GetProperty("security").EnumerateArray(), requirement =>
            requirement.EnumerateObject().Any(scheme => scheme.Name == "Bearer"));
        var schema = ResolveSchema(root, operation.GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        Assert.Equal(new[] { "cashBalance", "currency", "initialCapital", "investedValue", "positions", "totalValue" },
            schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(name => name).ToArray());
        var position = ResolveSchema(root, schema.GetProperty("properties").GetProperty("positions").GetProperty("items"));
        Assert.Equal(new[] { "averageCost", "quantity", "symbol" },
            position.GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(name => name).ToArray());
        var schemas = root.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "PortfolioResponse", "PortfolioPerformanceResponse", "PaperTradeResponse" })
            Assert.Equal("string", schemas.GetProperty(name).GetProperty("properties").GetProperty("cashBalance").GetProperty("type").GetString());
        foreach (var name in new[] { "initialCapital", "investedValue", "totalValue" })
            Assert.Equal("string", schemas.GetProperty("PortfolioResponse").GetProperty("properties").GetProperty(name).GetProperty("type").GetString());
        foreach (var name in new[] { "PortfolioTransactionResponse", "PaperTradeResponse" })
            Assert.Equal("string", schemas.GetProperty(name).GetProperty("properties").GetProperty("totalAmount").GetProperty("type").GetString());
        foreach (var name in new[] { "totalInvested", "totalProceeds" })
            Assert.Equal("string", schemas.GetProperty("TransactionHistorySummaryResponse").GetProperty("properties").GetProperty(name).GetProperty("type").GetString());
    }

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema) =>
        schema.TryGetProperty("$ref", out var reference)
            ? root.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/').Last())
            : schema;

    private static void AssertBalances(JsonElement body, decimal cash, decimal initialCapital, decimal invested, decimal total, string currency)
    {
        Assert.Equal(cash, decimal.Parse(body.GetProperty("cashBalance").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(initialCapital, decimal.Parse(body.GetProperty("initialCapital").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(invested, decimal.Parse(body.GetProperty("investedValue").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(total, decimal.Parse(body.GetProperty("totalValue").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(currency, body.GetProperty("currency").GetString());
    }

    private static void AssertPublicFields(JsonElement body)
    {
        Assert.Equal(new[] { "cashBalance", "currency", "initialCapital", "investedValue", "positions", "totalValue" },
            body.EnumerateObject().Select(p => p.Name).OrderBy(name => name).ToArray());
        foreach (var position in body.GetProperty("positions").EnumerateArray())
            Assert.Equal(new[] { "averageCost", "quantity", "symbol" },
                position.EnumerateObject().Select(p => p.Name).OrderBy(name => name).ToArray());
    }

    private static void AssertPosition(JsonElement position, string symbol, decimal quantity, decimal averageCost)
    {
        Assert.Equal(symbol, position.GetProperty("symbol").GetString());
        Assert.Equal(quantity, position.GetProperty("quantity").GetDecimal());
        Assert.Equal(averageCost, position.GetProperty("averageCost").GetDecimal());
    }

    private static async Task<(Guid Id, string Token)> CreateSignedInAccountAsync(PortfolioFixture fixture, string email)
    {
        using var payload = new StringContent(JsonSerializer.Serialize(new
        {
            displayName = "Portfolio Test User", email, password = "correct horse battery staple"
        }), Encoding.UTF8, "application/json");
        using var registrationResponse = await fixture.Client.PostAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.Created, registrationResponse.StatusCode);
        using var registration = JsonDocument.Parse(await registrationResponse.Content.ReadAsStringAsync());

        using var loginPayload = new StringContent(JsonSerializer.Serialize(new
        {
            email, password = "correct horse battery staple"
        }), Encoding.UTF8, "application/json");
        using var loginResponse = await fixture.Client.PostAsync("/api/auth/login", loginPayload);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        using var login = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        return (registration.RootElement.GetProperty("id").GetGuid(), login.RootElement.GetProperty("accessToken").GetString()!);
    }

    private static async Task<Guid> SeedPortfolioAsync(PortfolioFixture fixture, Guid userId, decimal cash, string currency,
        params (string Symbol, decimal Quantity, decimal AverageCost)[] positions)
    {
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var portfolio = await context.Portfolios.SingleAsync(p => p.UserId == userId);
        portfolio.CashBalance = cash;
        portfolio.Currency = currency;
        foreach (var position in positions)
            context.Holdings.Add(new Holding
            {
                Id = Guid.NewGuid(), PortfolioId = portfolio.Id, Symbol = position.Symbol,
                Quantity = position.Quantity, AverageCost = position.AverageCost, UpdatedAtUtc = DateTime.UtcNow
            });
        await context.SaveChangesAsync();
        return portfolio.Id;
    }

    private static async Task SeedTransactionAsync(PortfolioFixture fixture, Guid portfolioId, string symbol, DateTime executedAtUtc,
        decimal amount = 200m)
    {
        using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        context.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            PortfolioId = portfolioId,
            OrderId = Guid.NewGuid(),
            Side = "BUY",
            Symbol = symbol,
            Quantity = 2m,
            ExecutionPrice = 100m,
            TotalAmount = amount,
            ExecutedAtUtc = executedAtUtc,
        });
        await context.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> GetWithTokenAsync(HttpClient client, string token,
        string url = "/api/portfolio")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static string CreateSignedToken(string? subject, bool expired = false, string signingKey = TestSigningKey)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(TestIssuer, TestAudience,
            subject is null ? [] : [new Claim(JwtRegisteredClaimNames.Sub, subject)],
            notBefore: now.AddHours(-2), expires: expired ? now.AddHours(-1) : now.AddMinutes(30),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetString()!;
    }

    private sealed class PortfolioFixture(SqliteConnection connection, WebApplicationFactory<Program> application,
        HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public IServiceScope CreateScope() => application.Services.CreateScope();

        public static async Task<PortfolioFixture> CreateAsync(
            IMarketDataProvider? marketDataProvider = null,
            int? marketDataPermitLimit = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite(connection).Options;
            var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    var settings = new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:StockLab"] = "Server=localhost;Database=PortfolioTestsNeverUsed;Integrated Security=true;TrustServerCertificate=true",
                        ["Jwt:Issuer"] = TestIssuer,
                        ["Jwt:Audience"] = TestAudience,
                        ["Jwt:SigningKey"] = TestSigningKey,
                        ["MarketData:Provider"] = "Mock"
                    };
                    if (marketDataPermitLimit is not null)
                        settings["MarketDataRateLimit:PermitLimit"] = marketDataPermitLimit.Value.ToString(CultureInfo.InvariantCulture);
                    configuration.AddInMemoryCollection(settings);
                });
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<StockLabDbContext>>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<StockLabDbContext>();
                    services.AddSingleton(options);
                    services.AddScoped<StockLabDbContext, SqlitePortfolioDbContext>();
                    if (marketDataProvider is not null)
                    {
                        services.RemoveAll<IMarketDataProvider>();
                        services.AddSingleton(marketDataProvider);
                    }
                });
            });
            try
            {
                var client = application.CreateClient();
                using var scope = application.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Database.EnsureCreatedAsync();
                return new PortfolioFixture(connection, application, client);
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

    private sealed class SqlitePortfolioDbContext(DbContextOptions<StockLabDbContext> options) : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().Property(user => user.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(portfolio => portfolio.Version).ValueGeneratedNever();
            modelBuilder.Entity<Transaction>().Property(transaction => transaction.TotalAmount).HasColumnType("TEXT");
            modelBuilder.Entity<Portfolio>().Property(portfolio => portfolio.CashBalance).HasColumnType("TEXT");
        }
    }

    private sealed class CountingMarketDataProvider : IMarketDataProvider
    {
        public int QuoteCalls { get; private set; }

        public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            QuoteCalls++;
            return Task.FromResult<StockQuote?>(new StockQuote(
                symbol, "USD", 100m, 0m, 0m, null, DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(
            string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockSearchResult>>(Array.Empty<StockSearchResult>());

        public Task<StockHistory?> GetHistoryAsync(
            StockHistoryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<StockHistory?>(null);
    }
}
