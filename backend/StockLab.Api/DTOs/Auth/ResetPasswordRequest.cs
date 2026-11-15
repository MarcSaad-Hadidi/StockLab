using System.ComponentModel.DataAnnotations;

namespace StockLab.Api.DTOs.Auth;

public sealed class ResetPasswordRequest
{
    // Token shape is checked by the reset service so all invalid links share one error.
    public Guid UserId { get; init; }
    public string? Token { get; init; }
    [Required, MinLength(8), MaxLength(1024)]
    public string NewPassword { get; init; } = string.Empty;
    [Required, MaxLength(1024), Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; init; } = string.Empty;
}
