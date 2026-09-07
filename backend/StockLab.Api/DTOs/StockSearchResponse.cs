namespace StockLab.Api.DTOs;

/// <summary>A stock matching a ticker or company-name search.</summary>
public sealed record StockSearchResponse(
    string Symbol,
    string CompanyName,
    string? Exchange,
    string? Currency);
