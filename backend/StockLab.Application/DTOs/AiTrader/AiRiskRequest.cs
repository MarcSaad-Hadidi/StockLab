namespace StockLab.Application.DTOs.AiTrader;

public enum AiTradingSignal { Buy, Sell, Hold }

/// <summary>Already-produced ML decision. CurrentPrice must be resolved by trusted backend orchestration in USD.</summary>
public sealed record AiRiskRequest(
    string Symbol,
    AiTradingSignal Signal,
    decimal Confidence,
    decimal CurrentPrice);
