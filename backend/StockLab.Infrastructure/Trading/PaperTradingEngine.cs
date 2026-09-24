using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.Trading;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public sealed class PaperTradingEngine(
    StockLabDbContext dbContext,
    TimeProvider timeProvider) : IPaperTradingEngine
{
    public async Task<PaperTradeResult> ExecuteAsync(
        Guid portfolioId,
        PaperTradeRequest request,
        CancellationToken cancellationToken = default)
    {
        var order = Normalize(portfolioId, request);

        await using var databaseTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var portfolio = await dbContext.Portfolios
                .Include(row => row.Holdings)
                .SingleOrDefaultAsync(row => row.Id == portfolioId, cancellationToken);
            if (portfolio is null)
            {
                throw new PaperTradingException(PaperTradingFailure.PortfolioNotFound);
            }

            var existingTransaction = await dbContext.Transactions
                .AsNoTracking()
                .SingleOrDefaultAsync(row => row.PortfolioId == portfolioId && row.OrderId == order.OrderId,
                    cancellationToken);
            if (existingTransaction is not null)
            {
                EnsureSameOrder(existingTransaction, order);
                await databaseTransaction.CommitAsync(cancellationToken);
                return CreateResult(existingTransaction, portfolio);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var holding = portfolio.Holdings.SingleOrDefault(row => row.Symbol == order.Symbol);

            if (order.Side == "BUY")
            {
                if (portfolio.CashBalance < order.TotalAmount)
                {
                    throw new PaperTradingException(PaperTradingFailure.InsufficientCash);
                }

                portfolio.CashBalance = decimal.Round(
                    portfolio.CashBalance - order.TotalAmount, 4, MidpointRounding.AwayFromZero);
                if (holding is null)
                {
                    holding = new Holding
                    {
                        Id = Guid.NewGuid(),
                        PortfolioId = portfolioId,
                        Portfolio = portfolio,
                        Symbol = order.Symbol,
                        Quantity = order.Quantity,
                        AverageCost = order.ExecutionPrice,
                        UpdatedAtUtc = now
                    };
                    dbContext.Holdings.Add(holding);
                }
                else
                {
                    var newQuantity = holding.Quantity + order.Quantity;
                    holding.AverageCost = decimal.Round(
                        ((holding.Quantity * holding.AverageCost) + (order.Quantity * order.ExecutionPrice))
                        / newQuantity,
                        4,
                        MidpointRounding.AwayFromZero);
                    holding.Quantity = newQuantity;
                    holding.UpdatedAtUtc = now;
                }
            }
            else
            {
                if (holding is null || holding.Quantity < order.Quantity)
                {
                    throw new PaperTradingException(PaperTradingFailure.InsufficientHoldings);
                }

                portfolio.CashBalance = decimal.Round(
                    portfolio.CashBalance + order.TotalAmount, 4, MidpointRounding.AwayFromZero);
                holding.Quantity -= order.Quantity;
                if (holding.Quantity == 0m)
                {
                    dbContext.Holdings.Remove(holding);
                    portfolio.Holdings.Remove(holding);
                    holding = null;
                }
                else
                {
                    holding.UpdatedAtUtc = now;
                }
            }

            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                PortfolioId = portfolioId,
                Portfolio = portfolio,
                OrderId = order.OrderId,
                Side = order.Side,
                Symbol = order.Symbol,
                Quantity = order.Quantity,
                ExecutionPrice = order.ExecutionPrice,
                TotalAmount = order.TotalAmount,
                ExecutedAtUtc = now
            };
            dbContext.Transactions.Add(transaction);
            await dbContext.SaveChangesAsync(cancellationToken);
            await databaseTransaction.CommitAsync(cancellationToken);

            return CreateResult(transaction, portfolio, holding);
        }
        catch (DbUpdateConcurrencyException)
        {
            await databaseTransaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw new PaperTradingException(PaperTradingFailure.ConcurrencyConflict);
        }
        catch (DbUpdateException)
        {
            await databaseTransaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();

            if (await dbContext.Transactions.AsNoTracking()
                    .AnyAsync(row => row.PortfolioId == portfolioId && row.OrderId == order.OrderId,
                        cancellationToken))
            {
                throw new PaperTradingException(PaperTradingFailure.DuplicateOrder);
            }

            throw;
        }
    }

    private static NormalizedOrder Normalize(Guid portfolioId, PaperTradeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (portfolioId == Guid.Empty || request.OrderId == Guid.Empty)
        {
            throw new ArgumentException("Portfolio and order identifiers are required.");
        }

        var side = request.Side?.Trim().ToUpperInvariant() ?? string.Empty;
        var symbol = request.Symbol?.Trim().ToUpperInvariant() ?? string.Empty;
        if (side is not ("BUY" or "SELL") || string.IsNullOrWhiteSpace(symbol) || symbol.Length > 32)
        {
            throw new ArgumentException("A valid BUY or SELL side and symbol are required.");
        }

        var quantity = decimal.Round(request.Quantity, 8, MidpointRounding.AwayFromZero);
        var executionPrice = decimal.Round(request.ExecutionPrice, 4, MidpointRounding.AwayFromZero);
        if (quantity <= 0m || executionPrice <= 0m)
        {
            throw new ArgumentException("Quantity and execution price must be positive.");
        }

        var totalAmount = decimal.Round(quantity * executionPrice, 4, MidpointRounding.AwayFromZero);
        if (totalAmount <= 0m)
        {
            throw new ArgumentException("The order total must be positive.");
        }

        return new NormalizedOrder(request.OrderId, side, symbol, quantity, executionPrice, totalAmount);
    }

    private static void EnsureSameOrder(Transaction transaction, NormalizedOrder order)
    {
        if (transaction.Side != order.Side || transaction.Symbol != order.Symbol
            || transaction.Quantity != order.Quantity || transaction.ExecutionPrice != order.ExecutionPrice
            || transaction.TotalAmount != order.TotalAmount)
        {
            throw new PaperTradingException(PaperTradingFailure.DuplicateOrder);
        }
    }

    private static PaperTradeResult CreateResult(
        Transaction transaction,
        Portfolio portfolio,
        Holding? holding = null)
    {
        holding ??= portfolio.Holdings.SingleOrDefault(row => row.Symbol == transaction.Symbol);
        return new PaperTradeResult(
            transaction.Id,
            transaction.OrderId,
            transaction.PortfolioId,
            transaction.Side,
            transaction.Symbol,
            transaction.Quantity,
            transaction.ExecutionPrice,
            transaction.TotalAmount,
            portfolio.CashBalance,
            holding?.Quantity ?? 0m,
            holding?.AverageCost,
            transaction.ExecutedAtUtc);
    }

    private sealed record NormalizedOrder(
        Guid OrderId,
        string Side,
        string Symbol,
        decimal Quantity,
        decimal ExecutionPrice,
        decimal TotalAmount);
}
