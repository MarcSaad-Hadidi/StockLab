using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Alerts;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Alerts;

public sealed class PriceAlertMonitoringServiceTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("exception")]
    [InlineData("currency_mismatch")]
    public async Task Warning_symbols_cannot_forge_multiline_logs(string scenario)
    {
        const string symbol = "AA\nPL\rFORGED\t\u001b\u0085\u2028\u2029";
        await using var f = await MonitoringDatabase.CreateAsync();
        await f.SeedAsync(f.Alert(symbol, "Above", 1m));
        var provider = new QuoteProvider((requested, _) => scenario switch
        {
            "null" => Task.FromResult<StockQuote?>(null),
            "exception" => Task.FromException<StockQuote?>(new InvalidOperationException("private upstream detail")),
            _ => Task.FromResult<StockQuote?>(Quote(requested, 250m) with { Currency = "CAD" })
        });
        using var logs = new MonitoringLogs();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var budget = new PriceAlertMonitoringBudget(Options.Create(new PriceAlertMonitoringOptions()), new MonitoringClock());
        var service = new PriceAlertMonitoringService(f.Db, provider, budget, factory.CreateLogger<PriceAlertMonitoringService>());

        await service.RunOnceAsync();

        Assert.Equal(new[] { symbol }, provider.Calls);
        var warning = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal("AA?PL?FORGED?????", warning.Fields["Symbol"]);
        Assert.DoesNotContain(warning.Message, character => char.IsControl(character) || character is '\u2028' or '\u2029');
        Assert.Null(warning.Exception);
        Assert.DoesNotContain("private upstream detail", warning.Message);
    }

    [Fact]
    public async Task Daily_budget_is_shared_across_users_symbols_cycles_and_service_scopes()
    {
        await using var f = await MonitoringDatabase.CreateAsync();
        await f.SeedAsync(f.Alert("AAPL", "Above", 1m), f.Alert("AAPL", "Below", 1000m),
            f.Alert("MSFT", "Above", 1m), f.Alert("NVDA", "Above", 1m));
        var clock = new MonitoringClock();
        var budget = new PriceAlertMonitoringBudget(Options.Create(new PriceAlertMonitoringOptions { DailyQuoteBudget = 2 }), clock);
        var provider = new QuoteProvider((symbol, _) => Task.FromResult<StockQuote?>(Quote(symbol, 250m)));

        var first = await Service(f, provider, budget).RunOnceAsync();
        await using var fresh = f.CreateDbContext();
        var second = await new PriceAlertMonitoringService(fresh, provider, budget, NullLogger<PriceAlertMonitoringService>.Instance).RunOnceAsync();

        Assert.Equal(new[] { "AAPL", "MSFT" }, provider.Calls);
        Assert.Equal(4, first.ActiveAlertCount);
        Assert.Equal(3, first.DistinctSymbolCount);
        Assert.Equal(2, first.QuoteCount);
        Assert.Equal(0, first.FailedQuoteCount);
        Assert.Equal(1, first.DeferredSymbolCount);
        Assert.Equal(3, first.Matches.Count);
        Assert.Equal(0, second.QuoteCount);
        Assert.Equal(0, second.FailedQuoteCount);
        Assert.Equal(3, second.DeferredSymbolCount);
        Assert.Empty(second.Matches);
    }

    [Fact]
    public async Task Default_budget_caps_a_full_day_of_minute_cycles()
    {
        await using var f = await MonitoringDatabase.CreateAsync();
        await f.SeedAsync(f.Alert("AAPL", "Above", 1m));
        var clock = new MonitoringClock();
        clock.Advance(TimeSpan.FromHours(-12));
        var budget = new PriceAlertMonitoringBudget(Options.Create(new PriceAlertMonitoringOptions()), clock);
        var provider = new QuoteProvider((symbol, _) => Task.FromResult<StockQuote?>(Quote(symbol, 250m)));

        for (var minute = 0; minute < 1440; minute++)
        {
            await Service(f, provider, budget).RunOnceAsync();
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(200, provider.Calls.Count);
    }

    [Fact]
    public async Task Next_utc_day_resumes_after_the_last_attempted_symbol()
    {
        await using var f = await MonitoringDatabase.CreateAsync();
        await f.SeedAsync(f.Alert("AAPL", "Above", 1m), f.Alert("MSFT", "Above", 1m), f.Alert("NVDA", "Above", 1m));
        var clock = new MonitoringClock();
        var budget = new PriceAlertMonitoringBudget(Options.Create(new PriceAlertMonitoringOptions { DailyQuoteBudget = 2 }), clock);
        var provider = new QuoteProvider((symbol, _) => Task.FromResult<StockQuote?>(Quote(symbol, 250m)));
        await Service(f, provider, budget).RunOnceAsync();
        clock.Advance(TimeSpan.FromHours(12) - TimeSpan.FromTicks(1));
        var beforeMidnight = await Service(f, provider, budget).RunOnceAsync();
        Assert.Equal(3, beforeMidnight.DeferredSymbolCount);

        clock.Advance(TimeSpan.FromTicks(1));
        var nextDay = await Service(f, provider, budget).RunOnceAsync();

        Assert.Equal(new[] { "AAPL", "MSFT", "NVDA", "AAPL" }, provider.Calls);
        Assert.Equal(2, nextDay.QuoteCount);
        Assert.Equal(1, nextDay.DeferredSymbolCount);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("exception")]
    public async Task Missing_and_failed_quotes_consume_budget_without_retries(string failure)
    {
        await using var f = await MonitoringDatabase.CreateAsync();
        await f.SeedAsync(f.Alert("AAPL", "Above", 1m));
        var budget = new PriceAlertMonitoringBudget(Options.Create(new PriceAlertMonitoringOptions { DailyQuoteBudget = 1 }), new MonitoringClock());
        var provider = new QuoteProvider((_, _) => failure == "null" ? Task.FromResult<StockQuote?>(null)
            : Task.FromException<StockQuote?>(new InvalidOperationException("private upstream detail")));

        var first = await Service(f, provider, budget).RunOnceAsync();
        var second = await Service(f, provider, budget).RunOnceAsync();

        Assert.Equal(new[] { "AAPL" }, provider.Calls);
        Assert.Equal(1, first.FailedQuoteCount);
        Assert.Equal(0, second.FailedQuoteCount);
        Assert.Equal(1, second.DeferredSymbolCount);
    }

    [Fact]
    public async Task Groups_all_users_and_conditions_into_one_lookup_per_symbol()
    {
        await using var f = await MonitoringDatabase.CreateAsync();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var first = f.Alert("AAPL", "Above", 250m, userA);
        var second = f.Alert("AAPL", "Below", 180m, userA);
        var third = f.Alert("AAPL", "Above", 300m, userB);
        var fourth = f.Alert("MSFT", "Below", 400m);
        await f.SeedAsync(first, second, third, fourth);
        var provider = new QuoteProvider((symbol, _) => Task.FromResult<StockQuote?>(Quote(symbol, symbol == "AAPL" ? 260m : 399m)));

        var result = await Service(f, provider).RunOnceAsync();

        Assert.Equal(new[] { "AAPL", "MSFT" }, provider.Calls.Order());
        Assert.Equal(4, result.ActiveAlertCount);
        Assert.Equal(2, result.DistinctSymbolCount);
        Assert.Equal(2, result.QuoteCount);
        Assert.Equal(0, result.FailedQuoteCount);
        Assert.Equal(new[] { first.Id, fourth.Id }.Order(), result.Matches.Select(m => m.AlertId).Order());
    }

    [Theory]
    [InlineData("Above", "201", true)]
    [InlineData("Above", "200", false)]
    [InlineData("Above", "199", false)]
    [InlineData("Below", "199", true)]
    [InlineData("Below", "200", false)]
    [InlineData("Below", "201", false)]
    public async Task Conditions_are_strict(string condition, string observedPrice, bool matches)
    {
        await using var f = await MonitoringDatabase.CreateAsync();
        await f.SeedAsync(f.Alert("AAPL", condition, 200m));
        var provider = new QuoteProvider((symbol, _) => Task.FromResult<StockQuote?>(Quote(symbol, decimal.Parse(observedPrice, CultureInfo.InvariantCulture))));

        var result = await Service(f, provider).RunOnceAsync();

        Assert.Equal(matches ? 1 : 0, result.Matches.Count);
        Assert.Equal(new[] { "AAPL" }, provider.Calls);
    }

    [Fact]
    public async Task Evaluates_every_condition_with_the_same_quote()
    {
        await using var f = await MonitoringDatabase.CreateAsync();
        var above = f.Alert("AAPL", "Above", 200m);
        var below = f.Alert("AAPL", "Below", 300m);
        await f.SeedAsync(above, below, f.Alert("AAPL", "Above", 300m), f.Alert("AAPL", "Below", 200m));
        var provider = new QuoteProvider((symbol, _) => Task.FromResult<StockQuote?>(Quote(symbol, 250m)));

        var result = await Service(f, provider).RunOnceAsync();

        Assert.Equal(new[] { above.Id, below.Id }.Order(), result.Matches.Select(m => m.AlertId).Order());
        Assert.Equal(new[] { "AAPL" }, provider.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Disabled_and_triggered_alerts_never_cause_lookups(bool includeActive)
    {
        await using var f = await MonitoringDatabase.CreateAsync();
        var disabled = f.Alert("MSFT", "Above", 1m);
        disabled.Status = "Disabled";
        var triggered = f.Alert("NVDA", "Above", 1m);
        triggered.Status = "Triggered";
        triggered.TriggeredPrice = 2m;
        triggered.TriggeredAtUtc = triggered.CreatedAtUtc.AddMinutes(1);
        await f.SeedAsync(disabled, triggered);
        if (includeActive) await f.SeedAsync(f.Alert("AAPL", "Above", 1m));
        var provider = new QuoteProvider((symbol, _) => Task.FromResult<StockQuote?>(Quote(symbol, 250m)));

        var result = await Service(f, provider).RunOnceAsync();

        Assert.Equal(includeActive ? new[] { "AAPL" } : [], provider.Calls);
        Assert.Equal(includeActive ? 1 : 0, result.ActiveAlertCount);
        Assert.Equal(includeActive ? 1 : 0, result.DistinctSymbolCount);
        Assert.Equal(includeActive ? 1 : 0, result.QuoteCount);
        Assert.Equal(includeActive ? 1 : 0, result.Matches.Count);
    }

    [Fact]
    public async Task Empty_database_causes_no_market_data_calls()
    {
        await using var f = await MonitoringDatabase.CreateAsync();
        var provider = new QuoteProvider((_, _) => throw new InvalidOperationException("Unexpected lookup"));

        var result = await Service(f, provider).RunOnceAsync();

        Assert.Empty(provider.Calls);
        Assert.Equal(0, result.ActiveAlertCount);
        Assert.Equal(0, result.DistinctSymbolCount);
        Assert.Equal(0, result.QuoteCount);
        Assert.Equal(0, result.FailedQuoteCount);
        Assert.Empty(result.Matches);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("exception")]
    [InlineData("provider_cancellation")]
    public async Task Failed_symbol_does_not_prevent_later_symbols_from_matching(string failure)
    {
        await using var f = await MonitoringDatabase.CreateAsync();
        var msft = f.Alert("MSFT", "Above", 399m);
        await f.SeedAsync(f.Alert("AAPL", "Below", 100m), msft);
        var provider = new QuoteProvider((symbol, _) => symbol == "MSFT"
            ? Task.FromResult<StockQuote?>(Quote(symbol, 400m))
            : failure switch
            {
                "null" => Task.FromResult<StockQuote?>(null),
                "provider_cancellation" => Task.FromException<StockQuote?>(new OperationCanceledException()),
                _ => Task.FromException<StockQuote?>(new InvalidOperationException("private upstream detail"))
            });

        var result = await Service(f, provider).RunOnceAsync();

        Assert.Equal(msft.Id, Assert.Single(result.Matches).AlertId);
        Assert.Equal(new[] { "AAPL", "MSFT" }, provider.Calls);
        Assert.Equal(1, result.QuoteCount);
        Assert.Equal(1, result.FailedQuoteCount);
    }

    [Fact]
    public async Task Currency_mismatch_skips_only_alerts_in_another_currency()
    {
        await using var f = await MonitoringDatabase.CreateAsync();
        var cad = f.Alert("AAPL", "Above", 1m);
        cad.Currency = "CAD";
        await f.SeedAsync(f.Alert("AAPL", "Above", 1m), cad);
        var provider = new QuoteProvider((symbol, _) => Task.FromResult<StockQuote?>(Quote(symbol, 250m) with { Currency = "CAD" }));

        var result = await Service(f, provider).RunOnceAsync();

        Assert.Equal(cad.Id, Assert.Single(result.Matches).AlertId);
        Assert.Equal(new[] { "AAPL" }, provider.Calls);
        Assert.Equal(1, result.QuoteCount);
        Assert.Equal(0, result.FailedQuoteCount);
    }

    [Fact]
    public async Task Match_preserves_identity_decimal_precision_and_quote_observation_time_without_mutation()
    {
        await using var f = await MonitoringDatabase.CreateAsync();
        var alert = f.Alert("AAPL", "Above", 999999999999999.9998m);
        await f.SeedAsync(alert);
        var quote = Quote("AAPL", 999999999999999.9999m);
        var provider = new QuoteProvider((_, _) => Task.FromResult<StockQuote?>(quote));

        var result = await Service(f, provider).RunOnceAsync();

        var match = Assert.Single(result.Matches);
        Assert.Equal(alert.Id, match.AlertId);
        Assert.Equal(alert.UserId, match.UserId);
        Assert.Equal("AAPL", match.Symbol);
        Assert.Equal("USD", match.Currency);
        Assert.Equal("Above", match.Condition);
        Assert.Equal(999999999999999.9998m, match.TargetPrice);
        Assert.Equal(999999999999999.9999m, match.ObservedPrice);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 20, 0, 0, TimeSpan.Zero), match.ObservedAtUtc);
        Assert.Empty(f.Db.ChangeTracker.Entries());
        Assert.Equal(0, await f.Db.SaveChangesAsync());
        await using var fresh = f.CreateDbContext();
        var stored = await fresh.PriceAlerts.AsNoTracking().SingleAsync();
        Assert.Equal("Active", stored.Status);
        Assert.Null(stored.TriggeredPrice);
        Assert.Null(stored.TriggeredAtUtc);
        Assert.Equal(alert.UpdatedAtUtc, stored.UpdatedAtUtc);
        Assert.Equal(alert.Version, stored.Version);
    }

    [Fact]
    public async Task Sql_reads_only_active_alert_projection_without_user_navigation()
    {
        var commands = new ReadCommands();
        await using var f = await MonitoringDatabase.CreateAsync(commands);
        await f.SeedAsync(f.Alert("AAPL", "Above", 1m));
        commands.Text.Clear();
        var provider = new QuoteProvider((symbol, _) => Task.FromResult<StockQuote?>(Quote(symbol, 250m)));

        await Service(f, provider).RunOnceAsync();

        var sql = Assert.Single(commands.Text);
        Assert.Contains("WHERE", sql);
        Assert.Contains("Active", sql);
        Assert.DoesNotContain("JOIN", sql);
        Assert.DoesNotContain("TriggeredPrice", sql);
        Assert.DoesNotContain("TriggeredAtUtc", sql);
        Assert.DoesNotContain("CreatedAtUtc", sql);
        Assert.DoesNotContain("UpdatedAtUtc", sql);
        Assert.DoesNotContain("Version", sql);
        Assert.Empty(f.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Shutdown_cancellation_propagates_and_stops_further_lookups()
    {
        await using var f = await MonitoringDatabase.CreateAsync();
        await f.SeedAsync(f.Alert("AAPL", "Above", 1m), f.Alert("MSFT", "Above", 1m));
        using var cancellation = new CancellationTokenSource();
        var provider = new QuoteProvider((_, ct) =>
        {
            Assert.Equal(cancellation.Token, ct);
            cancellation.Cancel();
            return Task.FromCanceled<StockQuote?>(ct);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(f, provider).RunOnceAsync(cancellation.Token));

        Assert.Equal(new[] { "AAPL" }, provider.Calls);
    }

    private static PriceAlertMonitoringService Service(MonitoringDatabase f, IMarketDataProvider provider, PriceAlertMonitoringBudget? budget = null) =>
        new(f.Db, provider, budget ?? new PriceAlertMonitoringBudget(Options.Create(new PriceAlertMonitoringOptions()), TimeProvider.System),
            NullLogger<PriceAlertMonitoringService>.Instance);

    private static StockQuote Quote(string symbol, decimal price) =>
        new(symbol, "USD", price, null, null, null, new DateTimeOffset(2026, 8, 28, 20, 0, 0, TimeSpan.Zero));

    private sealed class QuoteProvider(Func<string, CancellationToken, Task<StockQuote?>> getQuote) : IMarketDataProvider
    {
        public List<string> Calls { get; } = [];
        public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            Calls.Add(symbol);
            return getQuote(symbol, cancellationToken);
        }
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ReadCommands : DbCommandInterceptor
    {
        public List<string> Text { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Text.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}

internal sealed class MonitoringDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    public DbContextOptions<StockLabDbContext> Options { get; }
    public StockLabDbContext Db { get; }
    private static readonly DateTime CreatedAt = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

    private MonitoringDatabase(SqliteConnection connection, DbContextOptions<StockLabDbContext> options)
    {
        this.connection = connection;
        Options = options;
        Db = CreateDbContext();
    }

    public static async Task<MonitoringDatabase> CreateAsync(params IInterceptor[] interceptors)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite(connection).AddInterceptors(interceptors).Options;
        var database = new MonitoringDatabase(connection, options);
        await database.Db.Database.EnsureCreatedAsync();
        return database;
    }

    public PriceAlert Alert(string symbol, string condition, decimal targetPrice, Guid? userId = null) => new()
    {
        Id = Guid.NewGuid(), UserId = userId ?? Guid.NewGuid(), Symbol = symbol, Condition = condition,
        TargetPrice = targetPrice, Currency = "USD", Status = "Active", CreatedAtUtc = CreatedAt,
        UpdatedAtUtc = CreatedAt, Version = Guid.NewGuid().ToByteArray()
    };

    public async Task SeedAsync(params PriceAlert[] alerts)
    {
        var existing = await Db.Users.Select(u => u.Id).ToArrayAsync();
        foreach (var userId in alerts.Select(a => a.UserId).Distinct().Except(existing))
        {
            Db.Users.Add(new User
            {
                Id = userId, DisplayName = "Monitoring test", Email = $"{userId:N}@example.com",
                NormalizedEmail = $"{userId:N}@EXAMPLE.COM", PasswordHash = "test-only",
                CreatedAtUtc = CreatedAt, UpdatedAtUtc = CreatedAt, Version = []
            });
        }
        Db.PriceAlerts.AddRange(alerts);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public StockLabDbContext CreateDbContext() => new SqliteMonitoringDbContext(Options);
    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await connection.DisposeAsync();
    }

    internal sealed class SqliteMonitoringDbContext(DbContextOptions<StockLabDbContext> options) : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().Property(u => u.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(p => p.Version).ValueGeneratedNever();
            modelBuilder.Entity<PriceAlert>().Property(a => a.Version).ValueGeneratedNever();
            // SQLite NUMERIC affinity rounds high-precision decimals through floating point.
            // Store text in this test database so it preserves SQL Server's decimal contract.
            modelBuilder.Entity<PriceAlert>().Property(a => a.TargetPrice).HasColumnType("TEXT");
            modelBuilder.Entity<PriceAlert>().Property(a => a.TriggeredPrice).HasColumnType("TEXT");
        }
    }
}
