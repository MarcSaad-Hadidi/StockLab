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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Watchlists;

namespace StockLab.UnitTests.Api;

public sealed class WatchlistApiTests
{
    private const string Issuer = "StockLab.Watchlist.Tests";
    private const string Audience = "StockLab.Tests";
    private const string SigningKey = "test-only-signing-key-at-least-32-bytes-long";

    [Theory]
    [InlineData("GET", "/api/watchlist")]
    [InlineData("POST", "/api/watchlist")]
    [InlineData("DELETE", "/api/watchlist/AAPL")]
    public async Task Endpoints_require_authentication(string method, string path)
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        using var response = await SendAsync(fixture.Client, method, path, null, """{"symbol":"AAPL"}""");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("invalid_signature")]
    [InlineData("expired")]
    [InlineData("missing_sub")]
    [InlineData("invalid_sub")]
    public async Task Invalid_tokens_cannot_read_or_modify_the_watchlist(string scenario)
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        var token = scenario switch
        {
            "malformed" => "not-a-jwt",
            "invalid_signature" => SignedToken(fixture.UserA.ToString(), signingKey: new string('x', 40)),
            "expired" => SignedToken(fixture.UserA.ToString(), expired: true),
            "missing_sub" => SignedToken(null),
            _ => SignedToken("not-a-guid")
        };
        foreach (var (method, path) in new[]
                 { ("GET", "/api/watchlist"), ("POST", "/api/watchlist"), ("DELETE", "/api/watchlist/AAPL") })
        {
            using var response = await SendAsync(fixture.Client, method, path, token, """{"symbol":"AAPL"}""");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("unauthorized", await ErrorCodeAsync(response));
        }
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Watchlists.ToArrayAsync());
    }

    [Fact]
    public async Task New_users_receive_an_empty_array()
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        Assert.Empty(await GetItemsAsync(fixture, fixture.TokenA));
    }

    [Fact]
    public async Task Add_normalizes_persists_and_uses_only_sub_without_other_side_effects()
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        var payload = JsonSerializer.Serialize(new { symbol = "  aapl  ", userId = fixture.UserB });
        using var response = await SendAsync(fixture.Client, "POST",
            $"/api/watchlist?userId={fixture.UserB}", fixture.TokenA, payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/watchlist", response.Headers.Location?.OriginalString);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertItem(body.RootElement, "AAPL", fixture.Clock.Now.UtcDateTime);
        AssertItem(Assert.Single(await GetItemsAsync(fixture, fixture.TokenA)), "AAPL", fixture.Clock.Now.UtcDateTime);

        await using var db = fixture.CreateDbContext();
        var row = Assert.Single(await db.Watchlists.AsNoTracking().ToArrayAsync());
        Assert.NotEqual(Guid.Empty, row.Id);
        Assert.Equal(fixture.UserA, row.UserId);
        Assert.Equal("AAPL", row.Symbol);
        Assert.Equal(fixture.Clock.Now.UtcDateTime, row.CreatedAtUtc);
        Assert.All(await db.Portfolios.AsNoTracking().ToArrayAsync(), portfolio =>
        {
            Assert.Equal(100_000m, portfolio.CashBalance);
            Assert.Equal(100_000m, portfolio.InitialCapital);
        });
        Assert.Equal(2, await db.Portfolios.CountAsync());
        Assert.Empty(await db.Holdings.ToArrayAsync());
        Assert.Empty(await db.Transactions.ToArrayAsync());
        Assert.Empty(await db.PriceAlerts.ToArrayAsync());
        Assert.Equal(0, fixture.Market.Calls);
    }

    public static IEnumerable<object[]> InvalidAddRequests => new[]
    {
        "{}", """{"symbol":null}""", """{"symbol":""}""", """{"symbol":"   "}""",
        JsonSerializer.Serialize(new { symbol = new string('X', 33) }), """{"symbol":42}""", "{",
        """{"symbol":"BTC/USD"}""", """{"symbol":"/"}""",
        """{"symbol":"."}""", """{"symbol":".."}""",
        """{"symbol":"  .  "}""", """{"symbol":"  ..  "}""",
        JsonSerializer.Serialize(new { symbol = "A\0B" }),
        JsonSerializer.Serialize(new { symbol = "\0AAPL" }),
        JsonSerializer.Serialize(new { symbol = "AAPL\0" }),
        JsonSerializer.Serialize(new { symbol = "A\tB" }),
        JsonSerializer.Serialize(new { symbol = "A\nB" }),
        JsonSerializer.Serialize(new { symbol = "A\rB" }),
        JsonSerializer.Serialize(new { symbol = "A\u001FB" }),
        JsonSerializer.Serialize(new { symbol = "A\u007FB" }),
        JsonSerializer.Serialize(new { symbol = "A\u0085B" }),
        JsonSerializer.Serialize(new { symbol = "A\u009FB" })
    }.Select(payload => new object[] { payload });

    [Theory]
    [MemberData(nameof(InvalidAddRequests))]
    public async Task Invalid_symbols_are_rejected_before_writing(string payload)
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        using var response = await SendAsync(fixture.Client, "POST", "/api/watchlist", fixture.TokenA, payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_error", await ErrorCodeAsync(response));
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Watchlists.ToArrayAsync());
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("  .  ")]
    [InlineData("  ..  ")]
    [InlineData("A\0B")]
    [InlineData("\0AAPL")]
    [InlineData("AAPL\0")]
    [InlineData("A\tB")]
    [InlineData("A\nB")]
    [InlineData("A\rB")]
    [InlineData("A\u001FB")]
    [InlineData("A\u007FB")]
    [InlineData("A\u0085B")]
    [InlineData("A\u009FB")]
    public async Task Service_rejects_invalid_symbols_after_trimming(string symbol)
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        await using var db = fixture.CreateDbContext();
        var service = new WatchlistService(db, fixture.Clock);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(fixture.UserA, symbol, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RemoveAsync(fixture.UserA, symbol, CancellationToken.None));
        Assert.Empty(await db.Watchlists.ToArrayAsync());
    }

    [Fact]
    public async Task Symbols_with_32_characters_after_trimming_are_accepted()
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        using var response = await AddAsync(fixture, fixture.TokenA, "  " + new string('x', 32) + "  ");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new string('X', 32), Assert.Single(await GetItemsAsync(fixture, fixture.TokenA)).GetProperty("symbol").GetString());
    }

    [Fact]
    public async Task Duplicate_symbols_after_normalization_return_conflict()
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        using var first = await AddAsync(fixture, fixture.TokenA, "AAPL");
        using var duplicate = await AddAsync(fixture, fixture.TokenA, " aapl ");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("watchlist_item_already_exists", await ErrorCodeAsync(duplicate));
        await using var db = fixture.CreateDbContext();
        Assert.Single(await db.Watchlists.ToArrayAsync());
    }

    [Fact]
    public async Task Lists_are_isolated_and_the_same_symbol_is_allowed_for_two_users()
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        using var aaplA = await AddAsync(fixture, fixture.TokenA, "AAPL");
        using var msftB = await AddAsync(fixture, fixture.TokenB, "MSFT");
        Assert.Equal(HttpStatusCode.Created, aaplA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, msftB.StatusCode);
        Assert.Equal("AAPL", Assert.Single(await GetItemsAsync(fixture, fixture.TokenA)).GetProperty("symbol").GetString());
        Assert.Equal("MSFT", Assert.Single(await GetItemsAsync(fixture, fixture.TokenB)).GetProperty("symbol").GetString());

        using var aaplB = await AddAsync(fixture, fixture.TokenB, "aapl");
        Assert.Equal(HttpStatusCode.Created, aaplB.StatusCode);
        await using var db = fixture.CreateDbContext();
        var rows = await db.Watchlists.Where(row => row.Symbol == "AAPL").ToArrayAsync();
        Assert.Equal(2, rows.Length);
        Assert.Equal(new[] { fixture.UserA, fixture.UserB }.Order(), rows.Select(row => row.UserId).Order());
    }

    [Fact]
    public async Task Listing_orders_newest_first_with_a_symbol_tiebreaker()
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        using var msft = await AddAsync(fixture, fixture.TokenA, "MSFT");
        using var aapl = await AddAsync(fixture, fixture.TokenA, "AAPL");
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(1);
        using var nvda = await AddAsync(fixture, fixture.TokenA, "NVDA");
        Assert.Equal(new[] { "NVDA", "AAPL", "MSFT" },
            (await GetItemsAsync(fixture, fixture.TokenA)).Select(item => item.GetProperty("symbol").GetString()));
    }

    [Fact]
    public async Task Delete_normalizes_the_symbol_and_preserves_other_users_and_symbols()
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        using var aaplA = await AddAsync(fixture, fixture.TokenA, "AAPL");
        using var msftA = await AddAsync(fixture, fixture.TokenA, "MSFT");
        using var aaplB = await AddAsync(fixture, fixture.TokenB, "AAPL");
        using var response = await SendAsync(fixture.Client, "DELETE", "/api/watchlist/%20aapl%20", fixture.TokenA);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.Equal("MSFT", Assert.Single(await GetItemsAsync(fixture, fixture.TokenA)).GetProperty("symbol").GetString());
        Assert.Equal("AAPL", Assert.Single(await GetItemsAsync(fixture, fixture.TokenB)).GetProperty("symbol").GetString());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.Watchlists.CountAsync());
    }

    [Fact]
    public async Task Deleting_absent_or_another_users_symbol_returns_not_found()
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        using var added = await AddAsync(fixture, fixture.TokenB, "NVDA");
        using var response = await SendAsync(fixture.Client, "DELETE", "/api/watchlist/NVDA", fixture.TokenA);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("watchlist_item_not_found", await ErrorCodeAsync(response));
        Assert.Single(await GetItemsAsync(fixture, fixture.TokenB));
    }

    [Theory]
    [InlineData("%20%20%20")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567")]
    [InlineData("%20.%20")]
    [InlineData("%20..%20")]
    [InlineData("A%09B")]
    [InlineData("A%7FB")]
    [InlineData("A%C2%85B")]
    public async Task Delete_validates_the_symbol(string symbol)
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        using var response = await SendAsync(fixture.Client, "DELETE", "/api/watchlist/" + symbol, fixture.TokenA);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_error", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("BRK.B")]
    [InlineData("AAPL:NASDAQ")]
    [InlineData("BTC%2FUSD")]
    [InlineData("PERCENT%")]
    [InlineData("%2E")]
    [InlineData("%2E%2E")]
    [InlineData("...")]
    [InlineData("AAPL..")]
    [InlineData("A%00B")]
    [InlineData("A%09B")]
    public async Task Accepted_symbols_round_trip_through_an_encoded_delete_path(string symbol)
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        using var added = await AddAsync(fixture, fixture.TokenA, symbol);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        using var deleted = await SendAsync(fixture.Client, "DELETE",
            "/api/watchlist/" + Uri.EscapeDataString(symbol), fixture.TokenA);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await GetItemsAsync(fixture, fixture.TokenA));
    }

    [Fact]
    public async Task Concurrent_deletes_that_both_load_the_row_return_no_content_and_not_found()
    {
        var gate = new ConcurrentWatchlistSaveGate(EntityState.Deleted);
        await using var fixture = await WatchlistFixture.CreateAsync(gate);
        using var added = await AddAsync(fixture, fixture.TokenA, "AAPL");
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var responses = await Task.WhenAll(
            SendAsync(fixture.Client, "DELETE", "/api/watchlist/AAPL", fixture.TokenA),
            SendAsync(fixture.Client, "DELETE", "/api/watchlist/AAPL", fixture.TokenA))
            .WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            Assert.Equal(2, gate.Arrivals);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
            var missing = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NotFound);
            Assert.Equal("watchlist_item_not_found", await ErrorCodeAsync(missing));
            Assert.Empty(await GetItemsAsync(fixture, fixture.TokenA));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Concurrent_adds_that_both_pass_the_precheck_return_one_created_and_one_conflict()
    {
        var gate = new ConcurrentWatchlistSaveGate();
        await using var fixture = await WatchlistFixture.CreateAsync(gate);
        var responses = await Task.WhenAll(
            AddAsync(fixture, fixture.TokenA, "AAPL"), AddAsync(fixture, fixture.TokenA, " aapl "))
            .WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            Assert.Equal(2, gate.Arrivals);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("watchlist_item_already_exists", await ErrorCodeAsync(conflict));
            await using var db = fixture.CreateDbContext();
            Assert.Single(await db.Watchlists.ToArrayAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Unexpected_database_errors_remain_safe_server_errors()
    {
        await using var fixture = await WatchlistFixture.CreateAsync(new RejectWatchlistSave());
        using var response = await AddAsync(fixture, fixture.TokenA, "AAPL");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("internal_server_error", await ErrorCodeAsync(response));
        Assert.DoesNotContain("private database details", await response.Content.ReadAsStringAsync());
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Watchlists.ToArrayAsync());
    }

    [Fact]
    public async Task Registered_users_watchlist_survives_application_restart_and_can_be_deleted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stocklab-watchlist-{Guid.NewGuid():N}.db");
        try
        {
            string token;
            await using (var first = await WatchlistFixture.CreateAsync(databasePath: path))
            {
                using var registered = await SendAsync(first.Client, "POST", "/api/auth/register", null,
                    """{"displayName":"Persistence","email":"persistence@example.com","password":"correct horse battery staple"}""");
                Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
                using var login = await SendAsync(first.Client, "POST", "/api/auth/login", null,
                    """{"email":"persistence@example.com","password":"correct horse battery staple"}""");
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
                token = body.RootElement.GetProperty("accessToken").GetString()!;
                using var added = await AddAsync(first, token, "AAPL");
                Assert.Equal(HttpStatusCode.Created, added.StatusCode);
                Assert.Single(await GetItemsAsync(first, token));
            }
            await using var restarted = await WatchlistFixture.CreateAsync(databasePath: path);
            Assert.Equal("AAPL", Assert.Single(await GetItemsAsync(restarted, token)).GetProperty("symbol").GetString());
            using var deleted = await SendAsync(restarted.Client, "DELETE", "/api/watchlist/AAPL", token);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.Empty(await GetItemsAsync(restarted, token));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenApi_documents_bearer_authentication_statuses_and_public_fields()
    {
        await using var fixture = await WatchlistFixture.CreateAsync();
        using var response = await fixture.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        foreach (var (path, method, statuses) in new[]
                 {
                     ("/api/watchlist", "get", new[] { "200", "401", "500" }),
                     ("/api/watchlist", "post", new[] { "201", "400", "401", "409", "500" }),
                     ("/api/watchlist/{symbol}", "delete", new[] { "204", "400", "401", "404", "500" })
                 })
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            foreach (var status in statuses) Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
            Assert.Contains(operation.GetProperty("security").EnumerateArray(), requirement => requirement.TryGetProperty("Bearer", out _));
        }
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal(new[] { "symbol", "createdAtUtc" }, schemas.GetProperty("WatchlistItemResponse")
            .GetProperty("properties").EnumerateObject().Select(property => property.Name));
        Assert.Equal(new[] { "symbol" }, schemas.GetProperty("AddWatchlistItemRequest")
            .GetProperty("properties").EnumerateObject().Select(property => property.Name));
    }

    private static void AssertItem(JsonElement item, string symbol, DateTime createdAtUtc)
    {
        Assert.Equal(new[] { "symbol", "createdAtUtc" }, item.EnumerateObject().Select(property => property.Name));
        Assert.Equal(symbol, item.GetProperty("symbol").GetString());
        Assert.Equal(createdAtUtc, item.GetProperty("createdAtUtc").GetDateTime());
        Assert.EndsWith("Z", item.GetProperty("createdAtUtc").GetString());
    }

    private static Task<HttpResponseMessage> AddAsync(WatchlistFixture fixture, string token, string symbol) =>
        SendAsync(fixture.Client, "POST", "/api/watchlist", token, JsonSerializer.Serialize(new { symbol }));

    private static async Task<JsonElement[]> GetItemsAsync(WatchlistFixture fixture, string token)
    {
        using var response = await SendAsync(fixture.Client, "GET", "/api/watchlist", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path,
        string? token, string? payload = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null && method == "POST") request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static string SignedToken(string? subject, bool expired = false, string signingKey = SigningKey)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (subject is not null) claims.Add(new(JwtRegisteredClaimNames.Sub, subject));
        var token = new JwtSecurityToken(Issuer, Audience, claims, now.AddHours(-2),
            expired ? now.AddHours(-1) : now.AddMinutes(30),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetString()!;
    }

    private sealed class WatchlistFixture(WebApplicationFactory<Program> application, HttpClient client,
        DbContextOptions<StockLabDbContext> options, string path, bool ownsDatabase, Clock clock) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public Clock Clock { get; } = clock;
        public RejectingMarket Market { get; } = application.Services.GetRequiredService<RejectingMarket>();
        public Guid UserA { get; private set; }
        public Guid UserB { get; private set; }
        public string TokenA => SignedToken(UserA.ToString());
        public string TokenB => SignedToken(UserB.ToString());
        public StockLabDbContext CreateDbContext() => new SqliteWatchlistDbContext(options);

        public static async Task<WatchlistFixture> CreateAsync(SaveChangesInterceptor? interceptor = null, string? databasePath = null)
        {
            var path = databasePath ?? Path.Combine(Path.GetTempPath(), $"stocklab-watchlist-{Guid.NewGuid():N}.db");
            var optionsBuilder = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite($"Data Source={path};Pooling=False");
            if (interceptor is not null) optionsBuilder.AddInterceptors(interceptor);
            var options = optionsBuilder.Options;
            var clock = new Clock();
            var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:StockLab"] = "Server=localhost;Database=WatchlistTestsNeverUsed;Integrated Security=true;TrustServerCertificate=true",
                    ["Jwt:Issuer"] = Issuer, ["Jwt:Audience"] = Audience, ["Jwt:SigningKey"] = SigningKey,
                    ["MarketData:Provider"] = "Mock"
                }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<StockLabDbContext>>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<StockLabDbContext>();
                    services.AddSingleton(options);
                    services.AddScoped<StockLabDbContext, SqliteWatchlistDbContext>();
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(clock);
                    services.RemoveAll<IMarketDataProvider>();
                    services.AddSingleton<RejectingMarket>();
                    services.AddSingleton<IMarketDataProvider>(provider => provider.GetRequiredService<RejectingMarket>());
                });
            });
            var fixture = new WatchlistFixture(application, application.CreateClient(), options, path, databasePath is null, clock);
            try
            {
                await using var db = fixture.CreateDbContext();
                await db.Database.EnsureCreatedAsync();
                if (!await db.Users.AnyAsync())
                {
                    foreach (var name in new[] { "A", "B" })
                    {
                        var user = new User
                        {
                            Id = Guid.NewGuid(), DisplayName = name, Email = name + "@example.com", NormalizedEmail = name + "@EXAMPLE.COM",
                            PasswordHash = "test-only-hash", CreatedAtUtc = clock.Now.UtcDateTime, UpdatedAtUtc = clock.Now.UtcDateTime
                        };
                        user.Portfolio = new Portfolio { Id = Guid.NewGuid(), UserId = user.Id, CreatedAtUtc = clock.Now.UtcDateTime };
                        db.Users.Add(user);
                    }
                    await db.SaveChangesAsync();
                }
                fixture.UserA = await db.Users.Where(user => user.DisplayName == "A").Select(user => user.Id).SingleAsync();
                fixture.UserB = await db.Users.Where(user => user.DisplayName == "B").Select(user => user.Id).SingleAsync();
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.DisposeAsync();
            if (ownsDatabase) File.Delete(path);
        }
    }

    private sealed class SqliteWatchlistDbContext(DbContextOptions<StockLabDbContext> options) : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().Property(user => user.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(portfolio => portfolio.Version).ValueGeneratedNever();
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RejectingMarket : IMarketDataProvider
    {
        public int Calls { get; private set; }
        public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Watchlist must not request market data."); }
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Watchlist must not request market data."); }
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Watchlist must not request market data."); }
    }

    private sealed class ConcurrentWatchlistSaveGate(EntityState state = EntityState.Added) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public int Arrivals => Volatile.Read(ref arrivals);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Watchlist>().Any(entry => entry.State == state) == true)
            {
                if (Interlocked.Increment(ref arrivals) == 2) bothArrived.TrySetResult();
                await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }

    private sealed class RejectWatchlistSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Watchlist>().Any(entry => entry.State == EntityState.Added) == true)
                throw new DbUpdateException("private database details");
            return ValueTask.FromResult(result);
        }
    }
}
