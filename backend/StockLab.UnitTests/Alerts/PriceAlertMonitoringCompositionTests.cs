using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Alerts;
using StockLab.Infrastructure.MarketData;
using StockLab.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace StockLab.UnitTests.Alerts;

public sealed class PriceAlertMonitoringCompositionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(200, 2, 3, 0)]
    [InlineData(1, 1, 2, 1)]
    public async Task Api_runs_periodic_monitoring_with_configured_mock_pipeline_and_three_alerts(
        int dailyBudget, int expectedQuotes, int expectedMatches, int expectedDeferred)
    {
        await using var database = await MonitoringDatabase.CreateAsync();
        await database.SeedAsync(database.Alert("AAPL", "Above", 1m), database.Alert("AAPL", "Below", 1000m),
            database.Alert("MSFT", "Above", 1m));
        var clock = new MonitoringClock();
        using var logs = new MonitoringLogs();
        await using var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StockLab"] = "Server=localhost;Database=MonitoringSmokeNeverUsed;Integrated Security=true;TrustServerCertificate=true",
                ["Jwt:SigningKey"] = "MONITORING-TEST-ONLY-SIGNING-KEY-AT-LEAST-32",
                ["MarketData:Provider"] = "Mock",
                ["PriceAlertMonitoring:Interval"] = "00:00:17",
                ["PriceAlertMonitoring:DailyQuoteBudget"] = dailyBudget.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<StockLabDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<StockLabDbContext>();
                services.AddSingleton(database.Options);
                services.AddScoped<StockLabDbContext, MonitoringDatabase.SqliteMonitoringDbContext>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.AddLogging(logging => logging.AddProvider(logs));
            });
        });
        using var client = application.CreateClient();
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<CachingMarketDataProvider>(application.Services.GetRequiredService<IMarketDataProvider>());
        Assert.IsType<MockMarketDataProvider>(application.Services.GetRequiredKeyedService<IMarketDataProvider>("Terminal"));

        clock.Advance(TimeSpan.FromSeconds(17));
        var summary = await logs.Summaries.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        output.WriteLine(summary.Message);
        Assert.Equal(3, summary.Fields["ActiveAlertCount"]);
        Assert.Equal(2, summary.Fields["DistinctSymbolCount"]);
        Assert.Equal(expectedQuotes, summary.Fields["QuoteCount"]);
        Assert.Equal(expectedMatches, summary.Fields["MatchCount"]);
        Assert.Equal(0, summary.Fields["FailedQuoteCount"]);
        Assert.Equal(expectedDeferred, summary.Fields["DeferredSymbolCount"]);
        Assert.Single(logs.Entries);
        if (dailyBudget == 1)
        {
            clock.Advance(TimeSpan.FromSeconds(17));
            var nextSummary = await logs.Summaries.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, nextSummary.Fields["QuoteCount"]);
            Assert.Equal(2, nextSummary.Fields["DeferredSymbolCount"]);
            Assert.Equal(0, nextSummary.Fields["FailedQuoteCount"]);
        }
        await using var db = database.CreateDbContext();
        var alerts = await db.PriceAlerts.AsNoTracking().ToArrayAsync();
        Assert.All(alerts, alert =>
        {
            Assert.Equal("Active", alert.Status);
            Assert.Null(alert.TriggeredPrice);
            Assert.Null(alert.TriggeredAtUtc);
        });
    }
}

internal sealed class MonitoringLogs : ILoggerProvider
{
    public sealed record Entry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Fields, Exception? Exception);
    public ConcurrentQueue<Entry> Entries { get; } = new();
    public Channel<Entry> Summaries { get; } = Channel.CreateUnbounded<Entry>();
    public ILogger CreateLogger(string categoryName) => new CollectorLogger(this, categoryName);
    public void Dispose() { }

    private sealed class CollectorLogger(MonitoringLogs logs, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!category.StartsWith("StockLab.Infrastructure.Alerts.PriceAlertMonitoring", StringComparison.Ordinal)) return;
            var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value) : new Dictionary<string, object?>();
            var entry = new Entry(logLevel, formatter(state, exception), fields, exception);
            logs.Entries.Enqueue(entry);
            if (logLevel == LogLevel.Information) logs.Summaries.Writer.TryWrite(entry);
        }
    }
}
