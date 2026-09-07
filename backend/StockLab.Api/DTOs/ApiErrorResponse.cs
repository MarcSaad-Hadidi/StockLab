namespace StockLab.Api.DTOs;

/// <summary>A public HTTP error with a stable code and a controlled, safe message.</summary>
public sealed record ApiErrorResponse(string Error, string Message);
