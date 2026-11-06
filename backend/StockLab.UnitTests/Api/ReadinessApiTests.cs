using System.Collections.Concurrent;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Api;

public sealed class ReadinessApiTests
{
    internal const string SigningKey = "test-only-readiness-signing-key-at-least-32-bytes";
    private const string PrivateMarker = "private-readiness-password-marker";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Probes_remain_anonymous_and_liveness_never_resolves_SQL_even_under_a_fallback_policy(bool fallback)
    {
        var resolutions = 0;
        using var app = CreateApp(services =>
        {
            ReplaceOptions(services, _ => { Interlocked.Increment(ref resolutions); throw new InvalidOperationException(PrivateMarker); });
            if (fallback) services.Configure<AuthorizationOptions>(options =>
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        });
        using var client = Client(app);
        foreach (var bearer in new[] { false, true })
        {
            if (bearer) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ValidOptionalToken());
            var before = resolutions;
            using var live = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());
            Assert.Equal(before, resolutions);
            using var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            Assert.Equal("Unhealthy", await ready.Content.ReadAsStringAsync());
            Assert.True(resolutions > before);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-connection-string-private-readiness-password-marker")]
    public async Task Missing_or_malformed_SQL_configuration_is_not_ready_and_never_leaks_details(string? connection)
    {
        var logs = new ProbeLogs();
        using var app = CreateApp(logs: logs, connection: connection);
        using var client = Client(app);
        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("Unhealthy", await ready.Content.ReadAsStringAsync());
        Assert.Contains("no-store", ready.Headers.CacheControl?.ToString());
        using var live = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.DoesNotContain(PrivateMarker, string.Join('\n', logs.Entries));
    }

    [Fact]
    public async Task Provider_connection_errors_are_sanitized_in_both_the_response_and_logs()
    {
        var probe = new ConnectionProbe(slow: false);
        var logs = new ProbeLogs();
        using var app = CreateApp(services => ReplaceOptions(services, _ => SqlOptions(probe)), logs);
        using var client = Client(app);
        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("Unhealthy", await ready.Content.ReadAsStringAsync());
        Assert.Equal(1, probe.Attempts);
        Assert.DoesNotContain(PrivateMarker, string.Join('\n', logs.Entries));
    }

    [Fact]
    public async Task A_slow_SQL_dependency_times_out_instead_of_hanging_the_probe()
    {
        var probe = new ConnectionProbe(slow: true);
        using var app = CreateApp(services => ReplaceOptions(services, _ => SqlOptions(probe)));
        using var client = Client(app);
        using var response = await client.GetAsync("/health/ready").WaitAsync(TimeSpan.FromSeconds(12));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
        Assert.True(probe.Canceled);
        using var live = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task A_canceled_probe_stops_its_SQL_work()
    {
        var probe = new ConnectionProbe(slow: true);
        using var app = CreateApp(services => ReplaceOptions(services, _ => SqlOptions(probe)));
        using var client = Client(app);
        using var cancellation = new CancellationTokenSource();
        var pending = app.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Tags.Contains("ready"), cancellation.Token);
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(probe.Canceled);
    }

    internal static WebApplicationFactory<Program> CreateApp(Action<IServiceCollection>? configure = null,
        ILoggerProvider? logs = null, string? connection = "Server=localhost;Database=ReadinessTestsNeverUsed;Integrated Security=true;TrustServerCertificate=true") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StockLab"] = connection,
                ["Jwt:Issuer"] = "StockLab.Readiness.Tests", ["Jwt:Audience"] = "StockLab.Readiness.Tests",
                ["Jwt:SigningKey"] = SigningKey, ["MarketData:Provider"] = "Mock",
                ["AiPerformanceCycle:Enabled"] = "false"
            }));
            builder.ConfigureTestServices(services =>
            {
                if (logs is not null) services.AddLogging(logging => logging.AddProvider(logs));
                configure?.Invoke(services);
            });
        });

    internal static HttpClient Client(WebApplicationFactory<Program> app) => app.CreateClient(new() { BaseAddress = new("https://localhost") });

    private static void ReplaceOptions(IServiceCollection services, Func<IServiceProvider, DbContextOptions<StockLabDbContext>> factory)
    {
        services.RemoveAll<DbContextOptions<StockLabDbContext>>();
        services.AddScoped(factory);
    }

    private static DbContextOptions<StockLabDbContext> SqlOptions(ConnectionProbe probe) =>
        new DbContextOptionsBuilder<StockLabDbContext>()
            .UseSqlServer("Server=localhost;Database=ReadinessTestsNeverUsed;Integrated Security=true;TrustServerCertificate=true")
            .AddInterceptors(probe).Options;

    private static string ValidOptionalToken() => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        "StockLab.Readiness.Tests", "StockLab.Readiness.Tests",
        [new Claim("sub", Guid.NewGuid().ToString("D")), new Claim("session_version", "0")],
        DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10),
        new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256)));

    private sealed class ConnectionProbe(bool slow) : DbConnectionInterceptor
    {
        public int Attempts;
        public bool Canceled;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Attempts);
            Started.TrySetResult();
            if (slow)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) { Canceled = true; throw; }
            }
            throw new InvalidOperationException(PrivateMarker);
        }
    }

    private sealed class ProbeLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new ProbeLogger(Entries);
        public void Dispose() { }
        private sealed class ProbeLogger(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(formatter(state, exception) + exception);
        }
    }
}
