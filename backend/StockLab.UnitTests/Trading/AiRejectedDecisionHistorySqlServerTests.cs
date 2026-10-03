using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;
using static StockLab.UnitTests.Trading.AiRejectedDecisionHistoryTests;

namespace StockLab.UnitTests.Trading;

public sealed class AiRejectedDecisionHistorySqlServerTests
{
    [LocalDbFact]
    public async Task Four_concurrent_same_reason_inserts_reload_one_pk_winner_and_timestamp()
    {
        await using var f = await SqlFixture.CreateAsync();
        var raw = Raw();
        await f.RawService().RecordAsync(raw);
        var gate = new InsertGate(4);
        var factory = f.With(gate);
        var tasks = Enumerable.Range(0, 4).Select(i => new AiRejectedDecisionHistoryService(factory,
            new Clock { Now = f.Now.AddSeconds(i) }).RecordAsync(Request(raw)));
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.All(results, r => Assert.Equal(results[0], r));
        Assert.Equal(3, gate.DuplicateFailures);
        await using var db = f.CreateDbContext();
        var row = await db.AiRejectedDecisions.SingleAsync();
        Assert.Equal(raw.DecisionId, row.AiDecisionId);
        Assert.Equal(results[0].RejectedAtUtc, row.RejectedAtUtc);
        Assert.Equal(DateTimeKind.Utc, row.RejectedAtUtc.Kind);
        Assert.Equal(1, await db.AiDecisions.CountAsync());
        Assert.Empty(await db.AiTrades.ToListAsync());
        Assert.Empty(await db.AiTraderPortfolios.ToListAsync());
    }

    [LocalDbFact]
    public async Task Concurrent_different_reasons_produce_one_record_and_one_typed_conflict()
    {
        await using var f = await SqlFixture.CreateAsync();
        var raw = Raw();
        await f.RawService().RecordAsync(raw);
        var gate = new InsertGate(2);
        var factory = f.With(gate);
        async Task<object> Record(AiRiskRejectionReason reason)
        {
            try { return await new AiRejectedDecisionHistoryService(factory, new Clock()).RecordAsync(Request(raw, reason)); }
            catch (AiRejectedDecisionHistoryException ex) { return ex; }
        }
        var results = await Task.WhenAll(Record(AiRiskRejectionReason.LowConfidence), Record(AiRiskRejectionReason.InsufficientCash))
            .WaitAsync(TimeSpan.FromSeconds(60));
        var winner = Assert.Single(results.OfType<AiRejectedDecisionRecord>());
        Assert.Equal(AiRejectedDecisionHistoryFailure.RejectionConflict, Assert.Single(results.OfType<AiRejectedDecisionHistoryException>()).Category);
        Assert.Equal(1, gate.DuplicateFailures);
        Assert.Equal(winner, await f.Service().GetByDecisionIdAsync(raw.DecisionId));
        Assert.Equal(winner, await f.Service().RecordAsync(Request(raw, winner.RejectionReason)));
        await using var db = f.CreateDbContext();
        Assert.Equal(1, await db.AiRejectedDecisions.CountAsync());
    }

    [LocalDbFact]
    public async Task Recent_history_uses_one_bounded_sql_join_with_all_three_ordering_keys_and_no_tracking()
    {
        await using var f = await SqlFixture.CreateAsync();
        // Rejection timestamp outranks decision date; identical timestamps use date then SQL Guid order.
        foreach (var (number, day, minute) in new[] { (1, 3, 0), (2, 1, 1), (3, 2, 1), (4, 2, 1), (5, 1, 2) })
        {
            var raw = Raw() with { DecisionId = Id(number), DecisionDate = new(2026, 10, day) };
            await f.RawService().RecordAsync(raw);
            await new AiRejectedDecisionHistoryService(f, new Clock { Now = f.Now.AddMinutes(minute) }).RecordAsync(Request(raw));
        }
        await f.RawService().RecordAsync(Raw()); // Unrejected decisions must not leak into history.
        var observer = new QueryObserver();
        var service = new AiRejectedDecisionHistoryService(f.With(observer), new Clock());
        var recent = await service.GetRecentAsync(3);
        Assert.Equal([Id(5), Id(4), Id(3)], recent.Select(r => r.DecisionId));
        Assert.Equal(1, observer.Reads);
        Assert.Contains("INNER JOIN [AiDecisions]", observer.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TOP(", observer.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", observer.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(3, observer.ParameterValues);
        Assert.Equal(0, observer.TrackedCount);
        Assert.Equal([Id(5), Id(4), Id(3), Id(2), Id(1)], (await service.GetRecentAsync(200)).Select(r => r.DecisionId));
        Assert.Equal(2, observer.Reads);
        Assert.Equal(recent[0], await service.GetByDecisionIdAsync(Id(5)));
        Assert.Equal(3, observer.Reads);
        Assert.Equal(0, observer.TrackedCount);
        Assert.Equal(DateTimeKind.Utc, recent[0].RejectedAtUtc.Kind);
    }

    [LocalDbTheory]
    [InlineData("0.1234567890123456789012345678")]
    [InlineData("0.0000000000000000000000000001")]
    [InlineData("0")]
    [InlineData("1")]
    public async Task Confidence_matching_is_exact_to_the_last_decimal_digit_before_insert_and_on_retry(string confidence)
    {
        await using var f = await SqlFixture.CreateAsync();
        var value = decimal.Parse(confidence, System.Globalization.CultureInfo.InvariantCulture);
        var raw = Raw() with { Confidence = value, ModelVersion = "release-ABC/42" };
        await f.RawService().RecordAsync(raw);
        var request = Request(raw);
        var changed = request with { RiskDecision = request.RiskDecision with
            { Confidence = value == 1m ? value - 0.0000000000000000000000000001m : value + 0.0000000000000000000000000001m } };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Service().RecordAsync(changed));
        var first = await f.Service().RecordAsync(request);
        Assert.Equal(value, first.Confidence);
        Assert.Equal(value, (await f.Service().GetByDecisionIdAsync(raw.DecisionId))!.Confidence);
        Assert.Equal(first, await f.Service().RecordAsync(request));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Service().RecordAsync(changed));
        Assert.Equal(first, await f.Service().GetByDecisionIdAsync(raw.DecisionId));
    }

    [LocalDbTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UnknownReason")]
    [InlineData("lowconfidence")]
    [InlineData("LowConfidence ")]
    [InlineData("LowConfidence\t")]
    [InlineData("3")]
    public async Task Database_rejects_noncanonical_reason_codes_even_with_default_collation(string reason)
    {
        await using var f = await SqlFixture.CreateAsync();
        var raw = Raw();
        await f.RawService().RecordAsync(raw);
        await using var db = f.CreateDbContext();
        db.AiRejectedDecisions.Add(new() { AiDecisionId = raw.DecisionId, RejectionReason = reason, RejectedAtUtc = f.Now.UtcDateTime });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(ex.InnerException).Number);
        db.ChangeTracker.Clear();
        Assert.Empty(await db.AiRejectedDecisions.ToListAsync());
    }

    [LocalDbFact]
    public async Task Database_accepts_every_defined_reason_and_enforces_fk_pk_and_no_cascade_delete()
    {
        await using var f = await SqlFixture.CreateAsync();
        await using var db = f.CreateDbContext();
        foreach (var reason in Enum.GetValues<AiRiskRejectionReason>())
        {
            var raw = Raw();
            await f.RawService().RecordAsync(raw);
            db.AiRejectedDecisions.Add(new() { AiDecisionId = raw.DecisionId, RejectionReason = reason.ToString(), RejectedAtUtc = f.Now.UtcDateTime });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var original = await db.AiRejectedDecisions.AsNoTracking().FirstAsync();
        db.AiRejectedDecisions.Add(new() { AiDecisionId = Guid.NewGuid(), RejectionReason = "LowConfidence", RejectedAtUtc = f.Now.UtcDateTime });
        var orphan = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(orphan.InnerException).Number);
        db.ChangeTracker.Clear();
        db.AiRejectedDecisions.Add(new() { AiDecisionId = original.AiDecisionId, RejectionReason = "InvalidPrice", RejectedAtUtc = f.Now.UtcDateTime });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(duplicate.InnerException).Number, new[] { 2601, 2627 });
        db.ChangeTracker.Clear();
        var delete = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AiDecisions WHERE Id = {original.AiDecisionId}"));
        Assert.Equal(547, delete.Number);
        Assert.Equal(10, await db.AiDecisions.CountAsync());
        Assert.Equal(10, await db.AiRejectedDecisions.CountAsync());
        Assert.Equal(original.RejectionReason, (await db.AiRejectedDecisions.AsNoTracking().SingleAsync(r => r.AiDecisionId == original.AiDecisionId)).RejectionReason);
    }

    [LocalDbTheory]
    [InlineData("reason")]
    [InlineData("timestamp")]
    public async Task Database_requires_reason_and_timestamp(string missing)
    {
        await using var f = await SqlFixture.CreateAsync();
        var raw = Raw();
        await f.RawService().RecordAsync(raw);
        await using var db = f.CreateDbContext();
        var ex = await Assert.ThrowsAsync<SqlException>(async () =>
        {
            if (missing == "reason")
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AiRejectedDecisions (AiDecisionId, RejectionReason, RejectedAtUtc) VALUES ({raw.DecisionId}, NULL, {f.Now.UtcDateTime})");
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AiRejectedDecisions (AiDecisionId, RejectionReason, RejectedAtUtc) VALUES ({raw.DecisionId}, 'LowConfidence', NULL)");
        });
        Assert.Equal(515, ex.Number);
        Assert.Empty(await db.AiRejectedDecisions.ToListAsync());
    }

    [LocalDbFact]
    public async Task Nonduplicate_sql_error_does_not_enter_replay_or_expose_database_details()
    {
        await using var f = await SqlFixture.CreateAsync();
        var raw = Raw();
        var original = await f.RawService().RecordAsync(raw);
        await using var db = f.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectTestInsert ON AiRejectedDecisions AFTER INSERT AS THROW 50001, 'private-storage-marker', 1;");
        var ex = await Assert.ThrowsAsync<AiRejectedDecisionHistoryException>(() => f.Service().RecordAsync(Request(raw)));
        Assert.Equal(AiRejectedDecisionHistoryFailure.PersistenceFailure, ex.Category);
        Assert.Null(ex.InnerException);
        Assert.DoesNotContain("private-storage-marker", ex.ToString());
        Assert.Empty(await db.AiRejectedDecisions.ToListAsync());
        Assert.Equal(original, await f.RawService().GetByIdAsync(raw.DecisionId));
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
        public int Reads { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            Reads++;
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
            if (observer is not null) observer.TrackedCount = ChangeTracker.Entries().Count();
            return base.DisposeAsync();
        }
    }
    private sealed class SqlFixture(string connection) : IDbContextFactory<StockLabDbContext>, IAsyncDisposable
    {
        public DateTimeOffset Now { get; } = new(2026, 10, 3, 20, 0, 0, TimeSpan.Zero);
        public AiRejectedDecisionHistoryService Service() => new(this, new Clock());
        public AiDecisionHistoryService RawService() => new(this, new Clock());
        public StockLabDbContext CreateDbContext() => new(new DbContextOptionsBuilder<StockLabDbContext>().UseSqlServer(connection).Options);
        public IDbContextFactory<StockLabDbContext> With(IInterceptor interceptor) => new ContextFactory(
            new DbContextOptionsBuilder<StockLabDbContext>().UseSqlServer(connection).AddInterceptors(interceptor).Options, interceptor as QueryObserver);
        public static async Task<SqlFixture> CreateAsync()
        {
            // Isolated LocalDB only; application and Azure credentials are never read.
            var f = new SqlFixture($"Server=(localdb)\\MSSQLLocalDB;Database=StockLabRejectionTests_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true");
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
