using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.Portfolio;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Portfolios;

public sealed class PortfolioPerformanceCaptureService(IDbContextFactory<StockLabDbContext> factory,
    IPortfolioService portfolios, IOptions<PortfolioPerformanceCaptureOptions> options, TimeProvider clock,
    PortfolioPerformanceSource selectedSource, IHostEnvironment environment, ILogger<PortfolioPerformanceCaptureService> logger)
    : IPortfolioPerformanceCaptureService
{
    private const decimal Capacity = 100_000_000_000_000_000_000_000_000m; // decimal(38,12), exclusive bound.

    public async Task<PortfolioValuationSnapshot?> CaptureAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var interval = options.Value.Interval;
        if (!options.Value.IsValid()) throw new InvalidOperationException("Invalid personal valuation capture interval.");
        var start = clock.GetUtcNow().UtcDateTime;
        var slot = new DateTime(start.Ticks - start.Ticks % interval.Ticks, DateTimeKind.Utc);
        await using var before = await factory.CreateDbContextAsync(cancellationToken);
        var original = await before.Portfolios.AsNoTracking().Where(row => row.UserId == userId)
            .Select(row => new { row.Id, row.Version, HasHoldings = row.Holdings.Any() }).SingleOrDefaultAsync(cancellationToken);
        if (original is null) return null;
        var existing = await before.PortfolioValuationSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(row => row.PortfolioId == original.Id && row.SlotUtc == slot, cancellationToken);
        if (existing is not null) return existing;

        var source = original.HasHoldings ? selectedSource.Name : "Cash";
        if (source is not ("Cash" or "Mock" or "TwelveData") || (source == "Mock" && !environment.IsDevelopment()))
            throw new InvalidOperationException("Mock holdings must not create production valuation history.");
        var valuation = await portfolios.GetPerformanceAsync(userId, cancellationToken);
        if (valuation is null) return null;
        if (original.HasHoldings != (valuation.Positions.Count > 0))
            throw new InvalidOperationException("Portfolio holdings changed while its valuation was being captured.");
        var recorded = clock.GetUtcNow().UtcDateTime;
        Validate(valuation, slot, recorded);
        var point = new PortfolioValuationSnapshot
        {
            Id = Guid.NewGuid(), PortfolioId = original.Id, SlotUtc = slot, RecordedAtUtc = recorded,
            CashBalance = valuation.CashBalance, PositionsValue = valuation.PositionsMarketValue,
            TotalValue = valuation.TotalValue, InitialCapital = valuation.InitialCapital, Currency = valuation.Currency,
            Source = source, PortfolioVersion = original.Version.ToArray(),
            QuoteAsOfUtc = valuation.Positions.Count == 0 ? null : valuation.Positions.Min(position => position.QuoteAsOfUtc!.Value.UtcDateTime),
            QuoteObservationsJson = JsonSerializer.Serialize(valuation.Positions.Select(position => new
            {
                position.Symbol, Quantity = position.Quantity.ToString(CultureInfo.InvariantCulture),
                Price = position.CurrentPrice.ToString(CultureInfo.InvariantCulture), position.QuoteAsOfUtc
            }))
        };
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            // Quotes are already obtained. Only the short consistency check and insert hold database locks.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var slots = db.PortfolioValuationSnapshots.AsQueryable();
            if (db.Database.IsSqlServer())
                // Reserve even an absent slot before insert to avoid shared-range lock conversion deadlocks.
                slots = db.PortfolioValuationSnapshots.FromSqlInterpolated(
                    $"SELECT * FROM [PortfolioValuationSnapshots] WITH (UPDLOCK) WHERE [PortfolioId] = {original.Id} AND [SlotUtc] = {slot}");
            existing = await slots.AsNoTracking()
                .SingleOrDefaultAsync(row => row.PortfolioId == original.Id && row.SlotUtc == slot, cancellationToken);
            if (existing is not null) return existing;
            var current = await db.Portfolios.AsNoTracking().Include(row => row.Holdings)
                .SingleOrDefaultAsync(row => row.Id == original.Id && row.UserId == userId, cancellationToken);
            if (current is null || !current.Version.SequenceEqual(original.Version)
                || current.CashBalance != valuation.CashBalance || current.InitialCapital != valuation.InitialCapital
                || current.Currency != valuation.Currency || !SameHoldings(current.Holdings, valuation.Positions))
                throw new InvalidOperationException("The portfolio changed while its valuation was being captured.");
            db.PortfolioValuationSnapshots.Add(point);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return point;
        }
        catch (Exception error) when (IsConcurrentWrite(error))
        {
            // Reconcile a concurrent slot winner in a fresh context after the failed transaction is disposed.
            await using var winner = await factory.CreateDbContextAsync(cancellationToken);
            existing = await winner.PortfolioValuationSnapshots.AsNoTracking()
                .SingleOrDefaultAsync(row => row.PortfolioId == original.Id && row.SlotUtc == slot, cancellationToken);
            if (existing is not null) return existing;
            throw;
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (db.Database.IsSqlServer() && (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            throw new InvalidOperationException("Apply personal valuation history migrations before capture.");
        const int batchSize = 50;
        for (var offset = 0; ; offset += batchSize)
        {
            var users = await db.Portfolios.AsNoTracking().OrderBy(row => row.Id)
                .Skip(offset).Take(batchSize).Select(row => row.UserId).ToArrayAsync(cancellationToken);
            foreach (var user in users)
            {
                try { await CaptureAsync(user, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { logger.LogWarning("A personal portfolio valuation could not be captured; existing history was preserved."); }
            }
            if (users.Length < batchSize) break;
        }
    }

    private static bool SameHoldings(ICollection<Holding> holdings, IReadOnlyList<PortfolioPerformancePosition> positions) =>
        holdings.Count == positions.Count && holdings.OrderBy(row => row.Symbol, StringComparer.Ordinal)
            .Zip(positions.OrderBy(row => row.Symbol, StringComparer.Ordinal))
            .All(pair => pair.First.Symbol == pair.Second.Symbol && pair.First.Quantity == pair.Second.Quantity
                && pair.First.AverageCost == pair.Second.AverageCost);

    private static bool IsConcurrentWrite(Exception error)
    {
        // EF may wrap SQL's deadlock victim in InvalidOperationException outside DbUpdateException.
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 or 2601 or 2627 }) return true;
        return false;
    }

    private static void Validate(PortfolioPerformance value, DateTime slot, DateTime recorded)
    {
        if (recorded < slot || value.InitialCapital <= 0m || value.Currency.Length != 3
            || new[] { value.CashBalance, value.PositionsMarketValue, value.TotalValue }
                .Any(amount => amount < 0m || amount >= Capacity || decimal.Round(amount, 12, MidpointRounding.AwayFromZero) != amount)
            || value.TotalValue != value.CashBalance + value.PositionsMarketValue
            || !PortfolioValuationPrecision.IsExact(value)
            || value.Positions.Any(position => position.QuoteAsOfUtc is null
                || position.QuoteAsOfUtc.Value.Offset != TimeSpan.Zero || position.QuoteAsOfUtc.Value.UtcDateTime > recorded))
            throw new InvalidOperationException("An incomplete or out-of-capacity valuation cannot be recorded.");
    }
}
