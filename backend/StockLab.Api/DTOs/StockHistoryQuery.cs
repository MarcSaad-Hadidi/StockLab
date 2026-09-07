using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using StockLab.Application.DTOs.MarketData;
using StockLab.Api.Validation;

namespace StockLab.Api.DTOs;

/// <summary>HTTP query binding and validation for the existing history request contract.</summary>
public sealed class StockHistoryQuery : IValidatableObject
{
    [FromQuery(Name = "from"), BindRequired, Required]
    [ModelBinder(BinderType = typeof(HistoryParameterModelBinder))]
    public DateTimeOffset? From { get; init; }

    [FromQuery(Name = "to"), BindRequired, Required]
    [ModelBinder(BinderType = typeof(HistoryParameterModelBinder))]
    public DateTimeOffset? To { get; init; }

    [FromQuery(Name = "interval"), BindRequired, Required, EnumDataType(typeof(StockHistoryInterval))]
    [ModelBinder(BinderType = typeof(HistoryParameterModelBinder))]
    public StockHistoryInterval? Interval { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is { Offset: var fromOffset } && fromOffset != TimeSpan.Zero)
            yield return new ValidationResult("Timestamp must be UTC.", ["from"]);
        if (To is { Offset: var toOffset } && toOffset != TimeSpan.Zero)
            yield return new ValidationResult("Timestamp must be UTC.", ["to"]);
        if (From.HasValue && To.HasValue && From >= To)
            yield return new ValidationResult("From must precede to.", ["from", "to"]);
    }
}
