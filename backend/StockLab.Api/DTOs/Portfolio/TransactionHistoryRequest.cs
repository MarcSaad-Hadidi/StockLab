using System.ComponentModel.DataAnnotations;
using StockLab.Application.DTOs.Portfolio;

namespace StockLab.Api.DTOs.Portfolio;

public sealed class TransactionHistoryRequest : IValidatableObject
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 50)]
    public int PageSize { get; init; } = 10;

    [StringLength(100)]
    public string? Search { get; init; }

    [RegularExpression("^(BUY|SELL)$")]
    public string? Side { get; init; }

    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From.HasValue && To.HasValue && From.Value > To.Value)
            yield return new ValidationResult("The end date must be on or after the start date.", [nameof(To)]);
    }

    public TransactionHistoryQuery ToQuery() => new(Page, PageSize, Search?.Trim(), Side, From, To);
}
