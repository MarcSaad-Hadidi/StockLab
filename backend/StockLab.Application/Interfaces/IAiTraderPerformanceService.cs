using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

public interface IAiTraderPerformanceService
{
    /// <summary>Read only; missing portfolio and concurrent state changes fail without initialization.</summary>
    Task<AiTraderPerformanceSnapshot> GetMetricsAsync(CancellationToken cancellationToken = default);
}
