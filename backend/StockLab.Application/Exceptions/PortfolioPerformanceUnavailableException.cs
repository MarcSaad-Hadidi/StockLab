namespace StockLab.Application.Exceptions;

/// <summary>Indicates that a current quote could not safely value a portfolio position.</summary>
public sealed class PortfolioPerformanceUnavailableException(string? symbol = null) : Exception
{
    public string? Symbol { get; } = symbol;
}
