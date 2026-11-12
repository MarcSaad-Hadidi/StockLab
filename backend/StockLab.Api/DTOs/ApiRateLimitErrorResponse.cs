namespace StockLab.Api.DTOs;

public sealed record ApiRateLimitErrorResponse(string Error, string Message, int RetryAfterSeconds);
