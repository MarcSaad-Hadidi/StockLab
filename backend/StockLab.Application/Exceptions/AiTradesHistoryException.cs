namespace StockLab.Application.Exceptions;

public enum AiTradesHistoryFailure { PersistenceFailure }

/// <summary>Safe history failure without database or connection details.</summary>
public sealed class AiTradesHistoryException(AiTradesHistoryFailure category)
    : Exception("The AI trades history could not be accessed.")
{
    public AiTradesHistoryFailure Category { get; } = category;
}
