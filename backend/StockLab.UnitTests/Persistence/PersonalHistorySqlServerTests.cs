using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.MarketData;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Portfolios;
using StockLab.UnitTests.Api;

namespace StockLab.UnitTests.Persistence;

[Trait("Category", "SqlServer")]
public sealed class PersonalHistorySqlServerTests
{
    private const string PreviousMigration = "20261031205259_AddUserSessionVersion";

    [LocalDbFact]
    public async Task Migration_adds_an_empty_history_and_empty_rollback_preserves_existing_account_amounts()
    {
        await using var fixture = new Fixture();
        await using var db = fixture.Context();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var user = await fixture.SeedAsync(holding: true);
        var cash = (await db.Portfolios.AsNoTracking().SingleAsync()).CashBalance;
        await db.Database.MigrateAsync();
        Assert.Empty(await db.PortfolioValuationSnapshots.ToArrayAsync());
        Assert.Equal(cash, (await db.Portfolios.AsNoTracking().SingleAsync()).CashBalance);
        Assert.Equal(0.5m, (await db.Holdings.AsNoTracking().SingleAsync()).Quantity);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        Assert.Equal(cash, (await db.Portfolios.AsNoTracking().SingleAsync()).CashBalance);
        Assert.Equal(user, (await db.Users.AsNoTracking().SingleAsync()).Id);
    }

    [LocalDbTheory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task Concurrent_captures_preserve_one_original_and_existing_history_cannot_be_edited_or_rolled_back(int captureCount)
    {
        await using var fixture = new Fixture();
        await fixture.MigrateAsync();
        var user = await fixture.SeedAsync(holding: true);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var quotes = new Quotes(async symbol =>
        {
            if (Interlocked.Increment(ref calls) == captureCount) arrived.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return fixture.Quote(symbol);
        });
        async Task<PortfolioValuationSnapshot?> Capture()
        {
            await using var context = fixture.Context();
            return await fixture.Capture(context, quotes).CaptureAsync(user, default);
        }
        var captures = Enumerable.Range(0, captureCount).Select(_ => Capture()).ToArray();
        PortfolioValuationSnapshot?[] captured;
        try { await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
        finally
        {
            release.TrySetResult();
            captured = await Task.WhenAll(captures);
        }
        Assert.All(captured, point =>
        {
            Assert.NotNull(point);
            Assert.Equal(captured[0]!.Id, point.Id);
        });
        Assert.Equal(100000.05555m, captured[0]!.TotalValue);
        await using var db = fixture.Context();
        var stored = await db.PortfolioValuationSnapshots.SingleAsync();
        Assert.Equal("Mock", stored.Source);
        Assert.Contains("MSFT", stored.QuoteObservationsJson);
        Assert.Equal(99999.38275m, (await db.Portfolios.SingleAsync()).CashBalance);
        Assert.Empty(await db.Transactions.ToArrayAsync());
        var unavailable = new Quotes(_ => throw new InvalidOperationException("provider offline"));
        Assert.Equal(stored.Id, (await fixture.Capture(db, unavailable).CaptureAsync(user, default))!.Id);
        stored.TotalValue = 0m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.PortfolioValuationSnapshots.Remove(await db.PortfolioValuationSnapshots.SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync(PreviousMigration));
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), name => name.EndsWith("_AddPersonalPortfolioPerformanceHistory", StringComparison.Ordinal));
        Assert.Equal(100000.05555m, (await db.PortfolioValuationSnapshots.SingleAsync()).TotalValue);
    }

    [LocalDbFact]
    public async Task A_trade_committing_during_quotes_or_invalid_quotes_cannot_create_a_partial_observation()
    {
        await using var fixture = new Fixture();
        await fixture.MigrateAsync();
        var user = await fixture.SeedAsync(holding: true);
        await using var db = fixture.Context();
        var racing = new Quotes(async symbol =>
        {
            await using var trade = fixture.Context();
            var portfolio = await trade.Portfolios.SingleAsync(row => row.UserId == user);
            portfolio.CashBalance -= 1m;
            await trade.SaveChangesAsync(); // SQL Server generates a new real rowversion.
            return fixture.Quote(symbol);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Capture(db, racing).CaptureAsync(user, default));
        Assert.Empty(await db.PortfolioValuationSnapshots.ToArrayAsync());
        foreach (var bad in new Quotes[] { new(_ => Task.FromResult<StockQuote?>(null)),
            new(symbol => Task.FromResult<StockQuote?>(fixture.Quote(symbol) with { Currency = "CAD" })),
            new(_ => throw new InvalidOperationException("provider offline")) })
        {
            await Assert.ThrowsAnyAsync<Exception>(() => fixture.Capture(db, bad).CaptureAsync(user, default));
            Assert.Empty(await db.PortfolioValuationSnapshots.ToArrayAsync());
        }
        var good = new Quotes(symbol => Task.FromResult<StockQuote?>(fixture.Quote(symbol)));
        var point = await fixture.Capture(db, good).CaptureAsync(user, default);
        Assert.Equal(99999.05555m, point!.TotalValue);
        Assert.Equal(8, point.PortfolioVersion.Length);
    }

    [LocalDbFact]
    public async Task Cycles_page_accounts_isolate_bad_quotes_and_do_not_generate_production_mock_holdings()
    {
        await using var fixture = new Fixture();
        await fixture.MigrateAsync();
        var badUser = await fixture.SeedAsync(holding: true);
        for (var index = 0; index < 51; index++) await fixture.SeedAsync(holding: false);
        await using var db = fixture.Context();
        var missing = new Quotes(_ => Task.FromResult<StockQuote?>(null));
        await fixture.Capture(db, missing).RunOnceAsync(default);
        Assert.Equal(51, await db.PortfolioValuationSnapshots.CountAsync());
        Assert.All(await db.PortfolioValuationSnapshots.ToArrayAsync(), point => Assert.Equal("Cash", point.Source));
        var calls = 0;
        var quotes = new Quotes(symbol => { calls++; return Task.FromResult<StockQuote?>(fixture.Quote(symbol)); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Capture(db, quotes, production: true).CaptureAsync(badUser, default));
        Assert.Equal(0, calls);
    }

    [LocalDbFact]
    public async Task SQL_history_downsampling_reads_actual_buckets_and_preserves_fractional_amounts()
    {
        await using var fixture = new Fixture();
        await fixture.MigrateAsync();
        var user = await fixture.SeedAsync(holding: false);
        await using var db = fixture.Context();
        var id = (await db.Portfolios.SingleAsync()).Id;
        var points = Enumerable.Range(0, 601).Select(index => new PortfolioValuationSnapshot
        {
            Id = Guid.NewGuid(), PortfolioId = id, SlotUtc = fixture.Clock.Now.UtcDateTime.AddMinutes(index - 600),
            RecordedAtUtc = fixture.Clock.Now.UtcDateTime.AddMinutes(index - 600), CashBalance = 99999.999999999999m,
            TotalValue = 99999.999999999999m, InitialCapital = 100000m, Currency = "USD", Source = "Cash"
        });
        db.PortfolioValuationSnapshots.AddRange(points);
        await db.SaveChangesAsync();
        var history = await new PortfolioPerformanceHistoryService(db, fixture.Clock).GetAsync(user, "ALL", default);
        Assert.Equal(601, history!.SampleCount);
        Assert.InRange(history.Points.Count, 2, 500);
        Assert.Equal("hour", history.Resolution);
        Assert.All(history.Points, point => Assert.Equal(99999.999999999999m, point.TotalValue));
        Assert.Equal(fixture.Clock.Now.UtcDateTime, history.Points[^1].RecordedAtUtc);
    }

    [LocalDbFact]
    public async Task History_amounts_round_trip_the_claimed_derived_capacity_without_SQL_decimal_reader_overflow()
    {
        await using var fixture = new Fixture();
        await fixture.MigrateAsync();
        var user = await fixture.SeedAsync(holding: true);
        await using var db = fixture.Context();
        var portfolio = await db.Portfolios.SingleAsync();
        const decimal wide = 12345678901234567890123456.123m;
        db.PortfolioValuationSnapshots.Add(new PortfolioValuationSnapshot
        {
            Id = Guid.NewGuid(), PortfolioId = portfolio.Id, SlotUtc = fixture.Clock.Now.UtcDateTime,
            RecordedAtUtc = fixture.Clock.Now.UtcDateTime, CashBalance = 0m, PositionsValue = wide,
            TotalValue = wide, InitialCapital = 100000m, Currency = "USD", Source = "Mock", PortfolioVersion = portfolio.Version
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var history = await new PortfolioPerformanceHistoryService(db, fixture.Clock).GetAsync(user, "ALL", default);
        Assert.Equal(wide, Assert.Single(history!.Points).TotalValue);
    }

    [LocalDbTheory]
    [InlineData("product")]
    [InlineData("sum")]
    [InlineData("cash")]
    public async Task Captures_reject_immutable_values_already_rounded_by_decimal_arithmetic(string scenario)
    {
        await using var fixture = new Fixture();
        await fixture.MigrateAsync();
        var user = await fixture.SeedAsync(holding: true);
        await using var db = fixture.Context();
        var portfolio = await db.Portfolios.Include(row => row.Holdings).SingleAsync();
        var large = portfolio.Holdings.Single();
        large.Symbol = "LARGE";
        large.Quantity = scenario == "product" ? 99999999999.99999999m : 1m;
        large.AverageCost = scenario == "product" ? 0.0001m : 1m;
        portfolio.CashBalance = scenario == "cash" ? 99999.999999999999m : 0m;
        if (scenario == "sum") db.Holdings.Add(new Holding { Id = Guid.NewGuid(), PortfolioId = portfolio.Id, Symbol = "TINY",
            Quantity = 0.00000001m, AverageCost = 0.0001m, UpdatedAtUtc = fixture.Clock.Now.UtcDateTime });
        await db.SaveChangesAsync();
        var quotes = new Quotes(symbol => Task.FromResult<StockQuote?>(fixture.Quote(symbol) with
        {
            Price = symbol == "TINY" ? 0.0001m : scenario == "product" ? 100000000000000.0001m : 10000000000000000000000000m
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Capture(db, quotes).CaptureAsync(user, default));
        Assert.Empty(await db.PortfolioValuationSnapshots.ToArrayAsync());
    }

    private sealed class LocalDbTheoryAttribute : TheoryAttribute
    {
        public LocalDbTheoryAttribute()
        {
            if (!OperatingSystem.IsWindows() || System.Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string name = $"StockLabPersonalHistoryTests_{Guid.NewGuid():N}";
        public PersonalHistoryClock Clock { get; } = new(DateTimeOffset.UtcNow);
        private DbContextOptions<StockLabDbContext> Options => new DbContextOptionsBuilder<StockLabDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={name};Integrated Security=true;TrustServerCertificate=true").Options;
        public StockLabDbContext Context() => new(Options);
        public async Task MigrateAsync() { await using var db = Context(); await db.Database.MigrateAsync(); }
        public async Task<Guid> SeedAsync(bool holding)
        {
            await using var db = Context();
            var id = Guid.NewGuid();
            var email = $"history-{id:N}@example.com";
            var user = new User { Id = id, DisplayName = "SQL history account", Email = email, NormalizedEmail = email.ToUpperInvariant(),
                PasswordHash = "test-only-not-a-login-hash", CreatedAtUtc = Clock.Now.UtcDateTime, UpdatedAtUtc = Clock.Now.UtcDateTime };
            var portfolio = new Portfolio { Id = Guid.NewGuid(), User = user, UserId = id,
                CashBalance = holding ? 99999.38275m : 99999.999999999999m, CreatedAtUtc = Clock.Now.UtcDateTime };
            if (holding) portfolio.Holdings.Add(new Holding { Id = Guid.NewGuid(), Symbol = "MSFT", Quantity = 0.5m,
                AverageCost = 1.2345m, UpdatedAtUtc = Clock.Now.UtcDateTime });
            db.Portfolios.Add(portfolio);
            await db.SaveChangesAsync();
            return id;
        }
        public StockQuote Quote(string symbol) => new(symbol, "USD", 1.3456m, null, null, null, Clock.Now.AddMinutes(-1));
        public PortfolioPerformanceCaptureService Capture(StockLabDbContext valuationDb, IMarketDataProvider quotes, bool production = false) =>
            new(new Factory(Options), new PortfolioService(valuationDb, quotes, Microsoft.Extensions.Options.Options.Create(new MarketDataRateLimitOptions())),
                Microsoft.Extensions.Options.Options.Create(new PortfolioPerformanceCaptureOptions()), Clock,
                new PortfolioPerformanceSource("Mock"),
                new EnvironmentStub { EnvironmentName = production ? Environments.Production : Environments.Development },
                NullLogger<PortfolioPerformanceCaptureService>.Instance);
        public async ValueTask DisposeAsync() { await using var db = Context(); await db.Database.EnsureDeletedAsync(); }
    }
    private sealed class Factory(DbContextOptions<StockLabDbContext> options) : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => new(options);
    }
    private sealed class Quotes(Func<string, Task<StockQuote?>> get) : IMarketDataProvider
    {
        public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default) => get(symbol);
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "StockLab.History.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class LocalDbFactAttribute : FactAttribute
    {
        public LocalDbFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || System.Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
}
