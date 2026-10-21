using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

/// <summary>Reads persisted outcomes for at most 200 exact decision IDs without market data or writes.</summary>
public interface IAiDecisionOutcomeReadService
{
    Task<IReadOnlyDictionary<Guid, AiDecisionOutcome>> GetAsync(IReadOnlyCollection<Guid> decisionIds,
        CancellationToken cancellationToken = default);
}
