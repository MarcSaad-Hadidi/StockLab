namespace StockLab.Api.DTOs;

/// <summary>Field errors extending the API's public error/message convention.</summary>
public sealed record ApiValidationErrorResponse(
    string Error,
    string Message,
    IReadOnlyDictionary<string, string[]> Errors);
