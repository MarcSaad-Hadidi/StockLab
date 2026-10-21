using System.Data;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public sealed class AiTraderPerformanceService(IDbContextFactory<StockLabDbContext> dbContextFactory,
    IAiTraderPortfolioService portfolioService) : IAiTraderPerformanceService
{
    public async Task<AiTraderPerformanceSnapshot> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var value = await AiTraderPerformanceData.ValueAsync(dbContextFactory, portfolioService, cancellationToken);
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await AiTraderPerformanceData.VerifyAsync(db, value, cancellationToken);
            var trades = db.AiTrades.AsNoTracking().Where(t => t.AiTraderPortfolioId == value.PortfolioId);
            var counts = await trades.GroupBy(t => 1).Select(g => new
            {
                Total = g.Count(), Buys = g.Count(t => t.Side == "BUY"), Sells = g.Count(t => t.Side == "SELL"),
                Unknown = g.Count(t => t.Side == "SELL" && t.RealizedPnL == null),
                Wins = g.Count(t => t.Side == "SELL" && t.RealizedPnL > 0m),
                Losses = g.Count(t => t.Side == "SELL" && t.RealizedPnL < 0m),
                Even = g.Count(t => t.Side == "SELL" && t.RealizedPnL == 0m),
                Realized = g.Sum(t => t.Side == "SELL" ? t.RealizedPnL ?? 0m : 0m),
                Won = g.Sum(t => t.Side == "SELL" && t.RealizedPnL > 0m ? t.RealizedPnL!.Value : 0m),
                Lost = g.Sum(t => t.Side == "SELL" && t.RealizedPnL < 0m ? t.RealizedPnL!.Value : 0m)
            }).SingleOrDefaultAsync(cancellationToken);
            var complete = (counts?.Unknown ?? 0) == 0;
            AiRealizedTrade? best = null, worst = null;
            if (complete && counts?.Sells > 0)
            {
                // Stream projected facts: decimal extrema stay exact on both SQL Server and SQLite.
                await foreach (var sell in trades.Where(t => t.Side == "SELL" && t.RealizedPnL != null)
                    .OrderByDescending(t => t.ExecutedAtUtc).ThenByDescending(t => t.Id)
                    .Select(t => new AiRealizedTrade(t.Id, t.Symbol, t.Quantity, t.ExecutionPrice, t.ExecutedAtUtc, t.RealizedPnL!.Value))
                    .AsAsyncEnumerable().WithCancellation(cancellationToken))
                {
                    if (best is null || sell.RealizedPnL > best.RealizedPnL) best = sell;
                    if (worst is null || sell.RealizedPnL < worst.RealizedPnL) worst = sell;
                }
            }
            var samples = 0;
            DateTime? first = null, last = null;
            var baseline = false;
            decimal peak = 0m, maxDrawdown = 0m;
            await foreach (var point in db.AiTraderValuationSnapshots.AsNoTracking().Where(p => p.AiTraderPortfolioId == value.PortfolioId)
                .OrderBy(p => p.ValuedAtUtc).ThenByDescending(p => p.IsInitialBaseline).ThenBy(p => p.Id)
                .Select(p => new { p.ValuedAtUtc, p.CashBalance, p.PositionsValue, p.IsInitialBaseline })
                .AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                if (samples++ == 0) { first = point.ValuedAtUtc; baseline = point.IsInitialBaseline; }
                last = point.ValuedAtUtc;
                var equity = point.CashBalance + point.PositionsValue;
                peak = Math.Max(peak, equity);
                if (peak > 0m) maxDrawdown = Math.Max(maxDrawdown, (peak - equity) / peak);
            }
            var coverage = new AiValuationHistoricalCoverage(samples, first, last, samples == 0 ? AiValuationCoverageStatus.Absent
                : samples < 2 ? AiValuationCoverageStatus.Insufficient : baseline
                    ? AiValuationCoverageStatus.SampledSinceInitialization : AiValuationCoverageStatus.PartialHistory);
            return new(value.PortfolioId, value.Currency, value.InitialCapital, value.CashBalance, value.PositionsMarketValue,
                value.TotalValue, value.TotalValue - value.InitialCapital, value.TotalValue / value.InitialCapital - 1m,
                complete ? counts?.Realized ?? 0m : null, value.Positions.Sum(p => p.UnrealizedPnL),
                !complete ? null : (counts?.Sells ?? 0) == 0 ? 0m : (decimal)counts!.Wins / counts.Sells,
                complete && counts?.Wins > 0 ? counts.Won / counts.Wins : null,
                complete && counts?.Losses > 0 ? counts.Lost / counts.Losses : null,
                best, worst, samples < 2 ? null : maxDrawdown, counts?.Total ?? 0, counts?.Buys ?? 0,
                counts?.Sells ?? 0, counts?.Wins ?? 0, counts?.Losses ?? 0, counts?.Even ?? 0,
                value.Positions.Count, complete, counts?.Unknown ?? 0, value.ValuationTimestampUtc, coverage);
        }
        catch (Exception ex) when (AiTraderPerformanceData.IsDatabaseFailure(ex)) { throw AiTraderPerformanceData.DatabaseFailure(ex); }
        catch (OverflowException) { throw new AiTraderPerformanceException(AiTraderPerformanceFailure.StorageCapacityExceeded); }
    }
}
