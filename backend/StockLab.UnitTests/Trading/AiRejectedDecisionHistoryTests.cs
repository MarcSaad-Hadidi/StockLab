using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiRejectedDecisionHistoryTests
{
    public static IEnumerable<object[]> Reasons() => Enum.GetValues<AiRiskRejectionReason>().Select(r => new object[] { r });

    [Theory]
    [MemberData(nameof(Reasons))]
    public async Task Every_existing_reason_is_stored_and_enriched_without_reevaluating_risk(AiRiskRejectionReason reason)
    {
        await using var f = await Fixture.CreateAsync();
        var raw = Raw() with { Signal = reason == AiRiskRejectionReason.HoldSignal ? AiTradingSignal.Hold : AiTradingSignal.Buy };
        await f.RawService().RecordAsync(raw);
        // History accepts the supplied outcome even when a policy would currently choose another reason.
        var request = Request(raw, reason) with { RiskDecision = Risk(raw, reason) with { RequestedPrice = -1m } };
        var result = await f.Service().RecordAsync(request);
        Assert.Equal(new AiRejectedDecisionRecord(raw.DecisionId, raw.Symbol, raw.Signal, raw.Confidence,
            raw.DecisionDate, raw.ModelName, raw.ModelVersion, reason, Fixture.Now.UtcDateTime), result);
        await using var db = f.CreateDbContext();
        var row = await db.AiRejectedDecisions.SingleAsync();
        Assert.Equal(raw.DecisionId, row.AiDecisionId);
        Assert.Equal(reason.ToString(), row.RejectionReason);
        Assert.Equal(DateTimeKind.Utc, row.RejectedAtUtc.Kind);
        Assert.Empty(await db.AiTraderPortfolios.ToListAsync());
        Assert.Empty(await db.AiTraderPositions.ToListAsync());
        Assert.Empty(await db.AiTrades.ToListAsync());
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Theory]
    [InlineData("AAPL", AiTradingSignal.Buy, "0.65", AiRiskRejectionReason.LowConfidence)]
    [InlineData("NVDA", AiTradingSignal.Buy, "0.85", AiRiskRejectionReason.MaxPositionsReached)]
    [InlineData("TSLA", AiTradingSignal.Sell, "0.90", AiRiskRejectionReason.NoPositionToSell)]
    [InlineData("MSFT", AiTradingSignal.Hold, "1", AiRiskRejectionReason.HoldSignal)]
    public async Task Required_examples_link_the_original_model_identity(string symbol, AiTradingSignal signal,
        string confidence, AiRiskRejectionReason reason)
    {
        await using var f = await Fixture.CreateAsync();
        var raw = Raw() with { Symbol = symbol, Signal = signal,
            Confidence = decimal.Parse(confidence, System.Globalization.CultureInfo.InvariantCulture) };
        await f.RawService().RecordAsync(raw);
        var saved = await f.Service().RecordAsync(Request(raw, reason));
        Assert.Equal(symbol, saved.Symbol);
        Assert.Equal(signal, saved.Signal);
        Assert.Equal(raw.Confidence, saved.Confidence);
        Assert.Equal(raw.ModelName, saved.ModelName);
        Assert.Equal(raw.ModelVersion, saved.ModelVersion);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        var r = Request(Raw());
        yield return [null!];
        yield return [r with { DecisionId = Guid.Empty }];
        yield return [r with { RiskDecision = null! }];
        yield return [r with { RiskDecision = r.RiskDecision with { Approved = true } }];
        yield return [r with { RiskDecision = r.RiskDecision with { RejectionReason = null } }];
        yield return [r with { RiskDecision = r.RiskDecision with { RejectionReason = (AiRiskRejectionReason)99 } }];
        foreach (var quantity in new[] { 1m, -1m, 0.0000000000000000000000000001m })
            yield return [r with { RiskDecision = r.RiskDecision with { ApprovedQuantity = quantity } }];
        yield return [r with { RiskDecision = r.RiskDecision with { Signal = (AiTradingSignal)99 } }];
        foreach (var confidence in new[] { -0.01m, 1.01m })
            yield return [r with { RiskDecision = r.RiskDecision with { Confidence = confidence } }];
        foreach (var symbol in new[] { null, "", " ", " AAPL", "AAPL ", "AA PL", "AAPL,MSFT", "AA\nPL", "AA\0PL", new string('A', 33) })
            yield return [r with { RiskDecision = r.RiskDecision with { Symbol = symbol! } }];
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_input_fails_before_opening_a_database(AiRejectedDecisionRecordRequest request)
    {
        var service = new AiRejectedDecisionHistoryService(new UnavailableFactory(), new Clock());
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.RecordAsync(request));
    }

    [Fact]
    public async Task Missing_decision_is_typed_and_is_never_created()
    {
        await using var f = await Fixture.CreateAsync();
        var error = await Assert.ThrowsAsync<AiRejectedDecisionHistoryException>(() => f.Service().RecordAsync(Request(Raw())));
        Assert.Equal(AiRejectedDecisionHistoryFailure.DecisionNotFound, error.Category);
        Assert.Null(error.InnerException);
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiDecisions.ToListAsync());
        Assert.Empty(await db.AiRejectedDecisions.ToListAsync());
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("symbol-case")]
    [InlineData("signal")]
    [InlineData("confidence")]
    public async Task Mismatch_is_rejected_even_on_a_retry_without_modifying_history(string field)
    {
        await using var f = await Fixture.CreateAsync();
        var raw = Raw();
        await f.RawService().RecordAsync(raw);
        var request = Request(raw);
        var risk = field switch
        {
            "symbol" => request.RiskDecision with { Symbol = "MSFT" },
            "symbol-case" => request.RiskDecision with { Symbol = "aapl" },
            "signal" => request.RiskDecision with { Signal = AiTradingSignal.Sell },
            _ => request.RiskDecision with { Confidence = 0.81m }
        };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Service().RecordAsync(request with { RiskDecision = risk }));
        var original = await f.Service().RecordAsync(request);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Service().RecordAsync(request with { RiskDecision = risk }));
        Assert.Equal(original, await f.Service().GetByDecisionIdAsync(raw.DecisionId));
        await using var db = f.CreateDbContext();
        Assert.Equal(1, await db.AiRejectedDecisions.CountAsync());
    }

    [Fact]
    public async Task Retry_preserves_original_timestamp_and_reason_conflict_does_not_overwrite()
    {
        await using var f = await Fixture.CreateAsync();
        var raw = Raw() with { Symbol = "aapl:NASDAQ", ModelVersion = "release-ABC/42" };
        await f.RawService().RecordAsync(raw);
        var request = Request(raw);
        var first = await f.Service().RecordAsync(request);
        f.Clock.Now = Fixture.Now.AddDays(1);
        Assert.Equal(first, await f.Service().RecordAsync(request with { RiskDecision = request.RiskDecision with { RequestedPrice = 999m } }));
        var error = await Assert.ThrowsAsync<AiRejectedDecisionHistoryException>(() =>
            f.Service().RecordAsync(Request(raw, AiRiskRejectionReason.InsufficientCash)));
        Assert.Equal(AiRejectedDecisionHistoryFailure.RejectionConflict, error.Category);
        Assert.Equal(first, await f.Service().GetByDecisionIdAsync(raw.DecisionId));
        await using var db = f.CreateDbContext();
        Assert.Equal(1, await db.AiRejectedDecisions.CountAsync());
    }

    [Fact]
    public async Task Reads_are_detached_and_unrejected_or_unknown_decisions_return_null()
    {
        await using var f = await Fixture.CreateAsync();
        Assert.Empty(await f.Service().GetRecentAsync(200));
        var raw = Raw();
        await f.RawService().RecordAsync(raw);
        Assert.Null(await f.Service().GetByDecisionIdAsync(raw.DecisionId));
        Assert.Null(await f.Service().GetByDecisionIdAsync(Guid.NewGuid()));
        var saved = await f.Service().RecordAsync(Request(raw));
        var copy = saved with { RejectionReason = AiRiskRejectionReason.TradeTooSmall, ModelVersion = "caller-change" };
        Assert.NotEqual(saved, copy);
        Assert.Equal(saved, await f.Service().GetByDecisionIdAsync(raw.DecisionId));
        Assert.Equal(saved, Assert.Single(await f.Service().GetRecentAsync(1)));
        Assert.Equal(["GetByDecisionIdAsync", "GetRecentAsync", "RecordAsync"],
            typeof(IAiRejectedDecisionHistoryService).GetMethods().Select(m => m.Name).Order());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(201)]
    public async Task Invalid_limit_fails_before_database_access(int limit) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new AiRejectedDecisionHistoryService(new UnavailableFactory(), new Clock()).GetRecentAsync(limit));

    [Fact]
    public async Task Empty_lookup_id_fails_before_database_access() =>
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new AiRejectedDecisionHistoryService(new UnavailableFactory(), new Clock()).GetByDecisionIdAsync(Guid.Empty));

    [Fact]
    public async Task Cancellation_propagates_before_database_access()
    {
        var service = new AiRejectedDecisionHistoryService(new UnavailableFactory(), new Clock());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RecordAsync(Request(Raw()), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetByDecisionIdAsync(Guid.NewGuid(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetRecentAsync(1, cts.Token));
    }

    [Fact]
    public async Task Nonduplicate_save_failure_is_safe_and_does_not_insert()
    {
        await using var f = await Fixture.CreateAsync();
        var raw = Raw();
        await f.RawService().RecordAsync(raw);
        var error = await Assert.ThrowsAsync<AiRejectedDecisionHistoryException>(() => f.Service(new RejectSave()).RecordAsync(Request(raw)));
        Assert.Equal(AiRejectedDecisionHistoryFailure.PersistenceFailure, error.Category);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private-storage-marker", error.ToString());
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiRejectedDecisions.ToListAsync());
        Assert.Equal(1, await db.AiDecisions.CountAsync());
    }

    [Theory]
    [InlineData("record")]
    [InlineData("id")]
    [InlineData("recent")]
    public async Task Read_failures_are_safe_without_sql_details(string operation)
    {
        await using var f = await Fixture.CreateAsync();
        var raw = Raw();
        await f.RawService().RecordAsync(raw);
        await using (var db = f.CreateDbContext()) await db.Database.ExecuteSqlRawAsync("DROP TABLE AiRejectedDecisions");
        var error = await Assert.ThrowsAsync<AiRejectedDecisionHistoryException>(async () =>
        {
            if (operation == "record") await f.Service().RecordAsync(Request(raw));
            else if (operation == "id") await f.Service().GetByDecisionIdAsync(raw.DecisionId);
            else await f.Service().GetRecentAsync(1);
        });
        Assert.Equal(AiRejectedDecisionHistoryFailure.PersistenceFailure, error.Category);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("SQLite", error.ToString());
    }

    [Fact]
    public async Task Recording_reading_retry_and_conflict_preserve_all_existing_data_and_pending_edits()
    {
        await using var f = await Fixture.CreateAsync();
        await using var db = f.CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Original", Email = "rejection@example.com",
            NormalizedEmail = "REJECTION@EXAMPLE.COM", PasswordHash = "test-hash", CreatedAtUtc = Fixture.Now.UtcDateTime,
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
        var raw = Raw();
        await f.RawService().RecordAsync(raw);
        async Task<string> State() => JsonSerializer.Serialize(new
        {
            Users = await db.Users.AsNoTracking().ToArrayAsync(), Portfolios = await db.Portfolios.AsNoTracking().ToArrayAsync(),
            Holdings = await db.Holdings.AsNoTracking().ToArrayAsync(), Transactions = await db.Transactions.AsNoTracking().ToArrayAsync(),
            Watchlists = await db.Watchlists.AsNoTracking().ToArrayAsync(), Alerts = await db.PriceAlerts.AsNoTracking().ToArrayAsync(),
            AiPortfolios = await db.AiTraderPortfolios.AsNoTracking().ToArrayAsync(),
            AiPositions = await db.AiTraderPositions.AsNoTracking().ToArrayAsync(), AiTrades = await db.AiTrades.AsNoTracking().ToArrayAsync(),
            AiDecisions = await db.AiDecisions.AsNoTracking().ToArrayAsync()
        });
        var before = await State();
        user.DisplayName = "Unrelated pending edit";
        var request = Request(raw);
        var saved = await f.Service().RecordAsync(request);
        Assert.Equal(saved, await f.Service().RecordAsync(request));
        Assert.Equal(saved, await f.Service().GetByDecisionIdAsync(raw.DecisionId));
        Assert.Equal(saved, Assert.Single(await f.Service().GetRecentAsync(200)));
        await Assert.ThrowsAsync<AiRejectedDecisionHistoryException>(() => f.Service().RecordAsync(Request(raw, AiRiskRejectionReason.TradeTooSmall)));
        Assert.Equal(before, await State());
        Assert.Equal(EntityState.Modified, db.Entry(user).State);
        Assert.Equal(1, await db.AiRejectedDecisions.CountAsync());
    }

    internal static AiDecisionRecordRequest Raw() => AiDecisionHistoryTests.Request();
    internal static AiRiskDecision Risk(AiDecisionRecordRequest raw, AiRiskRejectionReason reason) =>
        new(false, raw.Symbol, raw.Signal, raw.Confidence, 100m, 0m, reason);
    internal static AiRejectedDecisionRecordRequest Request(AiDecisionRecordRequest raw, AiRiskRejectionReason reason = AiRiskRejectionReason.LowConfidence) =>
        new(raw.DecisionId, Risk(raw, reason));

    private sealed class UnavailableFactory : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => throw new InvalidOperationException("Database must not be opened");
    }
    private sealed class RejectSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw new DbUpdateException("private-storage-marker");
    }
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = Fixture.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Fixture(SqliteConnection connection) : IDbContextFactory<StockLabDbContext>, IAsyncDisposable
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
        public AiDecisionHistoryService RawService() => new(this, Clock);
        public AiRejectedDecisionHistoryService Service(IInterceptor? interceptor = null) => new(new SqliteFactory(Options(interceptor)), Clock);
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
