using System.ComponentModel.DataAnnotations;

namespace StockLab.Api.DTOs.Auth;

public sealed class RegisterUserRequest
{
    private string displayName = string.Empty;
    private string email = string.Empty;

    [Required]
    [StringLength(100)]
    public string DisplayName
    {
        get => displayName;
        init => displayName = value?.Trim() ?? string.Empty;
    }

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email
    {
        get => email;
        init => email = value?.Trim() ?? string.Empty;
    }

    [Required]
    [MinLength(8)]
    public string Password { get; init; } = string.Empty;
}
