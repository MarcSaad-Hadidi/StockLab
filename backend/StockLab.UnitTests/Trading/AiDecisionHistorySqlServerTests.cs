using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;
using static StockLab.UnitTests.Trading.AiDecisionHistoryTests;

namespace StockLab.UnitTests.Trading;

public sealed class AiDecisionHistorySqlServerTests
{
    [LocalDbFact]
    public async Task Concurrent_identical_inserts_reload_the_pk_winner_and_return_one_original_record()
    {
        await using var f = await SqlFixture.CreateAsync();
        var request = Request();
        var gate = new InsertGate(4);
        var factory = f.With(gate);
        var tasks = Enumerable.Range(0, 4).Select(i => new AiDecisionHistoryService(factory,
            new Clock { Now = f.Now.AddSeconds(i) }).RecordAsync(request with
            { ModelName = i % 2 == 0 ? " random_forest_baseline " : request.ModelName }));
        var records = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.All(records, r => Assert.Equal(records[0], r));
        Assert.Equal(3, gate.DuplicateFailures);
        await using var db = f.CreateDbContext();
        var row = await db.AiDecisions.SingleAsync();
        Assert.Equal(request.DecisionId, row.Id);
        Assert.Equal(records[0].RecordedAtUtc, row.RecordedAtUtc);
        Assert.Equal(DateTimeKind.Utc, row.RecordedAtUtc.Kind);
        Assert.Empty(await db.AiTraderPortfolios.ToListAsync());
        Assert.Empty(await db.AiTrades.ToListAsync());
    }

    [LocalDbFact]
    public async Task Concurrent_conflicting_payload_has_one_winner_and_one_typed_conflict()
    {
        await using var f = await SqlFixture.CreateAsync();
        var first = Request();
        var second = first with { ModelVersion = "TEST-VERSION-001", Signal = AiTradingSignal.Sell, Confidence = 0.81m };
        var gate = new InsertGate(2);
        var factory = f.With(gate);
        async Task<object> RecordAsync(AiDecisionRecordRequest request)
        {
            try { return await new AiDecisionHistoryService(factory, new Clock()).RecordAsync(request); }
            catch (AiDecisionHistoryException error) { return error; }
        }
        var results = await Task.WhenAll(RecordAsync(first), RecordAsync(second)).WaitAsync(TimeSpan.FromSeconds(60));
        var success = Assert.Single(results.OfType<AiDecisionRecord>());
        var failure = Assert.Single(results.OfType<AiDecisionHistoryException>());
        Assert.Equal(AiDecisionHistoryFailure.DecisionConflict, failure.Category);
        Assert.Equal(1, gate.DuplicateFailures);
        var stored = await f.Service().GetByIdAsync(first.DecisionId);
        Assert.Equal(success, stored);
        Assert.Equal(success, await f.Service().RecordAsync(success.Signal == AiTradingSignal.Buy ? first : second));
        await using var db = f.CreateDbContext();
        Assert.Equal(1, await db.AiDecisions.CountAsync());
    }

    [LocalDbFact]
    public async Task Recent_history_orders_and_limits_in_sql_and_returns_detached_records()
    {
        await using var f = await SqlFixture.CreateAsync();
        Assert.Empty(await f.Service().GetRecentAsync(200));
        foreach (var (number, day, minute) in new[] { (1, 1, 10), (2, 2, 1), (3, 2, 2), (4, 2, 2), (5, 3, 0) })
        {
            var request = Request() with { DecisionId = Id(number), DecisionDate = new(2026, 10, day) };
            await new AiDecisionHistoryService(f, new Clock { Now = f.Now.AddMinutes(minute) }).RecordAsync(request);
        }
        var observer = new QueryObserver();
        var service = new AiDecisionHistoryService(f.With(observer), new Clock());
        var recent = await service.GetRecentAsync(3);
        Assert.Equal([Id(5), Id(4), Id(3)], recent.Select(r => r.Id));
        Assert.Contains("TOP(", observer.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", observer.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(3, observer.ParameterValues);
        Assert.Equal(0, observer.TrackedCount);
        var all = await service.GetRecentAsync(200);
        Assert.Equal([Id(5), Id(4), Id(3), Id(2), Id(1)], all.Select(r => r.Id));
        Assert.Equal(DateTimeKind.Utc, all[0].RecordedAtUtc.Kind);
        Assert.Equal(0, observer.TrackedCount);
        Assert.Equal(recent[0], await service.GetByIdAsync(Id(5)));
        Assert.Equal(0, observer.TrackedCount);
        Assert.Null(await service.GetByIdAsync(Guid.NewGuid()));
    }

    [LocalDbTheory]
    [InlineData("0.12345678")]
    [InlineData("0.1234567890123456789012345678")]
    [InlineData("0")]
    [InlineData("1")]
    public async Task Sql_precision_round_trip_remains_idempotent_with_exact_confidence_and_opaque_version(string confidence)
    {
        await using var f = await SqlFixture.CreateAsync();
        var value = decimal.Parse(confidence, System.Globalization.CultureInfo.InvariantCulture);
        var request = Request() with { Confidence = value, ModelName = " Future_Model ", ModelVersion = " release-ABC/42 " };
        var first = await f.Service().RecordAsync(request);
        var retry = await f.Service().RecordAsync(request with { ModelName = "Future_Model", ModelVersion = "release-ABC/42" });
        Assert.Equal(first, retry);
        Assert.Equal(value, retry.Confidence);
        Assert.Equal("release-ABC/42", retry.ModelVersion);
        var changed = request with { Confidence = value == 1m ? value - 0.0000000000000000000000000001m : value + 0.0000000000000000000000000001m };
        var error = await Assert.ThrowsAsync<AiDecisionHistoryException>(() => f.Service().RecordAsync(changed));
        Assert.Equal(AiDecisionHistoryFailure.DecisionConflict, error.Category);
        Assert.Equal(first, await f.Service().GetByIdAsync(request.DecisionId));
    }

    [LocalDbTheory]
    [InlineData("confidence-low")]
    [InlineData("confidence-high")]
    [InlineData("signal")]
    [InlineData("signal-lower")]
    [InlineData("signal-padded")]
    [InlineData("model")]
    [InlineData("version")]
    [InlineData("symbol")]
    public async Task Database_constraints_reject_invalid_direct_inserts(string invalid)
    {
        await using var f = await SqlFixture.CreateAsync();
        await using var db = f.CreateDbContext();
        db.AiDecisions.Add(new()
        {
            Id = Guid.NewGuid(), Symbol = invalid == "symbol" ? "   " : "AAPL",
            Signal = invalid switch { "signal" => "WAIT", "signal-lower" => "buy", "signal-padded" => "BUY ", _ => "HOLD" },
            Confidence = invalid == "confidence-low" ? -0.01m : invalid == "confidence-high" ? 1.01m : 0m,
            DecisionDate = new(2026, 10, 2), ModelName = invalid == "model" ? "   " : "future-model",
            ModelVersion = invalid == "version" ? "   " : "exact-version", RecordedAtUtc = f.Now.UtcDateTime
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        db.ChangeTracker.Clear();
        Assert.Empty(await db.AiDecisions.ToListAsync());
    }

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}");

    private sealed class InsertGate(int participants) : SaveChangesInterceptor
    {
        private int arrivals;
        private int duplicateFailures;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DuplicateFailures => duplicateFailures;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrivals) == participants) ready.SetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Exception is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } })
                Interlocked.Increment(ref duplicateFailures);
            return Task.CompletedTask;
        }
    }
    private sealed class QueryObserver : DbCommandInterceptor
    {
        public string Sql { get; private set; } = string.Empty;
        public object?[] ParameterValues { get; private set; } = [];
        public int TrackedCount { get; set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            Sql = command.CommandText;
            ParameterValues = command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray();
            return ValueTask.FromResult(result);
        }
    }
    private sealed class ContextFactory(DbContextOptions<StockLabDbContext> options, QueryObserver? observer = null) : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => new ObservedContext(options, observer);
    }
    private sealed class ObservedContext(DbContextOptions<StockLabDbContext> options, QueryObserver? observer) : StockLabDbContext(options)
    {
        public override ValueTask DisposeAsync()
        {
            // Observe after query materialization, so accidentally removing AsNoTracking fails the test.
            if (observer is not null) observer.TrackedCount = ChangeTracker.Entries<AiDecision>().Count();
            return base.DisposeAsync();
        }
    }
    private sealed class SqlFixture(string connection) : IDbContextFactory<StockLabDbContext>, IAsyncDisposable
    {
        public DateTimeOffset Now { get; } = new(2026, 10, 3, 20, 0, 0, TimeSpan.Zero);
        public AiDecisionHistoryService Service() => new(this, new Clock());
        public StockLabDbContext CreateDbContext() => new(new DbContextOptionsBuilder<StockLabDbContext>().UseSqlServer(connection).Options);
        public IDbContextFactory<StockLabDbContext> With(IInterceptor interceptor) => new ContextFactory(
            new DbContextOptionsBuilder<StockLabDbContext>().UseSqlServer(connection).AddInterceptors(interceptor).Options,
            interceptor as QueryObserver);
        public static async Task<SqlFixture> CreateAsync()
        {
            // Isolated local test database only; never reads application or Azure credentials.
            var f = new SqlFixture($"Server=(localdb)\\MSSQLLocalDB;Database=StockLabDecisionTests_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true");
            await using var db = f.CreateDbContext();
            await db.Database.MigrateAsync();
            return f;
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = CreateDbContext();
            await db.Database.EnsureDeletedAsync();
        }
    }
    private sealed class LocalDbFactAttribute : FactAttribute
    {
        public LocalDbFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
    private sealed class LocalDbTheoryAttribute : TheoryAttribute
    {
        public LocalDbTheoryAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
}
