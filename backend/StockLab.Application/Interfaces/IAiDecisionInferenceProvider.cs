using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

public interface IAiDecisionInferenceProvider
{
    Task<AiInferenceBatch> InferAsync(DateOnly decisionDate, CancellationToken cancellationToken = default);
}
