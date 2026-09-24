using StockLab.Application.DTOs.Trading;

namespace StockLab.Application.Interfaces;

/// <summary>Executes atomic paper-trading orders against a user's portfolio.</summary>
public interface IPaperTradingEngine
{
    /// <summary>Executes an order only within the authenticated user's portfolio.</summary>
    /// <param name="authenticatedUserId">
    /// The user ID from the validated authentication principal (JWT sub), never from client input.
    /// </param>
    /// <param name="portfolioId">The requested portfolio; ownership is verified by the engine.</param>
    /// <param name="request">The order with a server-resolved execution price.</param>
    /// <param name="cancellationToken">Cancellation for this execution.</param>
    Task<PaperTradeResult> ExecuteAsync(
        Guid authenticatedUserId,
        Guid portfolioId,
        PaperTradeRequest request,
        CancellationToken cancellationToken = default);
}
