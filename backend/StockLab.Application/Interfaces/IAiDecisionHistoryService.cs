using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

/// <summary>Append-only persistence of every valid BUY, SELL and HOLD decision. No risk or execution side effects.</summary>
public interface IAiDecisionHistoryService
{
    /// <summary>Replays the original record for an identical canonical payload; conflicting IDs fail.</summary>
    Task<AiDecisionRecord> RecordAsync(AiDecisionRecordRequest request, CancellationToken cancellationToken = default);
    Task<AiDecisionRecord?> GetByIdAsync(Guid decisionId, CancellationToken cancellationToken = default);
    /// <summary>Newest first by decision date, recorded UTC timestamp and ID; limit must be 1..200.</summary>
    Task<IReadOnlyList<AiDecisionRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
}
