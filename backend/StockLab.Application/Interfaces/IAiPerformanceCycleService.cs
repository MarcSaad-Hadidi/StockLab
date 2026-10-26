using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

public interface IAiPerformanceCycleService
{
    Task<AiPerformanceCycleResult> RunOnceAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiPerformanceCycleObservation>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default);
    Task<AiPerformanceCycleDetail?> GetByIdAsync(Guid captureId, int offset = 0, int limit = 100,
        CancellationToken cancellationToken = default);
}
