using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public sealed class AiRejectedDecisionHistoryService(
    IDbContextFactory<StockLabDbContext> dbContextFactory,
    TimeProvider timeProvider) : IAiRejectedDecisionHistoryService
{
    public async Task<AiRejectedDecisionRecord> RecordAsync(AiRejectedDecisionRecordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateId(request.DecisionId);
        ValidateRisk(request.RiskDecision);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Save only the rejection, never another scoped service's pending changes.
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var raw = await db.AiDecisions.AsNoTracking().SingleOrDefaultAsync(d => d.Id == request.DecisionId, cancellationToken)
                ?? throw new AiRejectedDecisionHistoryException(AiRejectedDecisionHistoryFailure.DecisionNotFound);
            var risk = request.RiskDecision;
            if (!string.Equals(raw.Symbol, risk.Symbol, StringComparison.Ordinal) ||
                raw.Signal != StoredSignal(risk.Signal) || raw.Confidence != risk.Confidence)
                throw new ArgumentException("The risk outcome must match the original decision's symbol, signal and confidence.", nameof(request));

            var reason = risk.RejectionReason!.Value.ToString();
            var existing = await History(db).SingleOrDefaultAsync(r => r.DecisionId == request.DecisionId, cancellationToken);
            if (existing is not null) return Replay(existing, reason);

            var rejection = new AiRejectedDecision
            {
                AiDecisionId = raw.Id,
                RejectionReason = reason,
                RejectedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            };
            db.AiRejectedDecisions.Add(rejection);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // The PK is the authority when callers raced past the initial lookup.
                db.Entry(rejection).State = EntityState.Detached;
                var winner = await History(db).SingleOrDefaultAsync(r => r.DecisionId == request.DecisionId, cancellationToken)
                    ?? throw new AiRejectedDecisionHistoryException(AiRejectedDecisionHistoryFailure.PersistenceFailure);
                return Replay(winner, reason);
            }
            return new(raw.Id, raw.Symbol, risk.Signal, raw.Confidence, raw.DecisionDate,
                raw.ModelName, raw.ModelVersion, risk.RejectionReason.Value, rejection.RejectedAtUtc);
        }
        catch (Exception ex) when (IsDatabaseFailure(ex))
        {
            throw new AiRejectedDecisionHistoryException(AiRejectedDecisionHistoryFailure.PersistenceFailure);
        }
    }

    public async Task<AiRejectedDecisionRecord?> GetByDecisionIdAsync(Guid decisionId, CancellationToken cancellationToken = default)
    {
        ValidateId(decisionId);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var row = await History(db).SingleOrDefaultAsync(r => r.DecisionId == decisionId, cancellationToken);
            return row is null ? null : Map(row);
        }
        catch (Exception ex) when (IsDatabaseFailure(ex))
        {
            throw new AiRejectedDecisionHistoryException(AiRejectedDecisionHistoryFailure.PersistenceFailure);
        }
    }

    public async Task<IReadOnlyList<AiRejectedDecisionRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit), "History limit must be between 1 and 200.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await History(db).OrderByDescending(r => r.RejectedAtUtc)
                .ThenByDescending(r => r.DecisionDate).ThenByDescending(r => r.DecisionId)
                .Take(limit).ToListAsync(cancellationToken);
            return rows.Select(Map).ToArray();
        }
        catch (Exception ex) when (IsDatabaseFailure(ex))
        {
            throw new AiRejectedDecisionHistoryException(AiRejectedDecisionHistoryFailure.PersistenceFailure);
        }
    }

    private static IQueryable<HistoryRow> History(StockLabDbContext db) =>
        from rejection in db.AiRejectedDecisions.AsNoTracking()
        join decision in db.AiDecisions.AsNoTracking() on rejection.AiDecisionId equals decision.Id
        select new HistoryRow
        {
            DecisionId = decision.Id, Symbol = decision.Symbol, Signal = decision.Signal, Confidence = decision.Confidence,
            DecisionDate = decision.DecisionDate, ModelName = decision.ModelName, ModelVersion = decision.ModelVersion,
            RejectionReason = rejection.RejectionReason, RejectedAtUtc = rejection.RejectedAtUtc
        };

    private static AiRejectedDecisionRecord Replay(HistoryRow existing, string reason)
    {
        if (!string.Equals(existing.RejectionReason, reason, StringComparison.Ordinal))
            throw new AiRejectedDecisionHistoryException(AiRejectedDecisionHistoryFailure.RejectionConflict);
        return Map(existing);
    }

    private static AiRejectedDecisionRecord Map(HistoryRow row)
    {
        var signal = row.Signal switch
        {
            "BUY" => AiTradingSignal.Buy, "SELL" => AiTradingSignal.Sell, "HOLD" => AiTradingSignal.Hold,
            _ => throw new AiRejectedDecisionHistoryException(AiRejectedDecisionHistoryFailure.PersistenceFailure)
        };
        if (!Enum.TryParse<AiRiskRejectionReason>(row.RejectionReason, out var reason) || !Enum.IsDefined(reason) ||
            !string.Equals(reason.ToString(), row.RejectionReason, StringComparison.Ordinal))
            throw new AiRejectedDecisionHistoryException(AiRejectedDecisionHistoryFailure.PersistenceFailure);
        return new(row.DecisionId, row.Symbol, signal, row.Confidence, row.DecisionDate,
            row.ModelName, row.ModelVersion, reason, row.RejectedAtUtc);
    }

    private static void ValidateId(Guid decisionId)
    {
        if (decisionId == Guid.Empty) throw new ArgumentException("A decision ID is required.", nameof(decisionId));
    }

    private static void ValidateRisk(AiRiskDecision risk)
    {
        ArgumentNullException.ThrowIfNull(risk);
        if (risk.Approved || risk.ApprovedQuantity != 0m || risk.RejectionReason is not { } reason || !Enum.IsDefined(reason))
            throw new ArgumentException("A rejected outcome requires a defined rejection reason and zero approved quantity.", nameof(risk));
        if (!Enum.IsDefined(risk.Signal) || risk.Confidence is < 0m or > 1m ||
            string.IsNullOrWhiteSpace(risk.Symbol) || risk.Symbol.Length > 32 || risk.Symbol.Contains(',') ||
            risk.Symbol.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("A risk outcome requires a valid single symbol, signal and confidence.", nameof(risk));
    }

    private static string StoredSignal(AiTradingSignal signal) => signal switch
    {
        AiTradingSignal.Buy => "BUY", AiTradingSignal.Sell => "SELL", AiTradingSignal.Hold => "HOLD",
        _ => throw new ArgumentException("A defined trading signal is required.", nameof(signal))
    };

    private static bool IsDatabaseFailure(Exception ex) => ex is DbUpdateException or DbException ||
        ex is InvalidOperationException && ex.GetBaseException() is DbException;

    private sealed class HistoryRow
    {
        public Guid DecisionId { get; init; }
        public string Symbol { get; init; } = string.Empty;
        public string Signal { get; init; } = string.Empty;
        public decimal Confidence { get; init; }
        public DateOnly DecisionDate { get; init; }
        public string ModelName { get; init; } = string.Empty;
        public string ModelVersion { get; init; } = string.Empty;
        public string RejectionReason { get; init; } = string.Empty;
        public DateTime RejectedAtUtc { get; init; }
    }
}
