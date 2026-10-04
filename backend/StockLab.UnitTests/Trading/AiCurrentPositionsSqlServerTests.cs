using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Domain.Entities;

namespace StockLab.UnitTests.Trading;

public sealed class AiCurrentPositionsSqlServerTests
{
    [LocalDbFact]
    public async Task Sql_committed_execution_lifecycle_is_visible_through_current_positions()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await AiCurrentPositionsServiceTests.VerifyExecutionLifecycleAsync(f);
    }

    [LocalDbFact]
    public async Task Sql_precision_of_executed_fractional_quantity_and_average_cost_is_preserved()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        await f.ExecuteAsync(quantity: 3.12500001m, price: 80.1234m);
        f.ValuationMarket.Prices["AAPL"] = 100m;
        Assert.Equal(new AiCurrentPosition("AAPL", 3.12500001m, 80.1234m, 100m,
            312.500001m, 62.114375198766m), Assert.Single(await f.Service().GetCurrentAsync()));
    }

    [LocalDbFact]
    public async Task Sql_read_leaves_all_rows_versions_timestamps_and_pending_edits_unchanged()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        await f.ExecuteAsync();
        await f.RecordDecisionAsync(AiTradingSignal.Buy);
        await f.RecordDecisionAsync(AiTradingSignal.Buy, AiRiskRejectionReason.LowConfidence);
        await f.RecordDecisionAsync(AiTradingSignal.Hold, AiRiskRejectionReason.HoldSignal);
        await using var db = f.CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Original", Email = "readonly@example.com",
            NormalizedEmail = "READONLY@EXAMPLE.COM", PasswordHash = "test-hash",
            CreatedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime, UpdatedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime };
        var portfolio = new Portfolio { Id = Guid.NewGuid(), User = user, CreatedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime };
        db.Holdings.Add(new() { Id = Guid.NewGuid(), Portfolio = portfolio, Symbol = "AAPL", Quantity = 999m,
            AverageCost = 100m, UpdatedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime });
        await db.SaveChangesAsync();
        var before = await f.StateAsync();
        user.DisplayName = "Pending edit";
        var ai = await db.AiTraderPortfolios.SingleAsync();
        ai.CashBalance = 5m;
        var position = await db.AiTraderPositions.SingleAsync();
        position.Quantity = 1m;

        var current = Assert.Single(await f.Service().GetCurrentAsync());
        Assert.Equal(new AiCurrentPosition("AAPL", 10m, 100m, 120m, 1200m, 200m), current);
        Assert.Equal(before, await f.StateAsync());
        Assert.Equal(0, f.Observer.SaveCalls);
        Assert.Equal(EntityState.Modified, db.Entry(user).State);
        Assert.Equal(EntityState.Modified, db.Entry(ai).State);
        Assert.Equal(EntityState.Modified, db.Entry(position).State);
        Assert.All(f.Observer.Commands, sql =>
        {
            Assert.DoesNotContain("AiTrades", sql);
            Assert.DoesNotContain("AiDecisions", sql);
            Assert.DoesNotContain("AiRejectedDecisions", sql);
            Assert.DoesNotContain("Holdings", sql);
            Assert.DoesNotContain("Users", sql);
        });
    }

    [LocalDbFact]
    public async Task Sql_rolled_back_execution_does_not_produce_a_current_position()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        var before = await f.StateAsync();
        var failure = await Assert.ThrowsAsync<AiTradeExecutionException>(() => f.ExecuteAsync(interceptor: new FailAfterSave()));
        Assert.Equal(AiTradeExecutionFailure.PersistenceFailure, failure.Category);
        Assert.Empty(await f.Service().GetCurrentAsync());
        Assert.Equal(before, await f.StateAsync());
        Assert.Empty(f.ValuationMarket.Calls);
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default) => throw new DbUpdateException("Injected rollback before commit.");
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
