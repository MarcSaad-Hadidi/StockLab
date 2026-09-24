namespace StockLab.Api.DTOs.Auth;

public sealed record LoginUserResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc,
    LoginUserIdentityResponse User);

public sealed record LoginUserIdentityResponse(Guid Id, string DisplayName, string Email);
