using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiDecisionHistoryTests
{
    [Theory]
    [InlineData(AiTradingSignal.Buy, "BUY")]
    [InlineData(AiTradingSignal.Sell, "SELL")]
    [InlineData(AiTradingSignal.Hold, "HOLD")]
    public async Task Every_valid_signal_is_persisted_without_a_portfolio(AiTradingSignal signal, string storedSignal)
    {
        await using var f = await Fixture.CreateAsync();
        var request = Request() with { Signal = signal };
        var result = await f.Service().RecordAsync(request);
        await using var db = f.CreateDbContext();
        var row = await db.AiDecisions.SingleAsync();
        Assert.Equal(request.DecisionId, row.Id);
        Assert.Equal("AAPL", row.Symbol);
        Assert.Equal(storedSignal, row.Signal);
        Assert.Equal(0.82m, row.Confidence);
        Assert.Equal(new DateOnly(2026, 10, 2), row.DecisionDate);
        Assert.Equal("random_forest_baseline", row.ModelName);
        Assert.Equal("test-version-001", row.ModelVersion);
        Assert.Equal(Fixture.Now.UtcDateTime, row.RecordedAtUtc);
        Assert.Equal(DateTimeKind.Utc, row.RecordedAtUtc.Kind);
        Assert.Equal(request.DecisionId, result.Id);
        Assert.Equal(signal, result.Signal);
        Assert.Equal(Fixture.Now.UtcDateTime, result.RecordedAtUtc);
        Assert.Empty(await db.AiTraderPortfolios.ToListAsync());
        Assert.Empty(await db.AiTraderPositions.ToListAsync());
        Assert.Empty(await db.AiTrades.ToListAsync());
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task Retry_returns_original_record_and_timestamp_after_canonical_trimming()
    {
        await using var f = await Fixture.CreateAsync();
        var request = Request() with { Symbol = "aapl:NASDAQ", ModelName = " Future_Model ", ModelVersion = " Version-AbC " };
        var first = await f.Service().RecordAsync(request);
        f.Clock.Now = Fixture.Now.AddDays(1);
        var retry = await f.Service().RecordAsync(request with { ModelName = "Future_Model", ModelVersion = "Version-AbC" });
        Assert.Equal(first, retry);
        Assert.Equal("aapl:NASDAQ", first.Symbol);
        Assert.Equal("Future_Model", first.ModelName);
        Assert.Equal("Version-AbC", first.ModelVersion);
        Assert.Equal(Fixture.Now.UtcDateTime, retry.RecordedAtUtc);
        await using var db = f.CreateDbContext();
        Assert.Equal(1, await db.AiDecisions.CountAsync());
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("signal")]
    [InlineData("confidence")]
    [InlineData("date")]
    [InlineData("model")]
    [InlineData("version")]
    [InlineData("model-case")]
    [InlineData("version-case")]
    [InlineData("symbol-case")]
    public async Task Same_id_with_changed_payload_conflicts_without_overwriting(string field)
    {
        await using var f = await Fixture.CreateAsync();
        var request = Request();
        var original = await f.Service().RecordAsync(request);
        var changed = field switch
        {
            "symbol" => request with { Symbol = "MSFT" },
            "signal" => request with { Signal = AiTradingSignal.Sell },
            "confidence" => request with { Confidence = 0.81m },
            "date" => request with { DecisionDate = new(2026, 10, 1) },
            "model" => request with { ModelName = "new-model" },
            "version" => request with { ModelVersion = "new-version" },
            "model-case" => request with { ModelName = "RANDOM_FOREST_BASELINE" },
            "version-case" => request with { ModelVersion = "TEST-VERSION-001" },
            _ => request with { Symbol = "aapl" }
        };
        var error = await Assert.ThrowsAsync<AiDecisionHistoryException>(() => f.Service().RecordAsync(changed));
        Assert.Equal(AiDecisionHistoryFailure.DecisionConflict, error.Category);
        Assert.Null(error.InnerException);
        Assert.Equal(original, await f.Service().GetByIdAsync(request.DecisionId));
        await using var db = f.CreateDbContext();
        Assert.Equal(1, await db.AiDecisions.CountAsync());
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        var r = Request();
        yield return [r with { DecisionId = Guid.Empty }];
        foreach (var symbol in new[] { null, "", " ", " AAPL", "AAPL ", "AAPL,MSFT", "AA PL", "AA\nPL", "AA\0PL", new string('x', 33) })
            yield return [r with { Symbol = symbol! }];
        yield return [r with { Signal = (AiTradingSignal)99 }];
        yield return [r with { Confidence = -0.01m }];
        yield return [r with { Confidence = 1.01m }];
        yield return [r with { DecisionDate = default }];
        yield return [r with { DecisionDate = new(2026, 10, 4) }];
        foreach (var text in new[] { null, "", " ", new string('x', 129) })
        {
            yield return [r with { ModelName = text! }];
            yield return [r with { ModelVersion = text! }];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_input_is_rejected_before_opening_a_database(AiDecisionRecordRequest request)
    {
        var service = new AiDecisionHistoryService(new UnavailableFactory(), new Clock());
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.RecordAsync(request));
    }

    [Fact]
    public async Task Null_request_is_rejected_before_opening_a_database()
    {
        var service = new AiDecisionHistoryService(new UnavailableFactory(), new Clock());
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RecordAsync(null!));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("0.12345678")]
    [InlineData("0.123456789")]
    [InlineData("0.820000000")]
    public async Task Representable_confidence_and_current_utc_date_are_preserved(string confidence)
    {
        await using var f = await Fixture.CreateAsync();
        var value = decimal.Parse(confidence, System.Globalization.CultureInfo.InvariantCulture);
        var request = Request() with { Confidence = value, DecisionDate = new(2026, 10, 3) };
        await f.Service().RecordAsync(request);
        Assert.Equal(value, (await f.Service().GetByIdAsync(request.DecisionId))!.Confidence);
    }

    [Fact]
    public async Task Maximum_length_identity_and_symbol_are_supported()
    {
        await using var f = await Fixture.CreateAsync();
        var request = Request() with { Symbol = new('A', 32), ModelName = new('M', 128), ModelVersion = new('V', 128) };
        await f.Service().RecordAsync(request);
        var saved = await f.Service().GetByIdAsync(request.DecisionId);
        Assert.Equal(request.Symbol, saved!.Symbol);
        Assert.Equal(request.ModelName, saved.ModelName);
        Assert.Equal(request.ModelVersion, saved.ModelVersion);
    }

    [Fact]
    public async Task Reads_return_detached_records_and_missing_id_returns_null()
    {
        await using var f = await Fixture.CreateAsync();
        var saved = await f.Service().RecordAsync(Request());
        var read = await f.Service().GetByIdAsync(saved.Id);
        Assert.Equal(saved, read);
        var callerCopy = read! with { ModelVersion = "caller-change", Signal = AiTradingSignal.Sell };
        Assert.NotEqual(read, callerCopy);
        Assert.Equal(saved, await f.Service().GetByIdAsync(saved.Id));
        Assert.Null(await f.Service().GetByIdAsync(Guid.NewGuid()));
        Assert.Equal(["GetByIdAsync", "GetRecentAsync", "RecordAsync"],
            typeof(StockLab.Application.Interfaces.IAiDecisionHistoryService).GetMethods().Select(m => m.Name).Order().ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    public async Task Invalid_history_limits_are_rejected_before_database_access(int limit)
    {
        var service = new AiDecisionHistoryService(new UnavailableFactory(), new Clock());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetRecentAsync(limit));
    }

    [Fact]
    public async Task Empty_id_lookup_is_rejected_before_database_access()
    {
        var service = new AiDecisionHistoryService(new UnavailableFactory(), new Clock());
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetByIdAsync(Guid.Empty));
    }

    [Fact]
    public async Task Cancellation_propagates_without_writes_or_reads()
    {
        var service = new AiDecisionHistoryService(new UnavailableFactory(), new Clock());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RecordAsync(Request(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetByIdAsync(Guid.NewGuid(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetRecentAsync(10, cancellation.Token));
    }

    [Fact]
    public async Task Nonduplicate_database_failure_returns_safe_error_and_does_not_insert()
    {
        await using var f = await Fixture.CreateAsync();
        var error = await Assert.ThrowsAsync<AiDecisionHistoryException>(() =>
            f.Service(new RejectSave()).RecordAsync(Request()));
        Assert.Equal(AiDecisionHistoryFailure.PersistenceFailure, error.Category);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private-storage-marker", error.ToString());
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiDecisions.ToListAsync());
    }

    [Theory]
    [InlineData("record")]
    [InlineData("id")]
    [InlineData("recent")]
    public async Task Database_read_failures_do_not_expose_sql_details(string operation)
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = f.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("DROP TABLE AiDecisions");
        var service = f.Service();
        var error = await Assert.ThrowsAsync<AiDecisionHistoryException>(async () =>
        {
            if (operation == "record") await service.RecordAsync(Request());
            else if (operation == "id") await service.GetByIdAsync(Guid.NewGuid());
            else await service.GetRecentAsync(10);
        });
        Assert.Equal(AiDecisionHistoryFailure.PersistenceFailure, error.Category);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("SQLite", error.ToString());
    }

    [Fact]
    public async Task Recording_and_reading_history_preserve_existing_user_and_ai_data_and_pending_edits()
    {
        await using var f = await Fixture.CreateAsync();
        await using var db = f.CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Original", Email = "history@example.com",
            NormalizedEmail = "HISTORY@EXAMPLE.COM", PasswordHash = "test-hash", CreatedAtUtc = Fixture.Now.UtcDateTime,
            UpdatedAtUtc = Fixture.Now.UtcDateTime, Version = new byte[8] };
        var personal = new Portfolio { Id = Guid.NewGuid(), UserId = user.Id, Version = new byte[8], CreatedAtUtc = Fixture.Now.UtcDateTime };
        var ai = new AiTraderPortfolio { Id = Guid.NewGuid(), CashBalance = 99000m,
            CreatedAtUtc = Fixture.Now.UtcDateTime, UpdatedAtUtc = Fixture.Now.UtcDateTime };
        db.Users.Add(user);
        db.Portfolios.Add(personal);
        db.Holdings.Add(new() { Id = Guid.NewGuid(), PortfolioId = personal.Id, Symbol = "MSFT", Quantity = 1m, AverageCost = 100m });
        db.Transactions.Add(new() { Id = Guid.NewGuid(), PortfolioId = personal.Id, OrderId = Guid.NewGuid(),
            Side = "BUY", Symbol = "MSFT", Quantity = 1m, ExecutionPrice = 100m, TotalAmount = 100m, ExecutedAtUtc = Fixture.Now.UtcDateTime });
        db.Watchlists.Add(new() { Id = Guid.NewGuid(), UserId = user.Id, Symbol = "MSFT", CreatedAtUtc = Fixture.Now.UtcDateTime });
        db.PriceAlerts.Add(new() { Id = Guid.NewGuid(), UserId = user.Id, Symbol = "MSFT", TargetPrice = 100m,
            Condition = "Above", CreatedAtUtc = Fixture.Now.UtcDateTime, UpdatedAtUtc = Fixture.Now.UtcDateTime, Version = new byte[8] });
        db.AiTraderPortfolios.Add(ai);
        db.AiTraderPositions.Add(new() { Id = Guid.NewGuid(), AiTraderPortfolioId = ai.Id, Symbol = "AAPL",
            Quantity = 10m, AverageCost = 100m, CreatedAtUtc = Fixture.Now.UtcDateTime, UpdatedAtUtc = Fixture.Now.UtcDateTime });
        db.AiTrades.Add(new() { Id = Guid.NewGuid(), AiTraderPortfolioId = ai.Id, OrderId = Guid.NewGuid(),
            OrderFingerprint = new('A', 64), Side = "BUY", Symbol = "AAPL", Quantity = 10m,
            ExecutionPrice = 100m, TotalAmount = 1000m, CashBalanceAfter = 99000m, PositionQuantityAfter = 10m,
            AverageCostAfter = 100m, ExecutedAtUtc = Fixture.Now.UtcDateTime });
        await db.SaveChangesAsync();
        async Task<string> State() => JsonSerializer.Serialize(new
        {
            Users = await db.Users.AsNoTracking().ToArrayAsync(), Portfolios = await db.Portfolios.AsNoTracking().ToArrayAsync(),
            Holdings = await db.Holdings.AsNoTracking().ToArrayAsync(), Transactions = await db.Transactions.AsNoTracking().ToArrayAsync(),
            Watchlists = await db.Watchlists.AsNoTracking().ToArrayAsync(), Alerts = await db.PriceAlerts.AsNoTracking().ToArrayAsync(),
            AiPortfolios = await db.AiTraderPortfolios.AsNoTracking().ToArrayAsync(),
            AiPositions = await db.AiTraderPositions.AsNoTracking().ToArrayAsync(), AiTrades = await db.AiTrades.AsNoTracking().ToArrayAsync()
        });
        var before = await State();
        user.DisplayName = "Unrelated pending edit";
        foreach (var signal in new[] { AiTradingSignal.Buy, AiTradingSignal.Sell, AiTradingSignal.Hold })
        {
            var saved = await f.Service().RecordAsync(Request() with { Signal = signal });
            await f.Service().GetByIdAsync(saved.Id);
        }
        Assert.Equal(3, (await f.Service().GetRecentAsync(10)).Count);
        Assert.Equal(before, await State());
        Assert.Equal(EntityState.Modified, db.Entry(user).State);
        Assert.Equal(3, await db.AiDecisions.CountAsync());
    }

    internal static AiDecisionRecordRequest Request() => new(Guid.NewGuid(), "AAPL", AiTradingSignal.Buy, 0.82m,
        new(2026, 10, 2), "random_forest_baseline", "test-version-001");

    private sealed class UnavailableFactory : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => throw new InvalidOperationException("Database must not be opened");
    }
    private sealed class RejectSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("private-storage-marker");
    }
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = Fixture.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Fixture(SqliteConnection connection) : IAsyncDisposable
    {
        internal static readonly DateTimeOffset Now = new(2026, 10, 3, 20, 0, 0, TimeSpan.Zero);
        public Clock Clock { get; } = new();
        public StockLabDbContext CreateDbContext() => new SqliteContext(Options());
        private DbContextOptions<StockLabDbContext> Options(IInterceptor? interceptor = null)
        {
            var builder = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite(connection);
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            return builder.Options;
        }
        public AiDecisionHistoryService Service(IInterceptor? interceptor = null) => new(new SqliteFactory(Options(interceptor)), Clock);
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var f = new Fixture(connection);
            await using var db = f.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            return f;
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
    private sealed class SqliteContext(DbContextOptions<StockLabDbContext> options) : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<AiTraderPortfolio>().Property(p => p.Version).HasDefaultValueSql("randomblob(8)");
            modelBuilder.Entity<User>().Property(p => p.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(p => p.Version).ValueGeneratedNever();
            modelBuilder.Entity<PriceAlert>().Property(p => p.Version).ValueGeneratedNever();
        }
    }
    private sealed class SqliteFactory(DbContextOptions<StockLabDbContext> options) : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => new SqliteContext(options);
    }
}
