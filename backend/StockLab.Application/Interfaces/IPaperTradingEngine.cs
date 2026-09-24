using StockLab.Application.DTOs.Trading;

namespace StockLab.Application.Interfaces;

/// <summary>Executes atomic paper-trading orders against a user's portfolio.</summary>
public interface IPaperTradingEngine
{
    Task<PaperTradeResult> ExecuteAsync(
        Guid portfolioId,
        PaperTradeRequest request,
        CancellationToken cancellationToken = default);
}
