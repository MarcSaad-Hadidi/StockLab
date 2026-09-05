namespace StockLab.Api.DTOs;

/// <summary>A known quote request error, without provider or internal details.</summary>
public sealed record StockQuoteErrorResponse(string Error, string Message);
