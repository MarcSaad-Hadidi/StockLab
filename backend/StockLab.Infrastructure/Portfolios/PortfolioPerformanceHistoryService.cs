using System.Data;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.Portfolio;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Portfolios;

public sealed class PortfolioPerformanceHistoryService(StockLabDbContext db, TimeProvider clock) : IPortfolioPerformanceHistoryService
{
    public async Task<PortfolioPerformanceHistory?> GetAsync(Guid userId, string range, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        DateTime? from = range switch
        {
            "1D" => now.AddDays(-1), "1W" => now.AddDays(-7), "1M" => now.AddMonths(-1),
            "3M" => now.AddMonths(-3), "1Y" => now.AddYears(-1),
            "YTD" => new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), "ALL" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(range))
        };
        // Keep metadata/count/buckets consistent with captures committing concurrently.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var portfolio = await db.Portfolios.AsNoTracking().Where(row => row.UserId == userId)
            .Select(row => new { row.Id, row.Currency }).SingleOrDefaultAsync(cancellationToken);
        if (portfolio is null) return null;
        var query = db.PortfolioValuationSnapshots.AsNoTracking().Where(point => point.PortfolioId == portfolio.Id
            && point.Currency == portfolio.Currency
            && point.RecordedAtUtc <= now && (from == null || point.RecordedAtUtc >= from));
        var count = await query.CountAsync(cancellationToken);
        var resolution = "observation";
        DateTime? firstRecorded = null, lastRecorded = null;
        PortfolioValuationSnapshot[] rows;
        if (count > 500)
        {
            var first = await query.OrderBy(point => point.RecordedAtUtc).Select(point => point.RecordedAtUtc).FirstAsync(cancellationToken);
            var last = await query.OrderByDescending(point => point.RecordedAtUtc).Select(point => point.RecordedAtUtc).FirstAsync(cancellationToken);
            firstRecorded = first;
            lastRecorded = last;
            (query, resolution) = Downsample(query, first, last);
            // Materialize only the bounded bucket winners before ordering them. Composing
            // another OrderBy over EF's GroupBy/First entity projection is unsupported.
            rows = (await query.ToArrayAsync(cancellationToken)).OrderBy(point => point.RecordedAtUtc)
                .ThenBy(point => point.Id).ToArray();
        }
        else
        {
            rows = await query.OrderBy(point => point.RecordedAtUtc).ThenBy(point => point.Id).Take(500).ToArrayAsync(cancellationToken);
            firstRecorded = rows.FirstOrDefault()?.RecordedAtUtc;
            lastRecorded = rows.LastOrDefault()?.RecordedAtUtc;
        }
        // Buckets return original rows, never invented averages or a new endpoint at 'now'.
        await transaction.CommitAsync(cancellationToken);
        return new(range, portfolio.Currency, from, now, count, resolution, firstRecorded, lastRecorded,
            rows.Select(point => new PortfolioPerformanceHistoryPoint(point.RecordedAtUtc, point.TotalValue,
                point.Source, point.QuoteAsOfUtc)).ToArray());
    }

    private static (IQueryable<PortfolioValuationSnapshot>, string) Downsample(
        IQueryable<PortfolioValuationSnapshot> query, DateTime first, DateTime last)
    {
        // The 400-bucket target leaves room for partially occupied calendar buckets.
        var span = last - first;
        if (span.TotalHours <= 400)
            return (query.GroupBy(point => new { point.RecordedAtUtc.Year, point.RecordedAtUtc.Month,
                point.RecordedAtUtc.Day, point.RecordedAtUtc.Hour }).Select(group => group
                    .OrderByDescending(point => point.RecordedAtUtc).ThenByDescending(point => point.Id).First()), "hour");
        if (span.TotalDays <= 400)
            return (query.GroupBy(point => new { point.RecordedAtUtc.Year, point.RecordedAtUtc.Month, point.RecordedAtUtc.Day })
                .Select(group => group.OrderByDescending(point => point.RecordedAtUtc).ThenByDescending(point => point.Id).First()), "day");
        if ((last.Year - first.Year) * 12 + last.Month - first.Month <= 400)
            return (query.GroupBy(point => new { point.RecordedAtUtc.Year, point.RecordedAtUtc.Month })
                .Select(group => group.OrderByDescending(point => point.RecordedAtUtc).ThenByDescending(point => point.Id).First()), "month");
        var yearsPerBucket = (last.Year - first.Year) / 400 + 1;
        return (query.GroupBy(point => point.RecordedAtUtc.Year / yearsPerBucket)
            .Select(group => group.OrderByDescending(point => point.RecordedAtUtc).ThenByDescending(point => point.Id).First()), "year");
    }
}
