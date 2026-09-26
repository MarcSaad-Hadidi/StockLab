using System.IdentityModel.Tokens.Jwt;
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
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Api;

public sealed class PortfolioApiTests
{
    private const string TestIssuer = "StockLab.Api.Tests";
    private const string TestAudience = "StockLab.Tests";
    private const string TestSigningKey = "test-only-signing-key-at-least-32-bytes-long";

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
        AssertBalances(body.RootElement, 100_000m, 0m, 100_000m, "USD");
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
        AssertBalances(body.RootElement, 97_000m, 3_000m, 100_000m, "USD");
        var positions = body.RootElement.GetProperty("positions").EnumerateArray().ToArray();
        Assert.Equal(2, positions.Length);
        AssertPosition(positions.Single(p => p.GetProperty("symbol").GetString() == "AAPL"), "AAPL", 10m, 150m);
        AssertPosition(positions.Single(p => p.GetProperty("symbol").GetString() == "MSFT"), "MSFT", 5m, 300m);
        AssertPublicFields(body.RootElement);
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
        AssertBalances(body.RootElement, 9_876.5432m, 268.834678m, 10_145.377878m, "CAD");
        var positions = body.RootElement.GetProperty("positions").EnumerateArray().ToArray();
        Assert.Equal(3, positions.Length);
        AssertPosition(positions.Single(p => p.GetProperty("symbol").GetString() == "NVDA"), "NVDA", 0.12345678m, 100m);
        AssertPosition(positions.Single(p => p.GetProperty("symbol").GetString() == "MSFT"), "MSFT", 2.5m, 12.3456m);
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
        AssertBalances(bodyA.RootElement, 800m, 200m, 1_000m, "USD");
        AssertBalances(bodyB.RootElement, 500m, 1_500m, 2_000m, "CAD");
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
        Assert.Equal(new[] { "cashBalance", "currency", "investedValue", "positions", "totalValue" },
            schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(name => name).ToArray());
        var position = ResolveSchema(root, schema.GetProperty("properties").GetProperty("positions").GetProperty("items"));
        Assert.Equal(new[] { "averageCost", "quantity", "symbol" },
            position.GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(name => name).ToArray());
    }

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema) =>
        schema.TryGetProperty("$ref", out var reference)
            ? root.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/').Last())
            : schema;

    private static void AssertBalances(JsonElement body, decimal cash, decimal invested, decimal total, string currency)
    {
        Assert.Equal(cash, body.GetProperty("cashBalance").GetDecimal());
        Assert.Equal(invested, body.GetProperty("investedValue").GetDecimal());
        Assert.Equal(total, body.GetProperty("totalValue").GetDecimal());
        Assert.Equal(currency, body.GetProperty("currency").GetString());
    }

    private static void AssertPublicFields(JsonElement body)
    {
        Assert.Equal(new[] { "cashBalance", "currency", "investedValue", "positions", "totalValue" },
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

        public static async Task<PortfolioFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite(connection).Options;
            var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:StockLab"] = "Server=localhost;Database=PortfolioTestsNeverUsed;Integrated Security=true;TrustServerCertificate=true",
                        ["Jwt:Issuer"] = TestIssuer,
                        ["Jwt:Audience"] = TestAudience,
                        ["Jwt:SigningKey"] = TestSigningKey,
                        ["MarketData:Provider"] = "Mock"
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<StockLabDbContext>>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<StockLabDbContext>();
                    services.AddSingleton(options);
                    services.AddScoped<StockLabDbContext, SqlitePortfolioDbContext>();
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
        }
    }
}
