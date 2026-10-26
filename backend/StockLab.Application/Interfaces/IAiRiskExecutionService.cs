using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

public interface IAiRiskExecutionService
{
    Task<AiRiskExecutionResult> ExecuteAsync(Guid decisionId, CancellationToken cancellationToken = default);
}
