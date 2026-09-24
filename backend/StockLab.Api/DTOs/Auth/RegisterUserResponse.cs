namespace StockLab.Api.DTOs.Auth;

public sealed record RegisterUserResponse(Guid Id, string DisplayName, string Email, DateTime CreatedAtUtc);
