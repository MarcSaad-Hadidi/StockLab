using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public sealed class AiDecisionOutcomeReadService(IDbContextFactory<StockLabDbContext> factory) : IAiDecisionOutcomeReadService
{
    public async Task<IReadOnlyDictionary<Guid, AiDecisionOutcome>> GetAsync(IReadOnlyCollection<Guid> decisionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decisionIds);
        if (decisionIds.Count > 200 || decisionIds.Contains(Guid.Empty))
            throw new ArgumentException("At most 200 nonempty decision IDs are required.", nameof(decisionIds));
        cancellationToken.ThrowIfCancellationRequested();
        if (decisionIds.Count == 0) return new Dictionary<Guid, AiDecisionOutcome>();
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var rows = await (from decision in db.AiDecisions.AsNoTracking()
                where decisionIds.Contains(decision.Id)
                join rejection in db.AiRejectedDecisions.AsNoTracking() on decision.Id equals rejection.AiDecisionId into rejections
                from rejection in rejections.DefaultIfEmpty()
                join trade in db.AiTrades.AsNoTracking() on decision.Id equals trade.AiDecisionId into trades
                from trade in trades.DefaultIfEmpty()
                select new { decision.Id, decision.Signal, TradeId = (Guid?)trade.Id,
                    RejectionReason = (string?)rejection.RejectionReason }).ToArrayAsync(cancellationToken);
            return rows.ToDictionary(r => r.Id, r => new AiDecisionOutcome(r.TradeId is not null ? "EXECUTED"
                : r.RejectionReason is not null ? "REJECTED" : r.Signal == "HOLD" ? "HOLD" : "UNRESOLVED",
                r.RejectionReason, r.TradeId));
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException
            || ex is InvalidOperationException && ex.GetBaseException() is DbException)
        {
            throw new AiDecisionHistoryException(AiDecisionHistoryFailure.PersistenceFailure);
        }
    }
}
