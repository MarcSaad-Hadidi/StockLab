using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.Trading;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public sealed class PaperTradingEngine(
    IDbContextFactory<StockLabDbContext> dbContextFactory,
    TimeProvider timeProvider) : IPaperTradingEngine
{
    private const decimal MaxQuantity = 99_999_999_999.99999999m;
    private const decimal MaxMoney = 999_999_999_999_999.9999m;

    public async Task<PaperTradeResult?> GetExistingAsync(
        Guid authenticatedUserId,
        Guid portfolioId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (authenticatedUserId == Guid.Empty || portfolioId == Guid.Empty || orderId == Guid.Empty)
        {
            throw new ArgumentException("Authenticated user, portfolio, and order identifiers are required.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var transaction = await dbContext.Transactions
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.PortfolioId == portfolioId && row.OrderId == orderId
                && row.Portfolio.UserId == authenticatedUserId, cancellationToken);
        return transaction is null
            ? null
            : await LoadCommittedResultAsync(dbContext, authenticatedUserId, transaction, cancellationToken);
    }

    public async Task<PaperTradeResult> ExecuteAsync(
        Guid authenticatedUserId,
        Guid portfolioId,
        PaperTradeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (authenticatedUserId == Guid.Empty)
        {
            throw new ArgumentException("An authenticated user identifier is required.", nameof(authenticatedUserId));
        }

        var order = Normalize(portfolioId, request);
        // Each order owns its unit of work; never save or discard another scoped service's edits.
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        DbUpdateException? saveException = null;

        await using (var databaseTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var portfolio = await dbContext.Portfolios
                    .Include(row => row.Holdings)
                    .SingleOrDefaultAsync(row => row.Id == portfolioId && row.UserId == authenticatedUserId,
                        cancellationToken);
                if (portfolio is null)
                {
                    throw new PaperTradingException(PaperTradingFailure.PortfolioNotFound);
                }

                var existingTransaction = await dbContext.Transactions
                    .AsNoTracking()
                    .SingleOrDefaultAsync(row => row.PortfolioId == portfolioId && row.OrderId == order.OrderId
                        && row.Portfolio.UserId == authenticatedUserId,
                        cancellationToken);
                if (existingTransaction is not null)
                {
                    EnsureSameOrder(existingTransaction, order);
                    var existingResult = await LoadCommittedResultAsync(
                        dbContext, authenticatedUserId, existingTransaction, cancellationToken);
                    await databaseTransaction.CommitAsync(cancellationToken);
                    return existingResult;
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
                    OrderType = order.OrderType,
                    LimitPrice = order.LimitPrice,
                    ExecutionPrice = order.ExecutionPrice,
                    TotalAmount = order.TotalAmount,
                    ExecutedAtUtc = now
                };
                dbContext.Transactions.Add(transaction);
                await dbContext.SaveChangesAsync(cancellationToken);
                await databaseTransaction.CommitAsync(cancellationToken);

                return CreateResult(transaction, portfolio, holding);
            }
            catch (DbUpdateException exception)
            {
                await databaseTransaction.RollbackAsync(CancellationToken.None);
                saveException = exception;
            }
        }

        if (saveException is null)
        {
            throw new InvalidOperationException("The paper-trading transaction ended without a result.");
        }

        var committedTransaction = await dbContext.Transactions
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.PortfolioId == portfolioId && row.OrderId == order.OrderId
                && row.Portfolio.UserId == authenticatedUserId,
                cancellationToken);
        if (committedTransaction is not null)
        {
            EnsureSameOrder(committedTransaction, order);
            return await LoadCommittedResultAsync(dbContext, authenticatedUserId, committedTransaction, cancellationToken);
        }

        if (saveException is DbUpdateConcurrencyException)
        {
            throw new PaperTradingException(PaperTradingFailure.ConcurrencyConflict);
        }

        ExceptionDispatchInfo.Capture(saveException).Throw();
        throw new InvalidOperationException("Unreachable paper-trading recovery path.");
    }

    private static async Task<PaperTradeResult> LoadCommittedResultAsync(
        StockLabDbContext dbContext, Guid authenticatedUserId, Transaction transaction,
        CancellationToken cancellationToken)
    {
        // A concurrent order may have changed cash and holdings since the tracked portfolio was loaded.
        var portfolio = await dbContext.Portfolios
            .AsNoTracking()
            .Include(row => row.Holdings)
            .SingleOrDefaultAsync(row => row.Id == transaction.PortfolioId && row.UserId == authenticatedUserId,
                cancellationToken);
        if (portfolio is null)
        {
            throw new PaperTradingException(PaperTradingFailure.PortfolioNotFound);
        }

        return CreateResult(transaction, portfolio);
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
        var orderType = request.OrderType?.Trim().ToLowerInvariant() ?? string.Empty;
        decimal? limitPrice = request.LimitPrice.HasValue
            ? decimal.Round(request.LimitPrice.Value, 4, MidpointRounding.AwayFromZero)
            : null;
        if (side is not ("BUY" or "SELL") || string.IsNullOrWhiteSpace(symbol) || symbol.Length > 32)
        {
            throw new ArgumentException("A valid BUY or SELL side and symbol are required.");
        }

        if (orderType is not ("market" or "limit")
            || (orderType == "market" && limitPrice.HasValue)
            || (orderType == "limit" && (!limitPrice.HasValue || limitPrice <= 0m)))
        {
            throw new PaperTradingException(PaperTradingFailure.InvalidOrder);
        }

        var quantity = decimal.Round(request.Quantity, 8, MidpointRounding.AwayFromZero);
        var executionPrice = decimal.Round(request.ExecutionPrice, 4, MidpointRounding.AwayFromZero);
        if (quantity <= 0m || executionPrice <= 0m)
        {
            throw new ArgumentException("Quantity and execution price must be positive.");
        }

        if (orderType == "limit")
        {
            var limitReached = side == "BUY"
                ? executionPrice <= limitPrice!.Value
                : executionPrice >= limitPrice!.Value;
            if (!limitReached)
            {
                throw new PaperTradingException(PaperTradingFailure.LimitPriceNotReached);
            }
        }

        if (quantity > MaxQuantity || executionPrice > MaxMoney || quantity > MaxMoney / executionPrice)
        {
            throw new PaperTradingException(PaperTradingFailure.InvalidOrder);
        }

        var totalAmount = decimal.Round(quantity * executionPrice, 4, MidpointRounding.AwayFromZero);
        if (totalAmount <= 0m)
        {
            throw new ArgumentException("The order total must be positive.");
        }

        return new NormalizedOrder(
            request.OrderId, side, symbol, quantity, orderType, limitPrice, executionPrice, totalAmount);
    }

    private static void EnsureSameOrder(Transaction transaction, NormalizedOrder order)
    {
        if (transaction.Side != order.Side || transaction.Symbol != order.Symbol
            || transaction.Quantity != order.Quantity || transaction.OrderType != order.OrderType
            || transaction.LimitPrice != order.LimitPrice)
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
            transaction.ExecutedAtUtc,
            transaction.OrderType,
            transaction.LimitPrice);
    }

    private sealed record NormalizedOrder(
        Guid OrderId,
        string Side,
        string Symbol,
        decimal Quantity,
        string OrderType,
        decimal? LimitPrice,
        decimal ExecutionPrice,
        decimal TotalAmount);
}
