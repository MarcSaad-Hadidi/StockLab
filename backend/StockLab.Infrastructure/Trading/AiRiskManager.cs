using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Trading;

public sealed class AiRiskManager(IAiTraderPortfolioService portfolioService, IOptions<AiRiskOptions> options) : IAiRiskManager
{
    public async Task<AiRiskDecision> EvaluateAsync(AiRiskRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var policy = options.Value;
        if (AiRiskPolicyEvaluator.Validate(request, policy) is { } reason)
            return AiRiskPolicyEvaluator.Reject(request, reason);
        try
        {
            if (request.Signal == AiTradingSignal.Sell)
                return AiRiskPolicyEvaluator.Sell(request,
                    await portfolioService.GetStateAsync(cancellationToken, initializeIfMissing: false));
            return AiRiskPolicyEvaluator.Buy(request,
                await portfolioService.GetSnapshotAsync(cancellationToken, initializeIfMissing: false), policy);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("AI risk evaluation exceeded the supported decimal range.", exception);
        }
    }
}
