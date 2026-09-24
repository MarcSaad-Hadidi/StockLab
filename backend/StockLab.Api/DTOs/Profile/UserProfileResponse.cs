namespace StockLab.Api.DTOs.Profile;

public sealed record UserProfileResponse(
    Guid Id,
    string DisplayName,
    string Email,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
