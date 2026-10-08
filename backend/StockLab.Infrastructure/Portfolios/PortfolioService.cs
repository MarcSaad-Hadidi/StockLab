using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.Portfolio;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Portfolios;

public sealed class PortfolioService(
    StockLabDbContext dbContext,
    IMarketDataProvider marketDataProvider,
    IOptions<MarketDataRateLimitOptions> rateLimitOptions) : IPortfolioService
{
    // Keep capacity available for unrelated market-data requests. With the default limit of 30,
    // this permits 20 quotes per portfolio request. Lower configured limits are respected so a
    // single request never needs to span multiple limiter windows.
    private const int DefaultMaxPerformanceQuoteLookups = 20;
    private const int ReservedMarketDataPermits = 10;
    private readonly int maxPerformanceQuoteLookups = CalculatePerformanceQuoteBudget(
        rateLimitOptions.Value.PermitLimit);

    private static int CalculatePerformanceQuoteBudget(int permitLimit) =>
        Math.Min(DefaultMaxPerformanceQuoteLookups, Math.Max(1, permitLimit - ReservedMarketDataPermits));

    public Task<Guid?> GetPortfolioIdAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Portfolios
            .AsNoTracking()
            .Where(portfolio => portfolio.UserId == userId)
            .Select(portfolio => (Guid?)portfolio.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<string?> GetPortfolioCurrencyAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Portfolios
            .AsNoTracking()
            .Where(portfolio => portfolio.UserId == userId)
            .Select(portfolio => portfolio.Currency)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PortfolioSummary?> GetPortfolioAsync(Guid userId, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios
            .AsNoTracking()
            .Include(portfolio => portfolio.Holdings)
            .SingleOrDefaultAsync(portfolio => portfolio.UserId == userId, cancellationToken);
        if (portfolio is null)
        {
            return null;
        }

        var positions = portfolio.Holdings
            .OrderBy(holding => holding.Symbol, StringComparer.Ordinal)
            .Select(holding => new PortfolioPosition(holding.Symbol, holding.Quantity, holding.AverageCost))
            .ToArray();
        var investedValue = positions.Sum(position => position.Quantity * position.AverageCost);

        return new PortfolioSummary(portfolio.CashBalance, portfolio.InitialCapital, investedValue,
            portfolio.CashBalance + investedValue, portfolio.Currency, positions);
    }

    public async Task<PortfolioPerformance?> GetPerformanceAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios
            .AsNoTracking()
            .Include(row => row.Holdings)
            .SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (portfolio is null)
            return null;
        if (portfolio.Holdings.Count > maxPerformanceQuoteLookups)
            throw new PortfolioPerformanceUnavailableException();

        var positions = new List<PortfolioPerformancePosition>(portfolio.Holdings.Count);
        foreach (var holding in portfolio.Holdings.OrderBy(row => row.Symbol, StringComparer.Ordinal))
        {
            var quote = await marketDataProvider.GetQuoteAsync(holding.Symbol, cancellationToken);
            if (quote is null || quote.Price <= 0m
                || !string.Equals(quote.Currency.Trim(), portfolio.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new PortfolioPerformanceUnavailableException(holding.Symbol);
            }

            var investedValue = holding.Quantity * holding.AverageCost;
            // Round each live position once before deriving P&L and portfolio totals,
            // matching the twelve-decimal valuation used by the frontend.
            var marketValue = decimal.Round(holding.Quantity * quote.Price, 12, MidpointRounding.AwayFromZero);
            var pnl = marketValue - investedValue;
            positions.Add(new PortfolioPerformancePosition(
                holding.Symbol,
                holding.Quantity,
                holding.AverageCost,
                quote.Price,
                marketValue,
                pnl,
                pnl / investedValue * 100m));
        }

        var investedCapital = positions.Sum(position => position.Quantity * position.AverageCost);
        var positionsMarketValue = positions.Sum(position => position.MarketValue);
        var totalValue = portfolio.CashBalance + positionsMarketValue;
        var totalPnl = totalValue - portfolio.InitialCapital;
        var returnPercent = portfolio.InitialCapital == 0m
            ? 0m
            : totalPnl / portfolio.InitialCapital * 100m;

        return new PortfolioPerformance(
            portfolio.CashBalance,
            portfolio.InitialCapital,
            investedCapital,
            positionsMarketValue,
            totalValue,
            totalPnl,
            returnPercent,
            portfolio.Currency,
            positions.ToArray());
    }

    public async Task<TransactionHistory?> GetTransactionHistoryAsync(
        Guid userId, TransactionHistoryQuery query, CancellationToken cancellationToken)
    {
        // Keep counts, aggregates and rows consistent if a trade commits during this read.
        await using var readTransaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var portfolio = await dbContext.Portfolios.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => new { row.Id, row.Currency })
            .SingleOrDefaultAsync(cancellationToken);
        if (portfolio is null) return null;

        var owned = dbContext.Transactions.AsNoTracking()
            .Where(row => row.PortfolioId == portfolio.Id);
        var summary = await owned.GroupBy(_ => 1)
            .Select(group => new TransactionHistorySummary(
                group.Count(),
                group.Sum(row => row.Side == "BUY" ? row.TotalAmount : 0m),
                group.Sum(row => row.Side == "SELL" ? row.TotalAmount : 0m)))
            .SingleOrDefaultAsync(cancellationToken) ?? new TransactionHistorySummary(0, 0m, 0m);

        var filtered = owned;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToUpperInvariant();
            filtered = filtered.Where(row => row.Symbol.ToUpper().Contains(search)
                || (row.RequestedSymbol != null && row.RequestedSymbol.ToUpper().Contains(search)));
        }
        if (query.Side is not null)
            filtered = filtered.Where(row => row.Side == query.Side);
        // Date-picker values and displayed transaction dates both use UTC days.
        if (query.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            filtered = filtered.Where(row => row.ExecutedAtUtc >= start);
        }
        if (query.To is { } to && to < DateOnly.MaxValue)
        {
            var endExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            filtered = filtered.Where(row => row.ExecutedAtUtc < endExclusive);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 1 : (totalCount - 1) / query.PageSize + 1;
        var page = Math.Min(query.Page, totalPages);
        var items = await filtered.OrderByDescending(row => row.ExecutedAtUtc)
            .ThenByDescending(row => row.Id)
            .Skip((page - 1) * query.PageSize).Take(query.PageSize)
            .Select(row => new PortfolioTransaction(row.Id, row.Side, row.Symbol,
                row.Quantity, row.ExecutionPrice, row.TotalAmount, row.ExecutedAtUtc))
            .ToArrayAsync(cancellationToken);
        await readTransaction.CommitAsync(cancellationToken);
        return new TransactionHistory(items, page, query.PageSize, totalCount, portfolio.Currency, summary);
    }

    public async Task<IReadOnlyList<PortfolioTransaction>?> GetRecentTransactionsAsync(
        Guid userId, int limit, CancellationToken cancellationToken)
    {
        var portfolioId = await dbContext.Portfolios
            .AsNoTracking()
            .Where(portfolio => portfolio.UserId == userId)
            .Select(portfolio => (Guid?)portfolio.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (portfolioId is null)
            return null;

        return await dbContext.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.PortfolioId == portfolioId.Value)
            .OrderByDescending(transaction => transaction.ExecutedAtUtc)
            .ThenByDescending(transaction => transaction.Id)
            .Take(limit)
            .Select(transaction => new PortfolioTransaction(
                transaction.Id,
                transaction.Side,
                transaction.Symbol,
                transaction.Quantity,
                transaction.ExecutionPrice,
                transaction.TotalAmount,
                transaction.ExecutedAtUtc))
            .ToArrayAsync(cancellationToken);
    }
}
