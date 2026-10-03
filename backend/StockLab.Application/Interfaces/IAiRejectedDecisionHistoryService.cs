using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

public interface IAiRejectedDecisionHistoryService
{
    Task<AiRejectedDecisionRecord> RecordAsync(AiRejectedDecisionRecordRequest request, CancellationToken cancellationToken = default);
    Task<AiRejectedDecisionRecord?> GetByDecisionIdAsync(Guid decisionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiRejectedDecisionRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
}
