using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Trading;

public sealed class AiCurrentPositionsService(IAiTraderPortfolioService portfolioService) : IAiCurrentPositionsService
{
    public async Task<IReadOnlyList<AiCurrentPosition>> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await portfolioService.GetSnapshotAsync(cancellationToken, initializeIfMissing: false);
        cancellationToken.ThrowIfCancellationRequested();
        return snapshot.Positions.Select(position => new AiCurrentPosition(
            position.Symbol, position.Quantity, position.AverageCost, position.CurrentPrice,
            position.MarketValue, position.UnrealizedPnL)).ToArray();
    }
}
