using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Exceptions;

public enum AiTradeExecutionFailure
{
    InvalidDecision,
    RiskRejected,
    HoldNotExecutable,
    InvalidQuantity,
    InvalidExecutionPrice,
    QuoteUnavailable,
    CurrencyMismatch,
    RiskChanged,
    InsufficientCash,
    InsufficientHoldings,
    TradeTooSmall,
    StorageCapacityExceeded,
    DuplicateOrder,
    ConcurrencyConflict,
    PortfolioNotFound,
    PersistenceFailure,
    DecisionNotFound,
    DecisionMismatch,
    DecisionRejected,
    DecisionAlreadyExecuted
}

/// <summary>Safe execution failure; provider and database details are never exposed.</summary>
public sealed class AiTradeExecutionException(AiTradeExecutionFailure category, AiRiskRejectionReason? riskReason = null)
    : Exception("The AI paper-trading order could not be executed.")
{
    public AiTradeExecutionFailure Category { get; } = category;
    public AiRiskRejectionReason? RiskReason { get; } = riskReason;
}
