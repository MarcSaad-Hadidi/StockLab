using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

public interface IAiRiskManager
{
    Task<AiRiskDecision> EvaluateAsync(AiRiskRequest request, CancellationToken cancellationToken = default);
}
