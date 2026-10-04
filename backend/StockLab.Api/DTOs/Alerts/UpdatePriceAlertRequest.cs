using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using StockLab.Application.Alerts;

namespace StockLab.Api.DTOs.Alerts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdatePriceAlertRequest : IValidatableObject
{
    private string condition = string.Empty;

    [Required]
    [RegularExpression(@"(?i)^(Above|Below)$")]
    public string Condition
    {
        get => condition;
        init => condition = value?.Trim() ?? string.Empty;
    }

    public decimal TargetPrice { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!PriceAlertValidation.IsValidTargetPrice(TargetPrice))
            yield return new ValidationResult("TargetPrice must remain positive and fit decimal(19,4) after rounding.", [nameof(TargetPrice)]);
    }
}
