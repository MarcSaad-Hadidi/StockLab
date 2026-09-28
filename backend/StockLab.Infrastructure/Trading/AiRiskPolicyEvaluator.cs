using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Infrastructure.Trading;

// Shared pure rules: approval and execution differ only in the state/price they supply.
internal static class AiRiskPolicyEvaluator
{
    internal const decimal MaxStoredQuantity = 99999999999.99999999m;

    internal static AiRiskRejectionReason? Validate(AiRiskRequest request, AiRiskOptions policy)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol) || request.Symbol.Length > 32
            || request.Symbol.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            || !Enum.IsDefined(request.Signal) || request.Confidence is < 0m or > 1m)
            return AiRiskRejectionReason.InvalidDecision;
        if (request.CurrentPrice <= 0m) return AiRiskRejectionReason.InvalidPrice;
        if (request.Signal == AiTradingSignal.Hold) return AiRiskRejectionReason.HoldSignal;
        return request.Confidence < policy.MinimumConfidence ? AiRiskRejectionReason.LowConfidence : null;
    }

    internal static AiRiskDecision Reject(AiRiskRequest request, AiRiskRejectionReason reason) =>
        new(false, request.Symbol, request.Signal, request.Confidence, request.CurrentPrice, 0m, reason);

    private static AiRiskDecision Approve(AiRiskRequest request, decimal quantity) =>
        new(true, request.Symbol, request.Signal, request.Confidence, request.CurrentPrice, quantity, null);

    internal static AiRiskDecision Sell(AiRiskRequest request, AiTraderPortfolioState state)
    {
        if (!string.Equals(state.Currency, "USD", StringComparison.OrdinalIgnoreCase))
            return Reject(request, AiRiskRejectionReason.CurrencyMismatch);
        var held = state.Positions.SingleOrDefault(p => string.Equals(p.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase));
        return held is null || held.Quantity <= 0m
            ? Reject(request, AiRiskRejectionReason.NoPositionToSell)
            : Approve(request, held.Quantity);
    }

    internal static AiRiskDecision Buy(AiRiskRequest request, AiTraderPortfolioSnapshot snapshot, AiRiskOptions policy)
    {
        if (!string.Equals(snapshot.Currency, "USD", StringComparison.OrdinalIgnoreCase))
            return Reject(request, AiRiskRejectionReason.CurrencyMismatch);
        var position = snapshot.Positions.SingleOrDefault(p => string.Equals(p.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase));
        if (position is null && snapshot.Positions.Count(p => p.Quantity > 0m) >= policy.MaxOpenPositions)
            return Reject(request, AiRiskRejectionReason.MaxPositionsReached);
        checked
        {
            var exposure = (position?.Quantity ?? 0m) * request.CurrentPrice;
            var totalValue = snapshot.TotalValue - (position?.MarketValue ?? 0m) + exposure;
            var remainingExposure = totalValue * policy.MaxPositionExposurePercent - exposure;
            if (remainingExposure <= 0m) return Reject(request, AiRiskRejectionReason.MaxSymbolExposureReached);
            if (snapshot.CashBalance <= 0m) return Reject(request, AiRiskRejectionReason.InsufficientCash);
            var maxNotional = Math.Min(snapshot.CashBalance,
                Math.Min(remainingExposure, totalValue * policy.MaxCashAllocationPerTradePercent));
            var quantity = decimal.Round(maxNotional / request.CurrentPrice, 8, MidpointRounding.ToZero);
            quantity = Math.Min(quantity, MaxStoredQuantity - (position?.Quantity ?? 0m));
            // Decimal division can round up at its precision limit; enforce the final cost too.
            if (quantity * request.CurrentPrice > maxNotional) quantity -= 0.00000001m;
            if (quantity <= 0m) return Reject(request, AiRiskRejectionReason.TradeTooSmall);
            if (quantity * request.CurrentPrice > maxNotional)
                throw new InvalidOperationException("AI risk sizing cannot satisfy the notional limit at decimal precision.");
            return Approve(request, quantity);
        }
    }

    internal static AiRiskRejectionReason? ValidateBuySettlement(AiRiskRequest request,
        AiTraderPortfolioSnapshot snapshot, AiRiskOptions policy, decimal quantity, decimal storedTotal)
    {
        if (storedTotal > snapshot.TotalValue * policy.MaxCashAllocationPerTradePercent)
            return AiRiskRejectionReason.TradeTooSmall;
        var notional = quantity * request.CurrentPrice;
        var beforeLimit = snapshot.TotalValue * policy.MaxPositionExposurePercent;
        var afterLimit = (snapshot.TotalValue - storedTotal + notional) * policy.MaxPositionExposurePercent;
        var target = snapshot.Positions.SingleOrDefault(p => string.Equals(p.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase));
        if ((target?.MarketValue ?? 0m) + notional > afterLimit)
            return AiRiskRejectionReason.MaxSymbolExposureReached;
        // Rounding must not push another previously compliant holding across its ceiling.
        if (snapshot.Positions.Any(p => p.MarketValue <= beforeLimit && p.MarketValue > afterLimit))
            return AiRiskRejectionReason.MaxSymbolExposureReached;
        return null;
    }
}
