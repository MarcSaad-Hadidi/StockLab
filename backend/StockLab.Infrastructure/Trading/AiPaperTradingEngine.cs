using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public sealed class AiPaperTradingEngine(
    IDbContextFactory<StockLabDbContext> dbContextFactory,
    [FromKeyedServices("Execution")] IMarketDataProvider marketDataProvider,
    IOptions<AiRiskOptions> options,
    TimeProvider timeProvider) : IAiTradeExecutionService
{
    private const decimal MaxStoredMoney = 999999999999999.9999m;

    public async Task<AiTradeExecutionResult> ExecuteAsync(AiTradeExecutionRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        var decision = request.RiskDecision with { Symbol = request.RiskDecision.Symbol.ToUpperInvariant() };
        var fingerprint = Fingerprint(decision);
        try
        {
            // Each attempt owns its contexts; never save another service's pending changes.
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var existing = await FindTradeAsync(db, request.OrderId, cancellationToken);
            if (existing is not null) return Replay(existing, decision, fingerprint);

            var before = await db.AiTraderPortfolios.AsNoTracking().Include(p => p.Positions)
                .SingleOrDefaultAsync(p => p.PortfolioKey == AiTraderPortfolio.MainPortfolioKey, cancellationToken)
                ?? throw new AiTradeExecutionException(AiTradeExecutionFailure.PortfolioNotFound);

            // All market I/O finishes before acquiring database locks. Fetch the target price once.
            var price = await PriceAsync(decision.Symbol, cancellationToken);
            var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { [decision.Symbol] = price };
            if (decision.Signal == AiTradingSignal.Buy)
                foreach (var position in before.Positions)
                    if (!prices.ContainsKey(position.Symbol))
                        prices[position.Symbol] = await PriceAsync(position.Symbol, cancellationToken);

            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var portfolio = await db.AiTraderPortfolios.Include(p => p.Positions)
                .SingleOrDefaultAsync(p => p.Id == before.Id, cancellationToken)
                ?? throw new AiTradeExecutionException(AiTradeExecutionFailure.ConcurrencyConflict);
            existing = await FindTradeAsync(db, request.OrderId, cancellationToken);
            if (existing is not null) return Replay(existing, decision, fingerprint);
            if (!SameState(before, portfolio))
                throw new AiTradeExecutionException(AiTradeExecutionFailure.ConcurrencyConflict);

            var policy = options.Value;
            var riskRequest = new AiRiskRequest(decision.Symbol, decision.Signal, decision.Confidence, price);
            if (AiRiskPolicyEvaluator.Validate(riskRequest, policy) is { } reason)
                throw new AiTradeExecutionException(AiTradeExecutionFailure.RiskChanged, reason);
            var state = new AiTraderPortfolioState(portfolio.Id, portfolio.Currency, portfolio.InitialCapital, portfolio.CashBalance,
                portfolio.Positions.Select(p => new AiTraderPositionState(p.Symbol, p.Quantity, p.AverageCost)).ToArray());
            var holding = portfolio.Positions.SingleOrDefault(p => string.Equals(p.Symbol, decision.Symbol, StringComparison.OrdinalIgnoreCase));
            var quantity = decimal.Round(decision.ApprovedQuantity, 8, MidpointRounding.ToZero);
            if (decision.Signal == AiTradingSignal.Sell && (holding is null || holding.Quantity < decision.ApprovedQuantity))
                throw new AiTradeExecutionException(AiTradeExecutionFailure.InsufficientHoldings);
            var snapshot = decision.Signal == AiTradingSignal.Buy ? Snapshot(state, prices) : null;
            var approval = decision.Signal == AiTradingSignal.Sell
                ? AiRiskPolicyEvaluator.Sell(riskRequest, state)
                : AiRiskPolicyEvaluator.Buy(riskRequest, snapshot!, policy);
            if (!approval.Approved)
                throw new AiTradeExecutionException(AiTradeExecutionFailure.RiskChanged, approval.RejectionReason);
            // A new price/state may shrink BUY sizing; the original approval is always an upper bound.
            quantity = Math.Min(quantity, approval.ApprovedQuantity);
            var total = Money(quantity * price);
            if (quantity <= 0m || total <= 0m)
                throw new AiTradeExecutionException(AiTradeExecutionFailure.TradeTooSmall);
            if (total > MaxStoredMoney)
                throw new AiTradeExecutionException(AiTradeExecutionFailure.StorageCapacityExceeded);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (decision.Signal == AiTradingSignal.Buy)
            {
                if (total > portfolio.CashBalance)
                    throw new AiTradeExecutionException(AiTradeExecutionFailure.InsufficientCash);
                if (AiRiskPolicyEvaluator.ValidateBuySettlement(riskRequest, snapshot!, policy, quantity, total) is { } settlementReason)
                    throw new AiTradeExecutionException(AiTradeExecutionFailure.RiskChanged, settlementReason);
                portfolio.CashBalance = Money(portfolio.CashBalance - total);
                if (holding is null)
                {
                    holding = new AiTraderPosition
                    {
                        Id = Guid.NewGuid(), AiTraderPortfolioId = portfolio.Id, Portfolio = portfolio,
                        Symbol = decision.Symbol, Quantity = quantity, AverageCost = price,
                        CreatedAtUtc = now, UpdatedAtUtc = now
                    };
                    db.AiTraderPositions.Add(holding);
                }
                else
                {
                    var newQuantity = holding.Quantity + quantity;
                    holding.AverageCost = Money((holding.Quantity * holding.AverageCost + quantity * price) / newQuantity);
                    holding.Quantity = newQuantity;
                    holding.UpdatedAtUtc = now;
                }
            }
            else
            {
                if (portfolio.CashBalance > MaxStoredMoney - total)
                    throw new AiTradeExecutionException(AiTradeExecutionFailure.StorageCapacityExceeded);
                portfolio.CashBalance = Money(portfolio.CashBalance + total);
                holding!.Quantity -= quantity;
                if (holding.Quantity == 0m)
                {
                    db.AiTraderPositions.Remove(holding);
                    holding = null;
                }
                else holding.UpdatedAtUtc = now;
            }
            portfolio.UpdatedAtUtc = now;
            // Force a rowversion-checked update even if the clock returns the previous timestamp.
            db.Entry(portfolio).Property(p => p.UpdatedAtUtc).IsModified = true;
            var trade = new AiTrade
            {
                Id = Guid.NewGuid(), AiTraderPortfolioId = portfolio.Id, OrderId = request.OrderId,
                OrderFingerprint = fingerprint, Side = decision.Signal == AiTradingSignal.Buy ? "BUY" : "SELL",
                Symbol = decision.Symbol, Quantity = quantity, ExecutionPrice = price, TotalAmount = total,
                ExecutedAtUtc = now, CashBalanceAfter = portfolio.CashBalance,
                PositionQuantityAfter = holding?.Quantity ?? 0m, AverageCostAfter = holding?.AverageCost
            };
            db.AiTrades.Add(trade);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result(trade, decision, false);
        }
        catch (OverflowException)
        {
            throw new AiTradeExecutionException(AiTradeExecutionFailure.StorageCapacityExceeded);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            // Disposal above rolls back before looking for a competing committed order.
            var conflict = exception is DbUpdateConcurrencyException
                || exception.GetBaseException() is SqlException { Number: 1205 or 2601 or 2627 };
            if (conflict)
            {
                await using var reader = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                AiTrade? committed;
                try { committed = await FindTradeAsync(reader, request.OrderId, cancellationToken); }
                catch (Exception readException) when (IsDatabaseFailure(readException))
                { throw new AiTradeExecutionException(AiTradeExecutionFailure.PersistenceFailure); }
                if (committed is not null) return Replay(committed, decision, fingerprint);
            }
            throw new AiTradeExecutionException(conflict ? AiTradeExecutionFailure.ConcurrencyConflict : AiTradeExecutionFailure.PersistenceFailure);
        }
    }

    private async Task<decimal> PriceAsync(string symbol, CancellationToken cancellationToken)
    {
        StockQuote? quote;
        try { quote = await marketDataProvider.GetQuoteAsync(symbol, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or MarketDataProviderException
            or MarketDataRateLimitException or OperationCanceledException or InvalidOperationException)
        {
            throw new AiTradeExecutionException(AiTradeExecutionFailure.QuoteUnavailable);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (quote is null) throw new AiTradeExecutionException(AiTradeExecutionFailure.QuoteUnavailable);
        if (!string.Equals(quote.Currency, "USD", StringComparison.OrdinalIgnoreCase))
            throw new AiTradeExecutionException(AiTradeExecutionFailure.CurrencyMismatch);
        var price = Money(quote.Price);
        if (price <= 0m || price > MaxStoredMoney || !string.Equals(symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase))
            throw new AiTradeExecutionException(AiTradeExecutionFailure.InvalidExecutionPrice);
        return price;
    }

    private static void Validate(AiTradeExecutionRequest request)
    {
        if (request is null || request.OrderId == Guid.Empty || request.RiskDecision is not { } decision)
            throw new AiTradeExecutionException(AiTradeExecutionFailure.InvalidDecision);
        if (!decision.Approved) throw new AiTradeExecutionException(AiTradeExecutionFailure.RiskRejected);
        if (decision.Signal == AiTradingSignal.Hold) throw new AiTradeExecutionException(AiTradeExecutionFailure.HoldNotExecutable);
        if (decision.ApprovedQuantity <= 0m || decision.ApprovedQuantity > AiRiskPolicyEvaluator.MaxStoredQuantity)
            throw new AiTradeExecutionException(AiTradeExecutionFailure.InvalidQuantity);
        if (decision.RejectionReason is not null || decision.RequestedPrice <= 0m
            || decision.Signal is not (AiTradingSignal.Buy or AiTradingSignal.Sell)
            || decision.Confidence is < 0m or > 1m || string.IsNullOrWhiteSpace(decision.Symbol)
            || decision.Symbol.Length > 32 || decision.Symbol.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new AiTradeExecutionException(AiTradeExecutionFailure.InvalidDecision);
    }

    private static bool SameState(AiTraderPortfolio before, AiTraderPortfolio current) =>
        before.Version.SequenceEqual(current.Version) && before.Currency == current.Currency
        && before.CashBalance == current.CashBalance && before.InitialCapital == current.InitialCapital
        && before.UpdatedAtUtc == current.UpdatedAtUtc
        && before.Positions.OrderBy(p => p.Id).Select(p => (p.Id, p.Symbol, p.Quantity, p.AverageCost, p.UpdatedAtUtc))
            .SequenceEqual(current.Positions.OrderBy(p => p.Id).Select(p => (p.Id, p.Symbol, p.Quantity, p.AverageCost, p.UpdatedAtUtc)));

    private static AiTraderPortfolioSnapshot Snapshot(AiTraderPortfolioState state, Dictionary<string, decimal> prices)
    {
        var positions = state.Positions.Select(p => new AiTraderPositionSnapshot(p.Symbol, p.Quantity, p.AverageCost,
            prices[p.Symbol], p.Quantity * prices[p.Symbol], p.Quantity * (prices[p.Symbol] - p.AverageCost))).ToArray();
        var marketValue = positions.Sum(p => p.MarketValue);
        var total = state.CashBalance + marketValue;
        return new(state.PortfolioId, state.Currency, state.InitialCapital, state.CashBalance, marketValue, total,
            total - state.InitialCapital, positions);
    }

    private static decimal Money(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
    // EF's non-retrying SQL strategy wraps transient SQL errors (including deadlocks) in InvalidOperationException.
    private static bool IsDatabaseFailure(Exception exception) => exception is DbUpdateException or DbException
        || exception is InvalidOperationException && exception.GetBaseException() is DbException;
    private static string Fingerprint(AiRiskDecision decision) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('\n', decision.Symbol, decision.Signal.ToString(), decision.Confidence.ToString("G29", CultureInfo.InvariantCulture),
            decision.RequestedPrice.ToString("G29", CultureInfo.InvariantCulture), decision.ApprovedQuantity.ToString("G29", CultureInfo.InvariantCulture)))));
    private static Task<AiTrade?> FindTradeAsync(StockLabDbContext db, Guid orderId, CancellationToken token) =>
        db.AiTrades.AsNoTracking().SingleOrDefaultAsync(t => t.OrderId == orderId
            && t.Portfolio.PortfolioKey == AiTraderPortfolio.MainPortfolioKey, token);
    private static AiTradeExecutionResult Replay(AiTrade trade, AiRiskDecision decision, string fingerprint)
    {
        if (trade.OrderFingerprint != fingerprint) throw new AiTradeExecutionException(AiTradeExecutionFailure.DuplicateOrder);
        return Result(trade, decision, true);
    }
    private static AiTradeExecutionResult Result(AiTrade trade, AiRiskDecision decision, bool replay) =>
        new(trade.Id, trade.OrderId, trade.AiTraderPortfolioId, trade.Side, trade.Symbol, decision.ApprovedQuantity,
            trade.Quantity, decision.RequestedPrice, trade.ExecutionPrice, trade.TotalAmount, trade.CashBalanceAfter,
            trade.PositionQuantityAfter, trade.AverageCostAfter, trade.ExecutedAtUtc, replay);
}
