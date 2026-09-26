namespace StockLab.Application.DTOs.AiTrader;

public enum AiRiskRejectionReason
{
    InvalidDecision,
    InvalidPrice,
    HoldSignal,
    LowConfidence,
    CurrencyMismatch,
    MaxPositionsReached,
    MaxSymbolExposureReached,
    InsufficientCash,
    NoPositionToSell,
    TradeTooSmall
}

/// <summary>Pre-execution approval only; reserves neither cash nor shares. Execution must revalidate atomically.</summary>
public sealed record AiRiskDecision(
    bool Approved,
    string Symbol,
    AiTradingSignal Signal,
    decimal Confidence,
    decimal RequestedPrice,
    decimal ApprovedQuantity,
    AiRiskRejectionReason? RejectionReason);
