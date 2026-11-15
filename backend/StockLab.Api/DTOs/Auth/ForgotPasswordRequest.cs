using System.ComponentModel.DataAnnotations;

namespace StockLab.Api.DTOs.Auth;

public sealed class ForgotPasswordRequest
{
    private string email = string.Empty;
    [Required, EmailAddress, StringLength(254)]
    public string Email { get => email; init => email = value?.Trim() ?? string.Empty; }

    [Required, RegularExpression("^(en|fr)$")]
    public string Language { get; init; } = "en";
}
