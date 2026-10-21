using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace StockLab.Api.DTOs.AiTrader;

public sealed class AiTraderHistoryQuery : IValidatableObject
{
    [Description("Inclusive UTC ISO 8601 timestamp with Z or +00:00; start must not exceed end.")]
    public string? StartDateUtc { get; init; }

    [Description("Inclusive UTC ISO 8601 timestamp with Z or +00:00.")]
    public string? EndDateUtc { get; init; }

    [Range(1, 200), Description("Latest 1..200 observations, returned chronologically. Default 20.")]
    public int Limit { get; init; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!TryUtc(StartDateUtc, out var start))
            yield return new ValidationResult("A UTC ISO timestamp is required.", [nameof(StartDateUtc)]);
        if (!TryUtc(EndDateUtc, out var end))
            yield return new ValidationResult("A UTC ISO timestamp is required.", [nameof(EndDateUtc)]);
        if (start > end)
            yield return new ValidationResult("Start must not exceed end.", [nameof(EndDateUtc)]);
    }

    public DateTime? StartUtc() { TryUtc(StartDateUtc, out var value); return value; }
    public DateTime? EndUtc() { TryUtc(EndDateUtc, out var value); return value; }

    private static bool TryUtc(string? text, out DateTime? value)
    {
        value = null;
        if (text is null) return true;
        if (!System.Text.RegularExpressions.Regex.IsMatch(text,
                @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)\z")
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || parsed == default) return false;
        value = parsed.UtcDateTime;
        return true;
    }
}
