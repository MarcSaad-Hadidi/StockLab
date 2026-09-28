using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

public interface IAiTradeExecutionService
{
    Task<AiTradeExecutionResult> ExecuteAsync(AiTradeExecutionRequest request, CancellationToken cancellationToken = default);
}
