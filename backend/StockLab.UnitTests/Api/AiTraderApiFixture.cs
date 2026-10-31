using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Identity;
using StockLab.UnitTests.Trading;
using StockLab.Domain.Entities;

namespace StockLab.UnitTests.Api;

/// <summary>Test-only HTTP host: real application services and isolated SQLite; no external market/ML sources.</summary>
internal sealed class AiTraderApiFixture(AiCurrentPositionsFixture database,
    WebApplicationFactory<Program> application, HttpClient client, AiTraderApiFixture.Clock clock,
    AiTraderApiFixture.DatabaseObserver observer) : IAsyncDisposable
{
    private const string Issuer = "StockLab.AiTrader.Api.Tests";
    private const string Audience = "StockLab.AiTrader.Tests";
    private const string SigningKey = "ai-trader-test-only-signing-key-at-least-32-bytes";
    private static readonly Guid DefaultUserId = Guid.Parse("11111111-2222-4333-8444-555555555555");
    public AiCurrentPositionsFixture Database { get; } = database;
    public HttpClient Client { get; } = client;
    public Clock Time { get; } = clock;
    public DatabaseObserver Observer { get; } = observer;
    public AiCurrentPositionsFixture.Market Market => Database.ValuationMarket;
    public IServiceScope CreateScope() => application.Services.CreateScope();

    public static async Task<AiTraderApiFixture> CreateAsync(Dictionary<string, string?>? settings = null,
        string environment = "Development")
    {
        var database = await AiCurrentPositionsFixture.CreateAsync();
        await using (var db = database.CreateDbContext())
        {
            db.Users.Add(new User { Id = DefaultUserId, DisplayName = "Test authenticated reader", Email = "reader@example.test",
                NormalizedEmail = "READER@EXAMPLE.TEST", PasswordHash = "test-only-not-a-login-hash",
                CreatedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime, UpdatedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime });
            await db.SaveChangesAsync();
        }
        var clock = new Clock();
        var observer = new DatabaseObserver();
        database.ValuationMarket.Quote = (symbol, _) => new StockQuote(symbol, "USD",
            database.ValuationMarket.Prices[symbol], null, null, null, AiCurrentPositionsFixture.Now, Exchange: "NASDAQ");
        var factory = database.With(observer);
        var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                // Operator catalogs/User Secrets must never merge into this isolated HTTP fixture.
                configuration.Sources.Clear();
                var values = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:StockLab"] = "Server=localhost;Database=AiTraderApiTestsNeverUsed;Integrated Security=true;TrustServerCertificate=true",
                    ["Jwt:Issuer"] = Issuer,
                    ["Jwt:Audience"] = Audience,
                    ["Jwt:SigningKey"] = SigningKey,
                    ["MarketData:Provider"] = "Mock",
                    ["AiTraderMetadata:RegistryPath"] = "",
                    ["AiTraderMetadata:BacktestReportPaths"] = "",
                    ["AiTraderMetadata:BenchmarkComparisonPaths"] = ""
                };
                if (settings is not null)
                    foreach (var entry in settings) values[entry.Key] = entry.Value;
                configuration.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<StockLabDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<StockLabDbContext>();
                services.RemoveAll<IDbContextFactory<StockLabDbContext>>();
                services.AddSingleton(factory);
                services.AddScoped(_ => factory.CreateDbContext());
                services.RemoveAll<IMarketDataProvider>();
                services.AddSingleton<IMarketDataProvider>(database.ValuationMarket);
                services.RemoveAllKeyed<IMarketDataProvider>("Execution");
                services.AddKeyedSingleton<IMarketDataProvider>("Execution", database.ValuationMarket);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                // JWT lifetime validation uses wall-clock time; only trading/captures use the fixed test clock.
                services.RemoveAll<IAccessTokenService>();
                services.AddSingleton<IAccessTokenService>(provider => new JwtAccessTokenService(
                    provider.GetRequiredService<IOptions<JwtOptions>>(), TimeProvider.System));
            });
        });
        try
        {
            return new(database, application, application.CreateClient(), clock, observer);
        }
        catch
        {
            await application.DisposeAsync();
            await database.DisposeAsync();
            throw;
        }
    }

    public async Task<(Guid Id, string Token)> RegisterAsync(string email = "ai-trader-api@example.com")
    {
        using var registration = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            displayName = "AI Trader API Test User", email, password = "correct horse battery staple"
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var registered = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        using var login = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            email, password = "correct horse battery staple"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var signedIn = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return (registered.RootElement.GetProperty("id").GetGuid(),
            signedIn.RootElement.GetProperty("accessToken").GetString()!);
    }

    public static string Token(Guid? userId = null, bool expired = false, bool invalidSignature = false)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(Issuer, Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, (userId ?? DefaultUserId).ToString()), new Claim("session_version", "0")],
            notBefore: now.AddHours(-2), expires: expired ? now.AddHours(-1) : now.AddMinutes(30),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                invalidSignature ? "another-test-only-key-at-least-32-bytes" : SigningKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<HttpResponseMessage> GetAsync(string path, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }

    public async Task<JsonElement> ReadAsync(string path, string token)
    {
        using var response = await GetAsync(path, token);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {(int)response.StatusCode} {content}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    public async Task<AiTraderPortfolioState> InitializeAsync()
    {
        using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAiTraderPortfolioService>().GetOrCreateAsync();
    }

    public async Task<AiDecisionRecord> RecordAsync(AiTradingSignal signal = AiTradingSignal.Buy,
        string symbol = "AAPL", decimal confidence = .9m, string modelVersion = "test-original-v1")
    {
        using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAiDecisionHistoryService>().RecordAsync(new(
            Guid.NewGuid(), symbol, signal, confidence, new(2026, 10, 2), "test-only-model", modelVersion));
    }

    public async Task<AiTradeExecutionResult> ExecuteAsync(AiTradingSignal signal = AiTradingSignal.Buy,
        decimal quantity = 10m, decimal price = 100m, string symbol = "AAPL")
    {
        Market.Prices[symbol] = price;
        var decision = await RecordAsync(signal, symbol);
        using var scope = CreateScope();
        var risk = await scope.ServiceProvider.GetRequiredService<IAiRiskManager>().EvaluateAsync(new(
            symbol, signal, decision.Confidence, price));
        Assert.True(risk.Approved);
        Assert.True(risk.ApprovedQuantity >= quantity);
        var result = await scope.ServiceProvider.GetRequiredService<IAiTradeExecutionService>().ExecuteAsync(new(
            Guid.NewGuid(), decision.Id, risk with { ApprovedQuantity = quantity }));
        Time.Advance();
        return result;
    }

    public async Task<AiTraderEquityObservation> CaptureAsync()
    {
        using var scope = CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAiTraderValuationHistoryService>()
            .CaptureCurrentAsync(Guid.NewGuid());
        Time.Advance();
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await application.DisposeAsync();
        await Database.DisposeAsync();
    }

    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = AiCurrentPositionsFixture.Now;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance() => Now = Now.AddMinutes(1);
    }

    internal sealed class DatabaseObserver : DbCommandInterceptor, ISaveChangesInterceptor
    {
        public List<string> Commands { get; } = [];
        public int SaveCalls { get; private set; }
        public void Reset() { Commands.Clear(); SaveCalls = 0; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
        public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            SaveCalls++;
            return result;
        }
        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return ValueTask.FromResult(result);
        }
    }
}
