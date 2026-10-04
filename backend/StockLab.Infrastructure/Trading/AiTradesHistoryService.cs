using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public sealed class AiTradesHistoryService(IDbContextFactory<StockLabDbContext> dbContextFactory) : IAiTradesHistoryService
{
    public async Task<AiTradeHistoryItem?> GetByIdAsync(Guid tradeId, CancellationToken cancellationToken = default)
    {
        if (tradeId == Guid.Empty) throw new ArgumentException("A nonempty trade ID is required.", nameof(tradeId));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await Project(db.AiTrades.AsNoTracking().Where(t => t.Id == tradeId)).SingleOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex) when (IsDatabaseFailure(ex))
        {
            throw new AiTradesHistoryException(AiTradesHistoryFailure.PersistenceFailure);
        }
    }

    public async Task<IReadOnlyList<AiTradeHistoryItem>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit), "History limit must be between 1 and 200.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await Project(db.AiTrades.AsNoTracking().OrderByDescending(t => t.ExecutedAtUtc)
                .ThenByDescending(t => t.Id).Take(limit)).ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (IsDatabaseFailure(ex))
        {
            throw new AiTradesHistoryException(AiTradesHistoryFailure.PersistenceFailure);
        }
    }

    // The optional navigation generates one LEFT JOIN, preserving unlinked legacy trades.
    private static IQueryable<AiTradeHistoryItem> Project(IQueryable<AiTrade> trades) => trades.Select(t =>
        new AiTradeHistoryItem(t.Id, t.OrderId, t.AiDecisionId, t.Side, t.Symbol, t.Quantity, t.ExecutionPrice,
            t.TotalAmount, t.ExecutedAtUtc, t.Decision == null ? null : new AiTradeDecisionSummary(t.Decision.Id,
                t.Decision.Signal == "BUY" ? AiTradingSignal.Buy : t.Decision.Signal == "SELL" ? AiTradingSignal.Sell : AiTradingSignal.Hold,
                t.Decision.Confidence, t.Decision.DecisionDate, t.Decision.ModelName, t.Decision.ModelVersion)));

    private static bool IsDatabaseFailure(Exception ex) => ex is DbUpdateException or DbException
        || ex is InvalidOperationException && ex.GetBaseException() is DbException;
}
