using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.Trading;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Application.Trading;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public sealed class PaperTradingEngine(
    IDbContextFactory<StockLabDbContext> dbContextFactory,
    TimeProvider timeProvider,
    IMarketDataProvider marketDataProvider) : IPaperTradingEngine
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
                var holding = await ReconcileLegacyHoldingAsync(
                    dbContext, portfolio, order.Symbol, now, cancellationToken);

                if (order.Side == "BUY")
                {
                    if (portfolio.CashBalance < order.TotalAmount)
                    {
                        throw new PaperTradingException(PaperTradingFailure.InsufficientCash);
                    }

                    // Bound the resulting decimal(19,8) holding before changing cash or positions.
                    if (holding is not null && holding.Quantity > MaxQuantity - order.Quantity)
                    {
                        throw new PaperTradingException(PaperTradingFailure.InvalidOrder);
                    }

                    portfolio.CashBalance -= order.TotalAmount;
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

                    // Preserve the supported cash limit even when an order has sub-four-decimal value.
                    if (portfolio.CashBalance > MaxMoney - order.TotalAmount)
                    {
                        throw new PaperTradingException(PaperTradingFailure.InvalidOrder);
                    }

                    portfolio.CashBalance += order.TotalAmount;
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
                    RequestedSymbol = order.RequestedSymbol,
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

    private async Task<Holding?> ReconcileLegacyHoldingAsync(
        StockLabDbContext dbContext, Portfolio portfolio, string symbol, DateTime now,
        CancellationToken cancellationToken)
    {
        var holding = portfolio.Holdings.SingleOrDefault(row => row.Symbol == symbol);
        var separator = symbol.IndexOf(':');
        if (separator < 1)
            return holding;

        var legacySymbol = symbol[..separator];
        var legacyHolding = portfolio.Holdings.SingleOrDefault(row => row.Symbol == legacySymbol);
        var legacyTransactions = dbContext.Transactions.Where(row => row.PortfolioId == portfolio.Id
            && row.Portfolio.UserId == portfolio.UserId && row.Symbol == legacySymbol);
        if (legacyHolding is null && !await legacyTransactions.AnyAsync(cancellationToken))
            return holding;

        // Resolve the old unqualified identifier independently: a requested exchange is not proof
        // that its listing owns the legacy position. Unknown mappings must not split a position.
        var quote = await marketDataProvider.GetQuoteAsync(legacySymbol, cancellationToken);
        var resolved = quote is null ? null : TradingSymbol.FromQuote(quote);
        if (resolved is null)
            throw new PaperTradingException(PaperTradingFailure.InvalidOrder);
        if (resolved != symbol)
            return holding;
        if (!string.Equals(quote!.Currency.Trim(), portfolio.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new PaperTradingException(PaperTradingFailure.InvalidOrder);

        if (legacyHolding is not null)
        {
            if (holding is null)
            {
                legacyHolding.Symbol = symbol;
                holding = legacyHolding;
            }
            else
            {
                if (holding.Quantity > MaxQuantity - legacyHolding.Quantity)
                    throw new PaperTradingException(PaperTradingFailure.InvalidOrder);

                var quantity = holding.Quantity + legacyHolding.Quantity;
                holding.AverageCost = decimal.Round(
                    (holding.Quantity * holding.AverageCost + legacyHolding.Quantity * legacyHolding.AverageCost)
                    / quantity, 4, MidpointRounding.AwayFromZero);
                holding.Quantity = quantity;
                dbContext.Holdings.Remove(legacyHolding);
                portfolio.Holdings.Remove(legacyHolding);
            }
            holding.UpdatedAtUtc = now;
        }

        // Keep old order IDs replayable after renaming/merging their position. Only identifiers
        // change; amounts, prices, dates and order terms are preserved. The enclosing trade
        // transaction also rolls this update back on rejection, cancellation or concurrency loss.
        await legacyTransactions.ExecuteUpdateAsync(setters => setters
            .SetProperty(row => row.RequestedSymbol, row => row.RequestedSymbol ?? legacySymbol)
            .SetProperty(row => row.Symbol, symbol), cancellationToken);
        return holding;
    }

    private static async Task<PaperTradeResult> LoadCommittedResultAsync(
        StockLabDbContext dbContext, Guid authenticatedUserId, Transaction transaction,
        CancellationToken cancellationToken)
    {
        // Read the current transaction identifier and balances together: a concurrent order may
        // have reconciled its legacy symbol as well as changed the portfolio's cash and holdings.
        var committed = await dbContext.Transactions
            .AsNoTracking()
            .Include(row => row.Portfolio).ThenInclude(portfolio => portfolio.Holdings)
            .AsSingleQuery()
            .SingleOrDefaultAsync(row => row.Id == transaction.Id && row.Portfolio.UserId == authenticatedUserId,
                cancellationToken);
        if (committed is null)
        {
            throw new PaperTradingException(PaperTradingFailure.PortfolioNotFound);
        }

        return CreateResult(committed, committed.Portfolio);
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
        var requestedSymbol = request.RequestedSymbol?.Trim().ToUpperInvariant() ?? symbol;
        var orderType = request.OrderType?.Trim().ToLowerInvariant() ?? string.Empty;
        decimal? limitPrice = request.LimitPrice.HasValue
            ? decimal.Round(request.LimitPrice.Value, 4, MidpointRounding.AwayFromZero)
            : null;
        if (side is not ("BUY" or "SELL") || string.IsNullOrWhiteSpace(symbol) || symbol.Length > 32)
        {
            throw new ArgumentException("A valid BUY or SELL side and symbol are required.");
        }

        if (string.IsNullOrWhiteSpace(requestedSymbol) || requestedSymbol.Length > 32)
        {
            throw new PaperTradingException(PaperTradingFailure.InvalidOrder);
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

        // Eight quantity decimals times four price decimals require twelve amount decimals.
        // Rounding each order independently would create or destroy cash when trades are split.
        var totalAmount = quantity * executionPrice;
        if (totalAmount <= 0m)
        {
            throw new ArgumentException("The order total must be positive.");
        }

        return new NormalizedOrder(
            request.OrderId, side, symbol, requestedSymbol, quantity, orderType, limitPrice, executionPrice, totalAmount);
    }

    private static void EnsureSameOrder(Transaction transaction, NormalizedOrder order)
    {
        if (transaction.Side != order.Side
            || (transaction.Symbol != order.Symbol && transaction.RequestedSymbol != order.Symbol)
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
            transaction.LimitPrice,
            transaction.RequestedSymbol);
    }

    private sealed record NormalizedOrder(
        Guid OrderId,
        string Side,
        string Symbol,
        string RequestedSymbol,
        decimal Quantity,
        string OrderType,
        decimal? LimitPrice,
        decimal ExecutionPrice,
        decimal TotalAmount);
}
