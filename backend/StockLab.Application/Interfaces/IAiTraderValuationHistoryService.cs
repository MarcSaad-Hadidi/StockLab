using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

public interface IAiTraderValuationHistoryService
{
    /// <summary>Explicit capture. Reusing an ID revalues and compares facts; changed content conflicts.</summary>
    Task<AiTraderEquityObservation> CaptureCurrentAsync(Guid captureId, CancellationToken cancellationToken = default);
    /// <summary>Latest 1..200 observations in the inclusive UTC range, returned chronologically. Read only.</summary>
    Task<IReadOnlyList<AiTraderEquityObservation>> GetHistoryAsync(DateTime? startDateUtc = null,
        DateTime? endDateUtc = null, int limit = 200, CancellationToken cancellationToken = default);
}
