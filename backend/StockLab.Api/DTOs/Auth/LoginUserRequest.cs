using System.ComponentModel.DataAnnotations;

namespace StockLab.Api.DTOs.Auth;

public sealed class LoginUserRequest
{
    private string email = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email
    {
        get => email;
        init => email = value?.Trim() ?? string.Empty;
    }

    [Required]
    public string Password { get; init; } = string.Empty;
}
