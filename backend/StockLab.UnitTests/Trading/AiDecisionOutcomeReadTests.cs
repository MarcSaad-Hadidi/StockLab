using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiDecisionOutcomeReadTests
{
    [Fact]
    public async Task Reads_exact_outcomes_in_one_detached_join()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await VerifyOutcomesAsync(f);
    }

    [LocalDbFact]
    public async Task SQL_Server_reads_exact_outcomes_in_one_detached_join()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await VerifyOutcomesAsync(f);
    }

    private static async Task VerifyOutcomesAsync(AiCurrentPositionsFixture f)
    {
        await f.InitializeAsync();
        var executed = await f.RecordExecutionAsync();
        var trade = await f.ExecuteAsync(executed);
        await f.RecordDecisionAsync(AiTradingSignal.Buy, AiRiskRejectionReason.LowConfidence);
        await f.RecordDecisionAsync(AiTradingSignal.Hold);
        await f.RecordDecisionAsync(AiTradingSignal.Sell);
        await using var db = f.CreateDbContext();
        var ids = await db.AiDecisions.Select(d => d.Id).ToArrayAsync();
        var rejectedId = await db.AiRejectedDecisions.Select(r => r.AiDecisionId).SingleAsync();
        var holdId = await db.AiDecisions.Where(d => d.Signal == "HOLD").Select(d => d.Id).SingleAsync();
        var unresolvedId = await db.AiDecisions.Where(d => d.Signal == "SELL").Select(d => d.Id).SingleAsync();
        var before = await f.StateAsync();
        var observer = new AiTradesHistoryServiceTests.ReadObserver();
        var factory = new AiTradesHistoryServiceTests.TrackingFactory(f.With(observer));
        var outcomes = await new AiDecisionOutcomeReadService(factory).GetAsync(ids);
        Assert.Equal(new AiDecisionOutcome("EXECUTED", null, trade.TradeId), outcomes[executed.DecisionId]);
        Assert.Equal(new AiDecisionOutcome("REJECTED", "LowConfidence", null), outcomes[rejectedId]);
        Assert.Equal(new AiDecisionOutcome("HOLD", null, null), outcomes[holdId]);
        Assert.Equal(new AiDecisionOutcome("UNRESOLVED", null, null), outcomes[unresolvedId]);
        var sql = Assert.Single(observer.Commands);
        Assert.Equal(2, sql.Split("LEFT JOIN").Length - 1);
        Assert.Equal(0, factory.Tracked);
        Assert.Equal(0, observer.SaveCalls);
        Assert.Equal(before, await f.StateAsync());
    }

    [Fact]
    public async Task Trade_history_preserves_nullable_persisted_realized_pnl()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        var buy = await f.ExecuteAsync();
        var sell = await f.ExecuteAsync(price: 120m, signal: AiTradingSignal.Sell);
        var service = new AiTradesHistoryService(f);
        Assert.Null((await service.GetByIdAsync(buy.TradeId))!.RealizedPnL);
        Assert.Equal(200m, (await service.GetByIdAsync(sell.TradeId))!.RealizedPnL);
        Assert.Equal(200m, (await service.GetRecentAsync(200)).Single(t => t.TradeId == sell.TradeId).RealizedPnL);
    }

    [Fact]
    public async Task Empty_cancelled_and_invalid_ID_batches_never_access_storage()
    {
        var service = new AiDecisionOutcomeReadService(new UnavailableFactory());
        Assert.Empty(await service.GetAsync([]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync([Guid.Empty]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync([Guid.NewGuid()], new CancellationToken(true)));
        var failure = await Assert.ThrowsAsync<StockLab.Application.Exceptions.AiDecisionHistoryException>(
            () => service.GetAsync([Guid.NewGuid()]));
        Assert.DoesNotContain("private-marker", failure.ToString());
    }

    private sealed class UnavailableFactory : IDbContextFactory<StockLab.Infrastructure.Persistence.StockLabDbContext>
    {
        public StockLab.Infrastructure.Persistence.StockLabDbContext CreateDbContext() =>
            throw new Microsoft.Data.Sqlite.SqliteException("private-marker", 1);
    }

    private sealed class LocalDbFactAttribute : FactAttribute
    {
        public LocalDbFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
}
