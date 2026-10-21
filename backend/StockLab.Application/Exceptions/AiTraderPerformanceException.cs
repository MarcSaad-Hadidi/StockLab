namespace StockLab.Application.Exceptions;

public enum AiTraderPerformanceFailure { PortfolioNotFound, ConcurrencyConflict, CaptureConflict, PersistenceFailure, StorageCapacityExceeded }

public sealed class AiTraderPerformanceException(AiTraderPerformanceFailure category)
    : Exception("AI Trader performance could not be read or captured.")
{
    public AiTraderPerformanceFailure Category { get; } = category;
}
