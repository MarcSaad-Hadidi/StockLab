using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public sealed class AiPerformanceCycleService(IDbContextFactory<StockLabDbContext> factory,
    AiTraderValuationHistoryService history, IOptions<AiPerformanceCycleOptions> options, TimeProvider clock,
    IHostEnvironment environment, IConfiguration configuration) : IAiPerformanceCycleService
{
    public async Task<AiPerformanceCycleResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value;
        if (!settings.IsValid() || string.IsNullOrWhiteSpace(settings.ExpectedDatabase))
            throw new InvalidOperationException("A valid cycle interval and expected database are required.");
        if (!environment.IsDevelopment() && (configuration["MarketData:Provider"] ?? "Mock") == "Mock")
            throw new InvalidOperationException("Mock valuation is only available in Development.");
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            if (!string.Equals(settings.ExpectedDatabase, db.Database.GetDbConnection().Database, StringComparison.Ordinal))
                throw new InvalidOperationException("Performance cycle database target mismatch.");
            if (!await db.Database.CanConnectAsync(cancellationToken)
                || (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                throw new InvalidOperationException("A reachable database with current migrations is required.");
            if (!await db.AiTraderPortfolios.AnyAsync(p => p.PortfolioKey == AiTraderPortfolio.MainPortfolioKey, cancellationToken))
                throw new AiTraderPerformanceException(AiTraderPerformanceFailure.PortfolioNotFound);
            var ticks = clock.GetUtcNow().UtcTicks;
            var slot = new DateTime(ticks - ticks % settings.Interval.Ticks, DateTimeKind.Utc);
            var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"stocklab-ai-performance-v1:{slot.Ticks}"))[..16]);
            var capture = await history.CaptureCycleAsync(id, slot, cancellationToken);
            return new(new(capture.Observation.CaptureId, slot, capture.Observation), capture.IsReplay);
        }
        catch (Exception error) when (AiTraderPerformanceData.IsDatabaseFailure(error))
        { throw AiTraderPerformanceData.DatabaseFailure(error); }
    }

    public async Task<IReadOnlyList<AiPerformanceCycleObservation>> GetRecentAsync(int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var rows = await (from point in db.AiTraderValuationSnapshots.AsNoTracking()
                join portfolio in db.AiTraderPortfolios on point.AiTraderPortfolioId equals portfolio.Id
                where portfolio.PortfolioKey == AiTraderPortfolio.MainPortfolioKey && point.CycleSlotUtc != null
                orderby point.CycleSlotUtc descending
                select new { portfolio.InitialCapital, Point = new AiTraderValuationSnapshot {
                    Id = point.Id, AiTraderPortfolioId = point.AiTraderPortfolioId, CaptureId = point.CaptureId,
                    CycleSlotUtc = point.CycleSlotUtc, ValuedAtUtc = point.ValuedAtUtc, CashBalance = point.CashBalance,
                    PositionsValue = point.PositionsValue, Currency = point.Currency, QuoteObservationsJson = point.QuoteObservationsJson
                } }).Take(limit).ToArrayAsync(cancellationToken);
            return rows.Select(r => Cycle(r.Point, r.InitialCapital)).ToArray();
        }
        catch (Exception error) when (AiTraderPerformanceData.IsDatabaseFailure(error))
        { throw AiTraderPerformanceData.DatabaseFailure(error); }
    }

    public async Task<AiPerformanceCycleDetail?> GetByIdAsync(Guid captureId, int offset = 0, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (captureId == Guid.Empty) throw new ArgumentException("A capture ID is required.", nameof(captureId));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var row = await (from point in db.AiTraderValuationSnapshots.AsNoTracking()
                join portfolio in db.AiTraderPortfolios on point.AiTraderPortfolioId equals portfolio.Id
                where portfolio.PortfolioKey == AiTraderPortfolio.MainPortfolioKey && point.CaptureId == captureId && point.CycleSlotUtc != null
                select new { Point = point, portfolio.InitialCapital }).SingleOrDefaultAsync(cancellationToken);
            if (row is null) return null;
            var trades = JsonSerializer.Deserialize<AiPerformanceTradeContext[]>(row.Point.TradeContextJson ?? "null")
                ?? throw new AiTraderPerformanceException(AiTraderPerformanceFailure.PersistenceFailure);
            return new(Cycle(row.Point, row.InitialCapital), trades.Length, offset, trades.Skip(offset).Take(limit).ToArray());
        }
        catch (JsonException) { throw new AiTraderPerformanceException(AiTraderPerformanceFailure.PersistenceFailure); }
        catch (Exception error) when (AiTraderPerformanceData.IsDatabaseFailure(error))
        { throw AiTraderPerformanceData.DatabaseFailure(error); }
    }

    private static AiPerformanceCycleObservation Cycle(AiTraderValuationSnapshot point, decimal capital) =>
        new(point.CaptureId, point.CycleSlotUtc!.Value, AiTraderValuationHistoryService.Observation(point, capital));
}
