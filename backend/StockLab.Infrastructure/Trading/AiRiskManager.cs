using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Trading;

public sealed class AiRiskManager(IAiTraderPortfolioService portfolioService, IOptions<AiRiskOptions> options) : IAiRiskManager
{
    private const decimal MaxStoredQuantity = 99999999999.99999999m; // AiPositions decimal(19,8).

    public async Task<AiRiskDecision> EvaluateAsync(AiRiskRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        AiRiskDecision Reject(AiRiskRejectionReason reason) =>
            new(false, request.Symbol, request.Signal, request.Confidence, request.CurrentPrice, 0m, reason);
        AiRiskDecision Approve(decimal quantity) =>
            new(true, request.Symbol, request.Signal, request.Confidence, request.CurrentPrice, quantity, null);

        if (string.IsNullOrWhiteSpace(request.Symbol) || request.Symbol.Length > 32
            || request.Symbol.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            || !Enum.IsDefined(request.Signal) || request.Confidence is < 0m or > 1m)
            return Reject(AiRiskRejectionReason.InvalidDecision);
        if (request.CurrentPrice <= 0m)
            return Reject(AiRiskRejectionReason.InvalidPrice);
        if (request.Signal == AiTradingSignal.Hold)
            return Reject(AiRiskRejectionReason.HoldSignal);

        var policy = options.Value;
        if (request.Confidence < policy.MinimumConfidence)
            return Reject(AiRiskRejectionReason.LowConfidence);

        try
        {
            if (request.Signal == AiTradingSignal.Sell)
            {
                var state = await portfolioService.GetStateAsync(cancellationToken, initializeIfMissing: false);
                if (!string.Equals(state.Currency, "USD", StringComparison.OrdinalIgnoreCase))
                    return Reject(AiRiskRejectionReason.CurrencyMismatch);
                var held = state.Positions.SingleOrDefault(p => string.Equals(p.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase));
                return held is null || held.Quantity <= 0m
                    ? Reject(AiRiskRejectionReason.NoPositionToSell)
                    : Approve(held.Quantity);
            }

            var snapshot = await portfolioService.GetSnapshotAsync(cancellationToken, initializeIfMissing: false);
            if (!string.Equals(snapshot.Currency, "USD", StringComparison.OrdinalIgnoreCase))
                return Reject(AiRiskRejectionReason.CurrencyMismatch);
            var position = snapshot.Positions.SingleOrDefault(p => string.Equals(p.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase));
            if (position is null && snapshot.Positions.Count(p => p.Quantity > 0m) >= policy.MaxOpenPositions)
                return Reject(AiRiskRejectionReason.MaxPositionsReached);

            checked
            {
                var exposure = (position?.Quantity ?? 0m) * request.CurrentPrice;
                // Use the same server-resolved target price for the numerator and portfolio denominator.
                var totalValue = snapshot.TotalValue - (position?.MarketValue ?? 0m) + exposure;
                var remainingExposure = totalValue * policy.MaxPositionExposurePercent - exposure;
                if (remainingExposure <= 0m)
                    return Reject(AiRiskRejectionReason.MaxSymbolExposureReached);
                if (snapshot.CashBalance <= 0m)
                    return Reject(AiRiskRejectionReason.InsufficientCash);

                var maxNotional = Math.Min(snapshot.CashBalance,
                    Math.Min(remainingExposure, totalValue * policy.MaxCashAllocationPerTradePercent));
                var quantity = decimal.Round(maxNotional / request.CurrentPrice, 8, MidpointRounding.ToZero);
                quantity = Math.Min(quantity, MaxStoredQuantity - (position?.Quantity ?? 0m));
                // Decimal division itself can round up at its precision limit; enforce the final cost too.
                if (quantity * request.CurrentPrice > maxNotional)
                    quantity -= 0.00000001m;
                if (quantity <= 0m)
                    return Reject(AiRiskRejectionReason.TradeTooSmall);
                if (quantity * request.CurrentPrice > maxNotional)
                    throw new InvalidOperationException("AI risk sizing cannot satisfy the notional limit at decimal precision.");
                return Approve(quantity);
            }
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("AI risk evaluation exceeded the supported decimal range.", exception);
        }
    }
}
