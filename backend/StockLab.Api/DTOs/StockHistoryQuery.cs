using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using StockLab.Application.DTOs.MarketData;
using StockLab.Api.Validation;

namespace StockLab.Api.DTOs;

/// <summary>from/to: yyyy-MM-dd for Day/Week/Month; explicit ISO UTC datetime for Minute/Hour. End is exclusive.</summary>
public sealed class StockHistoryQuery : IValidatableObject
{
    [FromQuery(Name = "from"), BindRequired, Required]
    [ModelBinder(BinderType = typeof(HistoryParameterModelBinder))]
    [System.ComponentModel.Description("Inclusive: yyyy-MM-dd for Day/Week/Month; ISO 8601 datetime with Z or +00:00 for Minute/Hour.")]
    public string? From { get; init; }

    [FromQuery(Name = "to"), BindRequired, Required]
    [ModelBinder(BinderType = typeof(HistoryParameterModelBinder))]
    [System.ComponentModel.Description("Exclusive: same temporal format as from, according to interval.")]
    public string? To { get; init; }

    [FromQuery(Name = "interval"), BindRequired, Required, EnumDataType(typeof(StockHistoryInterval))]
    [ModelBinder(BinderType = typeof(HistoryParameterModelBinder))]
    public StockHistoryInterval? Interval { get; init; }

    public StockHistoryRequest ToRequest(string symbol)
    {
        StockHistoryRange range = Interval is StockHistoryInterval.Minute or StockHistoryInterval.Hour
            ? new IntradayHistoryRange(ParseUtc(From!), ParseUtc(To!))
            : new CalendarHistoryRange(DateOnly.ParseExact(From!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly.ParseExact(To!, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        var request = new StockHistoryRequest(symbol, range, Interval!.Value);
        request.Validate();
        return request;
    }

    private static DateTimeOffset ParseUtc(string value)
    {
        string[] formats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
            "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"];
        var result = DateTimeOffset.ParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        if (result.Offset != TimeSpan.Zero) throw new FormatException();
        return result;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        bool valid;
        try { ToRequest("validation"); valid = true; }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException) { valid = false; }
        if (!valid) yield return new ValidationResult("History bounds must match the interval and from must precede to.", ["from", "to"]);
    }
}
