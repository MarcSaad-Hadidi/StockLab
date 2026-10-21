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
            var point = new AiTraderValuationSnapshot
            {
                Id = Guid.NewGuid(), AiTraderPortfolioId = value.PortfolioId, CaptureId = captureId,
                ValuedAtUtc = value.ValuationTimestampUtc, CashBalance = value.CashBalance,
                PositionsValue = positionsValue, Currency = value.Currency,
                PortfolioVersion = value.PortfolioVersion.ToArray(), QuoteObservationsJson = JsonSerializer.Serialize(quotes)
            };
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var portfolio = await AiTraderPerformanceData.VerifyAsync(db, value, cancellationToken);
            if (point.ValuedAtUtc < portfolio.CreatedAtUtc)
                throw new InvalidOperationException("Valuation time cannot precede portfolio creation.");
            var existing = await db.AiTraderValuationSnapshots.AsNoTracking().SingleOrDefaultAsync(p => p.CaptureId == captureId, cancellationToken);
            if (existing is not null)
            {
                if (existing.AiTraderPortfolioId != point.AiTraderPortfolioId || existing.Currency != point.Currency
                    || existing.CashBalance != point.CashBalance || existing.PositionsValue != point.PositionsValue
                    || existing.IsInitialBaseline || !existing.PortfolioVersion.SequenceEqual(point.PortfolioVersion)
                    || !Quotes(existing).SequenceEqual(quotes))
                    throw new AiTraderPerformanceException(AiTraderPerformanceFailure.CaptureConflict);
                return Observation(existing, portfolio.InitialCapital);
            }
            db.AiTraderValuationSnapshots.Add(point);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Observation(point, portfolio.InitialCapital);
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

    private static AiTraderEquityObservation Observation(AiTraderValuationSnapshot point, decimal capital)
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
