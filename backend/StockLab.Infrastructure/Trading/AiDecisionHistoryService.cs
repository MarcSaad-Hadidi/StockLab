using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public sealed class AiDecisionHistoryService(
    IDbContextFactory<StockLabDbContext> dbContextFactory,
    TimeProvider timeProvider) : IAiDecisionHistoryService
{
    public async Task<AiDecisionRecord> RecordAsync(AiDecisionRecordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var canonical = Validate(request, DateOnly.FromDateTime(now));
        try
        {
            // An owned context prevents history writes from saving another scoped service's pending edits.
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var existing = await FindAsync(db, canonical.DecisionId, cancellationToken);
            if (existing is not null) return Replay(existing, canonical);

            var decision = new AiDecision
            {
                Id = canonical.DecisionId, Symbol = canonical.Symbol,
                Signal = canonical.Signal switch
                {
                    AiTradingSignal.Buy => "BUY",
                    AiTradingSignal.Sell => "SELL",
                    _ => "HOLD"
                },
                Confidence = canonical.Confidence, DecisionDate = canonical.DecisionDate,
                ModelName = canonical.ModelName, ModelVersion = canonical.ModelVersion, RecordedAtUtc = now
            };
            db.AiDecisions.Add(decision);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // Only SQL Server duplicate keys enter this path. The PK is the concurrency authority.
                db.Entry(decision).State = EntityState.Detached;
                existing = await FindAsync(db, canonical.DecisionId, cancellationToken);
                if (existing is null) throw;
                return Replay(existing, canonical);
            }
            return Record(decision);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            throw new AiDecisionHistoryException(AiDecisionHistoryFailure.PersistenceFailure);
        }
    }

    public async Task<AiDecisionRecord?> GetByIdAsync(Guid decisionId, CancellationToken cancellationToken = default)
    {
        if (decisionId == Guid.Empty) throw new ArgumentException("A nonempty decision ID is required.", nameof(decisionId));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var decision = await FindAsync(db, decisionId, cancellationToken);
            return decision is null ? null : Record(decision);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            throw new AiDecisionHistoryException(AiDecisionHistoryFailure.PersistenceFailure);
        }
    }

    public async Task<IReadOnlyList<AiDecisionRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit), "History limit must be between 1 and 200.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var decisions = await db.AiDecisions.AsNoTracking()
                .OrderByDescending(d => d.DecisionDate).ThenByDescending(d => d.RecordedAtUtc).ThenByDescending(d => d.Id)
                .Take(limit).ToListAsync(cancellationToken);
            return decisions.Select(Record).ToArray();
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            throw new AiDecisionHistoryException(AiDecisionHistoryFailure.PersistenceFailure);
        }
    }

    private static AiDecisionRecordRequest Validate(AiDecisionRecordRequest request, DateOnly todayUtc)
    {
        if (request.DecisionId == Guid.Empty)
            throw new ArgumentException("A nonempty decision ID is required.", nameof(request));
        // Match AI risk's symbol policy, preserving the caller's accepted identifier verbatim.
        if (string.IsNullOrWhiteSpace(request.Symbol) || request.Symbol.Length > 32 || request.Symbol.Contains(',')
            || request.Symbol.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("A single valid symbol of at most 32 characters is required.", nameof(request));
        if (!Enum.IsDefined(request.Signal))
            throw new ArgumentException("The signal must be BUY, SELL or HOLD.", nameof(request));
        if (request.Confidence is < 0m or > 1m)
            throw new ArgumentException("Confidence must be between 0 and 1.", nameof(request));
        if (request.DecisionDate == default || request.DecisionDate > todayUtc)
            throw new ArgumentException("A decision date no later than the current UTC date is required.", nameof(request));
        return request with
        {
            ModelName = Identity(request.ModelName, nameof(request.ModelName)),
            ModelVersion = Identity(request.ModelVersion, nameof(request.ModelVersion))
        };
    }

    private static string Identity(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 128)
            throw new ArgumentException("An explicit nonblank model identity of at most 128 characters is required.", parameter);
        return value.Trim();
    }

    private static Task<AiDecision?> FindAsync(StockLabDbContext db, Guid id, CancellationToken token) =>
        db.AiDecisions.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, token);

    private static AiDecisionRecord Replay(AiDecision decision, AiDecisionRecordRequest request)
    {
        var record = Record(decision);
        // Record equality is ordinal for strings and exact for decimals; casing remains part of model identity.
        if (record.Symbol != request.Symbol || record.Signal != request.Signal || record.Confidence != request.Confidence
            || record.DecisionDate != request.DecisionDate || record.ModelName != request.ModelName || record.ModelVersion != request.ModelVersion)
            throw new AiDecisionHistoryException(AiDecisionHistoryFailure.DecisionConflict);
        return record;
    }

    private static AiDecisionRecord Record(AiDecision decision) => new(decision.Id, decision.Symbol,
        decision.Signal switch
        {
            "BUY" => AiTradingSignal.Buy,
            "SELL" => AiTradingSignal.Sell,
            "HOLD" => AiTradingSignal.Hold,
            _ => throw new AiDecisionHistoryException(AiDecisionHistoryFailure.PersistenceFailure)
        }, decision.Confidence, decision.DecisionDate, decision.ModelName, decision.ModelVersion, decision.RecordedAtUtc);

    private static bool IsDatabaseFailure(Exception exception) => exception is DbUpdateException or DbException
        || exception is InvalidOperationException && exception.GetBaseException() is DbException;
}
