using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

public interface IAiDecisionPipelineService
{
    /// <summary>Infer and record a closed daily session; identical retries reuse the original records.</summary>
    Task<IReadOnlyList<AiDecisionRecord>> RunAsync(DateOnly decisionDate, CancellationToken cancellationToken = default);
}
