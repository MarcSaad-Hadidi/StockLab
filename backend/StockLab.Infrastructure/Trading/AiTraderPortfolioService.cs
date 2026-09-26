using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public sealed class AiTraderPortfolioService(
    IDbContextFactory<StockLabDbContext> dbContextFactory,
    IMarketDataProvider marketDataProvider,
    TimeProvider timeProvider) : IAiTraderPortfolioService
{
    public async Task<AiTraderPortfolioState> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        // Own the unit of work so initialization cannot save another scoped service's pending edits.
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var portfolio = await LoadAsync(context, cancellationToken);
        if (portfolio is null)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            portfolio = new AiTraderPortfolio
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            context.AiTraderPortfolios.Add(portfolio);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // A concurrent creator won the unique-key insert. Never reset its cash or positions.
                var existing = await LoadAsync(context, cancellationToken);
                if (existing is null) throw;
                portfolio = existing;
            }
        }

        return new AiTraderPortfolioState(portfolio.Id, portfolio.Currency, portfolio.InitialCapital,
            portfolio.CashBalance, portfolio.Positions.OrderBy(position => position.Symbol, StringComparer.Ordinal)
                .Select(position => new AiTraderPositionState(position.Symbol, position.Quantity, position.AverageCost))
                .ToArray());
    }

    public Task<AiTraderPortfolioState> GetStateAsync(CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(cancellationToken);

    public async Task<AiTraderPortfolioSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(cancellationToken);
        var positions = new List<AiTraderPositionSnapshot>(state.Positions.Count);
        foreach (var position in state.Positions)
        {
            var quote = await marketDataProvider.GetQuoteAsync(position.Symbol, cancellationToken);
            if (quote is null || quote.Price <= 0m)
            {
                throw new InvalidOperationException($"No valid market quote is available for {position.Symbol}.");
            }
            if (!string.Equals(quote.Currency, state.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Quote currency for {position.Symbol} does not match portfolio currency {state.Currency}.");
            }

            var marketValue = position.Quantity * quote.Price;
            positions.Add(new AiTraderPositionSnapshot(position.Symbol, position.Quantity, position.AverageCost,
                quote.Price, marketValue, marketValue - position.Quantity * position.AverageCost));
        }

        var positionsMarketValue = positions.Sum(position => position.MarketValue);
        var totalValue = state.CashBalance + positionsMarketValue;
        return new AiTraderPortfolioSnapshot(state.PortfolioId, state.Currency, state.InitialCapital,
            state.CashBalance, positionsMarketValue, totalValue, totalValue - state.InitialCapital, positions.ToArray());
    }

    private static Task<AiTraderPortfolio?> LoadAsync(StockLabDbContext context, CancellationToken cancellationToken) =>
        context.AiTraderPortfolios.AsNoTracking().Include(portfolio => portfolio.Positions)
            .SingleOrDefaultAsync(portfolio => portfolio.PortfolioKey == AiTraderPortfolio.MainPortfolioKey, cancellationToken);
}
