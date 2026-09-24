namespace StockLab.Application.Exceptions;

public enum PaperTradingFailure
{
    InvalidOrder,
    PortfolioNotFound,
    InsufficientCash,
    InsufficientHoldings,
    DuplicateOrder,
    ConcurrencyConflict
}

/// <summary>Controlled failure raised when a paper-trading order cannot execute.</summary>
public sealed class PaperTradingException(PaperTradingFailure category)
    : Exception("The paper-trading order could not be executed.")
{
    public PaperTradingFailure Category { get; } = category;
}
