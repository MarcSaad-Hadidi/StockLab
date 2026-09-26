namespace StockLab.Infrastructure.Trading;

public sealed class AiRiskOptions
{
    public const string SectionName = "AiTrader:Risk";
    public const string ValidationMessage = "AI risk confidence must be in [0,1], exposure and allocation in (0,1], allocation <= exposure, positions >= 1, and short selling disabled.";

    public decimal MinimumConfidence { get; set; } = 0.70m;
    public decimal MaxPositionExposurePercent { get; set; } = 0.20m;
    public int MaxOpenPositions { get; set; } = 10;
    public decimal MaxCashAllocationPerTradePercent { get; set; } = 0.10m;
    public bool AllowShortSelling { get; set; }

    public bool IsValid() => MinimumConfidence is >= 0m and <= 1m
        && MaxPositionExposurePercent is > 0m and <= 1m
        && MaxOpenPositions >= 1
        && MaxCashAllocationPerTradePercent is > 0m and <= 1m
        && MaxCashAllocationPerTradePercent <= MaxPositionExposurePercent
        && !AllowShortSelling;
}
