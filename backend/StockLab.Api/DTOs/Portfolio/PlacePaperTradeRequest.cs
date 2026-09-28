using System.ComponentModel.DataAnnotations;

namespace StockLab.Api.DTOs.Portfolio;

/// <summary>Input for one authenticated paper-trading order.</summary>
public sealed class PlacePaperTradeRequest : IValidatableObject
{
    private string side = string.Empty;
    private string symbol = string.Empty;
    private string orderType = "market";

    [Required]
    public Guid OrderId { get; init; }

    [Required]
    public string Side
    {
        get => side;
        init => side = value?.Trim().ToUpperInvariant() ?? string.Empty;
    }

    [Required]
    [StringLength(32)]
    public string Symbol
    {
        get => symbol;
        init => symbol = value?.Trim().ToUpperInvariant() ?? string.Empty;
    }

    [Range(typeof(decimal), "0.00000001", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    [Required]
    public string OrderType
    {
        get => orderType;
        init => orderType = value?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    public decimal? LimitPrice { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OrderId == Guid.Empty)
            yield return new ValidationResult("OrderId is required.", [nameof(OrderId)]);

        if (Side is not ("BUY" or "SELL"))
            yield return new ValidationResult("Side must be BUY or SELL.", [nameof(Side)]);

        if (string.IsNullOrWhiteSpace(Symbol))
            yield return new ValidationResult("Symbol is required.", [nameof(Symbol)]);

        if (OrderType is not ("market" or "limit"))
            yield return new ValidationResult("OrderType must be market or limit.", [nameof(OrderType)]);

        if (OrderType == "limit" && (!LimitPrice.HasValue || LimitPrice.Value <= 0m))
            yield return new ValidationResult("LimitPrice must be greater than zero for a limit order.", [nameof(LimitPrice)]);

        if (OrderType == "market" && LimitPrice.HasValue)
            yield return new ValidationResult("LimitPrice is only valid for a limit order.", [nameof(LimitPrice)]);
    }
}
