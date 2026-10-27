using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public sealed class AiTraderValuationHistoryService(IDbContextFactory<StockLabDbContext> dbContextFactory,
    IAiTraderPortfolioService portfolioService) : IAiTraderValuationHistoryService
{
    public async Task<AiTraderEquityObservation> CaptureCurrentAsync(Guid captureId, CancellationToken cancellationToken = default)
        => (await CaptureAsync(captureId, null, cancellationToken)).Observation;

    public async Task<AiTraderEquityObservation> CaptureOnceAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        if (captureId == Guid.Empty) throw new ArgumentException("A capture identity is required.", nameof(captureId));
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var previous = await (from point in db.AiTraderValuationSnapshots.AsNoTracking()
            join portfolio in db.AiTraderPortfolios on point.AiTraderPortfolioId equals portfolio.Id
            where point.CaptureId == captureId && portfolio.PortfolioKey == AiTraderPortfolio.MainPortfolioKey
            select new { Point = point, portfolio.InitialCapital }).SingleOrDefaultAsync(cancellationToken);
        return previous is null ? await CaptureCurrentAsync(captureId, cancellationToken)
            : Observation(previous.Point, previous.InitialCapital);
    }

    internal async Task<(AiTraderEquityObservation Observation, bool IsReplay)> CaptureCycleAsync(
        Guid captureId, DateTime slotUtc, CancellationToken token)
    {
        if (await FindCycleAsync(slotUtc, token) is { } previous) return (previous, true);
        try { return await CaptureAsync(captureId, slotUtc, token); }
        catch (AiTraderPerformanceException error) when (error.Category is AiTraderPerformanceFailure.ConcurrencyConflict
            or AiTraderPerformanceFailure.CaptureConflict)
        {
            // The failed transaction has disposed before reading another worker's committed slot.
            if (await FindCycleAsync(slotUtc, token) is { } winner) return (winner, true);
            throw;
        }
    }

    private async Task<AiTraderEquityObservation?> FindCycleAsync(DateTime slotUtc, CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var row = await (from point in db.AiTraderValuationSnapshots.AsNoTracking()
            join portfolio in db.AiTraderPortfolios on point.AiTraderPortfolioId equals portfolio.Id
            where portfolio.PortfolioKey == AiTraderPortfolio.MainPortfolioKey && point.CycleSlotUtc == slotUtc
            select new { Point = point, portfolio.InitialCapital }).SingleOrDefaultAsync(token);
        return row is null ? null : Observation(row.Point, row.InitialCapital);
    }

    private async Task<(AiTraderEquityObservation Observation, bool IsReplay)> CaptureAsync(Guid captureId,
        DateTime? cycleSlotUtc, CancellationToken cancellationToken)
    {
        if (captureId == Guid.Empty) throw new ArgumentException("A nonempty capture ID is required.", nameof(captureId));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var value = await AiTraderPerformanceData.ValueAsync(dbContextFactory, portfolioService, cancellationToken);
            var quotes = value.Positions.Select(p => new AiTraderQuoteObservation(p.Symbol, p.Quantity,
                p.AverageCost, p.CurrentPrice, p.QuoteAsOfUtc)).ToArray();
            if (value.ValuationTimestampUtc == default || value.ValuationTimestampUtc.Kind != DateTimeKind.Utc
                || quotes.Any(q => q.AsOfUtc == default || q.AsOfUtc.Offset != TimeSpan.Zero
                    || q.AsOfUtc.UtcDateTime > value.ValuationTimestampUtc))
                throw new InvalidOperationException("A valid UTC quote observation no later than capture time is required.");
            var positionsValue = decimal.Round(value.PositionsMarketValue, 4, MidpointRounding.AwayFromZero);
            if (positionsValue is < 0m or > 999999999999999.9999m)
                throw new AiTraderPerformanceException(AiTraderPerformanceFailure.StorageCapacityExceeded);
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var portfolio = await AiTraderPerformanceData.VerifyAsync(db, value, cancellationToken);
            if (value.ValuationTimestampUtc < portfolio.CreatedAtUtc || cycleSlotUtc > value.ValuationTimestampUtc)
                throw new InvalidOperationException("Valuation time cannot precede portfolio creation.");
            var existing = await db.AiTraderValuationSnapshots.AsNoTracking().SingleOrDefaultAsync(p => p.CaptureId == captureId, cancellationToken);
            if (existing is not null)
            {
                if (cycleSlotUtc is not null && existing.CycleSlotUtc == cycleSlotUtc)
                    return (Observation(existing, portfolio.InitialCapital), true);
                if (existing.AiTraderPortfolioId != value.PortfolioId || existing.Currency != value.Currency
                    || existing.CashBalance != value.CashBalance || existing.PositionsValue != positionsValue
                    || existing.IsInitialBaseline || existing.CycleSlotUtc is not null || !existing.PortfolioVersion.SequenceEqual(value.PortfolioVersion)
                    || !Quotes(existing).SequenceEqual(quotes))
                    throw new AiTraderPerformanceException(AiTraderPerformanceFailure.CaptureConflict);
                return (Observation(existing, portfolio.InitialCapital), true);
            }
            // shortcut: cumulative context suits the small paper ledger; normalize provenance when history grows materially.
            var context = cycleSlotUtc is null ? null : JsonSerializer.Serialize(await db.AiTrades.AsNoTracking()
                .Where(t => t.AiTraderPortfolioId == value.PortfolioId)
                .OrderBy(t => t.ExecutedAtUtc).ThenBy(t => t.Id)
                .Select(t => new AiPerformanceTradeContext(t.Id, t.AiDecisionId,
                    t.Decision == null ? null : t.Decision.ModelName, t.Decision == null ? null : t.Decision.ModelVersion,
                    t.Symbol, t.Side, t.Quantity, t.ExecutionPrice, t.RealizedPnL, t.ExecutedAtUtc))
                .ToArrayAsync(cancellationToken));
            var point = new AiTraderValuationSnapshot
            {
                Id = Guid.NewGuid(), AiTraderPortfolioId = value.PortfolioId, CaptureId = captureId,
                ValuedAtUtc = value.ValuationTimestampUtc, CashBalance = value.CashBalance,
                PositionsValue = positionsValue, Currency = value.Currency,
                PortfolioVersion = value.PortfolioVersion.ToArray(), QuoteObservationsJson = JsonSerializer.Serialize(quotes),
                CycleSlotUtc = cycleSlotUtc, TradeContextJson = context
            };
            db.AiTraderValuationSnapshots.Add(point);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (Observation(point, portfolio.InitialCapital), false);
        }
        catch (Exception ex) when (AiTraderPerformanceData.IsDatabaseFailure(ex)) { throw AiTraderPerformanceData.DatabaseFailure(ex); }
        catch (OverflowException) { throw new AiTraderPerformanceException(AiTraderPerformanceFailure.StorageCapacityExceeded); }
    }

    public async Task<IReadOnlyList<AiTraderEquityObservation>> GetHistoryAsync(DateTime? startDateUtc = null,
        DateTime? endDateUtc = null, int limit = 200, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit), "History limit must be between 1 and 200.");
        if (startDateUtc is { } start && (start == default || start.Kind != DateTimeKind.Utc)
            || endDateUtc is { } end && (end == default || end.Kind != DateTimeKind.Utc) || startDateUtc > endDateUtc)
            throw new ArgumentException("History bounds must be ordered, nondefault UTC dates.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var portfolio = await db.AiTraderPortfolios.AsNoTracking().Where(p => p.PortfolioKey == AiTraderPortfolio.MainPortfolioKey)
                .Select(p => new { p.Id, p.InitialCapital }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new AiTraderPerformanceException(AiTraderPerformanceFailure.PortfolioNotFound);
            var query = db.AiTraderValuationSnapshots.AsNoTracking().Where(p => p.AiTraderPortfolioId == portfolio.Id);
            if (startDateUtc is not null) query = query.Where(p => p.ValuedAtUtc >= startDateUtc);
            if (endDateUtc is not null) query = query.Where(p => p.ValuedAtUtc <= endDateUtc);
            var points = await query.OrderByDescending(p => p.ValuedAtUtc).ThenBy(p => p.IsInitialBaseline)
                .ThenByDescending(p => p.Id).Take(limit).ToListAsync(cancellationToken);
            points.Reverse();
            return points.Select(p => Observation(p, portfolio.InitialCapital)).ToArray();
        }
        catch (Exception ex) when (AiTraderPerformanceData.IsDatabaseFailure(ex)) { throw AiTraderPerformanceData.DatabaseFailure(ex); }
    }

    internal static AiTraderEquityObservation Observation(AiTraderValuationSnapshot point, decimal capital)
    {
        var total = point.CashBalance + point.PositionsValue;
        return new(point.Id, point.AiTraderPortfolioId, point.CaptureId, point.ValuedAtUtc, point.Currency,
            point.CashBalance, point.PositionsValue, total, total - capital, total / capital - 1m,
            point.IsInitialBaseline, Quotes(point));
    }

    private static AiTraderQuoteObservation[] Quotes(AiTraderValuationSnapshot point)
    {
        try
        {
            return JsonSerializer.Deserialize<AiTraderQuoteObservation[]>(point.QuoteObservationsJson)
                ?? throw new AiTraderPerformanceException(AiTraderPerformanceFailure.PersistenceFailure);
        }
        catch (JsonException) { throw new AiTraderPerformanceException(AiTraderPerformanceFailure.PersistenceFailure); }
    }
}
