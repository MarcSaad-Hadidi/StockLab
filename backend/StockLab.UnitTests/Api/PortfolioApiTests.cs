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
    private const string Issuer = "StockLab.Api.Tests";
    private const string Audience = "StockLab.Tests";
    private const string SigningKey = "test-only-signing-key-at-least-32-bytes-long";

    [Fact]
    public async Task Portfolio_requires_authentication()
    {
        await using var fixture = await PortfolioApiFixture.CreateAsync();

        using var response = await fixture.Client.GetAsync("/api/portfolio");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Portfolio_returns_only_the_authenticated_users_summary()
    {
        await using var fixture = await PortfolioApiFixture.CreateAsync();

        using var response = await fixture.GetAsync(fixture.OwnerToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(fixture.PortfolioId, root.GetProperty("portfolioId").GetGuid());
        Assert.Equal("USD", root.GetProperty("currency").GetString());
        Assert.Equal(100_000m, root.GetProperty("initialCapital").GetDecimal());
        Assert.Equal(96_250m, root.GetProperty("cashAvailable").GetDecimal());
        Assert.Equal(2_350m, root.GetProperty("investedValue").GetDecimal());
        Assert.Equal(98_600m, root.GetProperty("totalValue").GetDecimal());
        var positions = root.GetProperty("positions");
        Assert.Equal(2, positions.GetArrayLength());
        Assert.Equal("AAPL", positions[0].GetProperty("symbol").GetString());
        Assert.Equal(10m, positions[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(125m, positions[0].GetProperty("averagePrice").GetDecimal());
        Assert.Equal(1_250m, positions[0].GetProperty("value").GetDecimal());
        Assert.Equal("MSFT", positions[1].GetProperty("symbol").GetString());
        Assert.DoesNotContain("password", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transaction", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Portfolio_returns_not_found_for_an_unknown_authenticated_user()
    {
        await using var fixture = await PortfolioApiFixture.CreateAsync();
        var token = fixture.CreateToken(Guid.NewGuid());

        using var response = await fixture.GetAsync(token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("portfolio_not_found", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Portfolio_returns_empty_positions_with_cash_as_total_value()
    {
        await using var fixture = await PortfolioApiFixture.CreateAsync(includeHoldings: false);

        using var response = await fixture.GetAsync(fixture.OwnerToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(96_250m, body.RootElement.GetProperty("cashAvailable").GetDecimal());
        Assert.Equal(0m, body.RootElement.GetProperty("investedValue").GetDecimal());
        Assert.Equal(96_250m, body.RootElement.GetProperty("totalValue").GetDecimal());
        Assert.Empty(body.RootElement.GetProperty("positions").EnumerateArray());
    }

    [Fact]
    public async Task Portfolio_openapi_describes_authentication_and_response_fields()
    {
        await using var fixture = await PortfolioApiFixture.CreateAsync();

        using var response = await fixture.Client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths").GetProperty("/api/portfolio").GetProperty("get");
        Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(operation.GetProperty("responses").TryGetProperty("404", out _));
        Assert.Contains(operation.GetProperty("security").EnumerateArray(), requirement =>
            requirement.EnumerateObject().Any(scheme => scheme.Name == "Bearer"));
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetString()!;
    }

    private sealed class PortfolioApiFixture(SqliteConnection connection, WebApplicationFactory<Program> application,
        HttpClient client, Guid ownerId, Guid portfolioId) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid OwnerId { get; } = ownerId;
        public Guid PortfolioId { get; } = portfolioId;
        public string OwnerToken { get; } = CreateSignedToken(ownerId);

        public Task<HttpResponseMessage> GetAsync(string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/portfolio");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return Client.SendAsync(request);
        }

        public string CreateToken(Guid userId) => CreateSignedToken(userId);

        public static async Task<PortfolioApiFixture> CreateAsync(bool includeHoldings = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite(connection).Options;
            var ownerId = Guid.NewGuid();
            var portfolioId = Guid.NewGuid();
            await SeedAsync(options, ownerId, portfolioId, includeHoldings);
            var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:StockLab"] = "Server=localhost;Database=PortfolioTestsNeverUsed;Integrated Security=true;TrustServerCertificate=true",
                        ["Jwt:Issuer"] = Issuer,
                        ["Jwt:Audience"] = Audience,
                        ["Jwt:SigningKey"] = SigningKey,
                        ["Jwt:AccessTokenMinutes"] = "60"
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<StockLabDbContext>>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<StockLabDbContext>();
                    services.AddSingleton(options);
                    services.AddScoped<StockLabDbContext, PortfolioApiDbContext>();
                });
            });

            var client = application.CreateClient();
            return new PortfolioApiFixture(connection, application, client, ownerId, portfolioId);
        }

        private static async Task SeedAsync(DbContextOptions<StockLabDbContext> options, Guid ownerId,
            Guid portfolioId, bool includeHoldings)
        {
            await using var context = new PortfolioApiDbContext(options);
            await context.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            var user = new User
            {
                Id = ownerId, DisplayName = "Portfolio owner", Email = "owner@example.com",
                NormalizedEmail = "OWNER@EXAMPLE.COM", PasswordHash = "test-hash",
                CreatedAtUtc = now, UpdatedAtUtc = now, Version = new byte[8]
            };
            var portfolio = new Portfolio
            {
                Id = portfolioId, UserId = ownerId, User = user, Currency = "USD",
                InitialCapital = 100_000m, CashBalance = 96_250m, CreatedAtUtc = now, Version = new byte[8]
            };
            context.Users.Add(user);
            context.Portfolios.Add(portfolio);
            if (includeHoldings)
            {
                context.Holdings.AddRange(
                    new Holding
                    {
                        Id = Guid.NewGuid(), PortfolioId = portfolioId, Portfolio = portfolio,
                        Symbol = "AAPL", Quantity = 10m, AverageCost = 125m, UpdatedAtUtc = now
                    },
                    new Holding
                    {
                        Id = Guid.NewGuid(), PortfolioId = portfolioId, Portfolio = portfolio,
                        Symbol = "MSFT", Quantity = 5m, AverageCost = 220m, UpdatedAtUtc = now
                    });
            }

            await context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.DisposeAsync();
            await connection.DisposeAsync();
        }

        private static string CreateSignedToken(Guid userId)
        {
            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                Issuer, Audience, [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D"))],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(30), credentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    private sealed class PortfolioApiDbContext(DbContextOptions<StockLabDbContext> options)
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
