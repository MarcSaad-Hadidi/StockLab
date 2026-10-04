namespace StockLab.Application.Alerts;

public static class PriceAlertValidation
{
    private const decimal MaximumPrice = 999_999_999_999_999.9999m;

    public static string NormalizeSymbol(string symbol)
    {
        var normalized = symbol?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 32)
            throw new ArgumentException("Symbol must contain between 1 and 32 characters.", nameof(symbol));
        return normalized;
    }

    public static string NormalizeCondition(string condition)
    {
        var trimmed = condition?.Trim();
        if (string.Equals(trimmed, "Above", StringComparison.OrdinalIgnoreCase)) return "Above";
        if (string.Equals(trimmed, "Below", StringComparison.OrdinalIgnoreCase)) return "Below";
        throw new ArgumentException("Condition must be Above or Below.", nameof(condition));
    }

    public static bool IsValidTargetPrice(decimal targetPrice)
    {
        var rounded = decimal.Round(targetPrice, 4, MidpointRounding.AwayFromZero);
        return rounded > 0m && rounded <= MaximumPrice;
    }

    public static decimal NormalizeTargetPrice(decimal targetPrice)
    {
        if (!IsValidTargetPrice(targetPrice))
            throw new ArgumentOutOfRangeException(nameof(targetPrice), "TargetPrice must remain positive and fit decimal(19,4) after rounding.");
        return decimal.Round(targetPrice, 4, MidpointRounding.AwayFromZero);
    }
}
