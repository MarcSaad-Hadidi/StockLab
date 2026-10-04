using System.ComponentModel.DataAnnotations;

namespace StockLab.Api.DTOs.Auth;

public sealed class ChangePasswordRequest : IValidatableObject
{
    [Required]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string NewPassword { get; init; } = string.Empty;

    [Required]
    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (NewPassword == CurrentPassword)
            yield return new ValidationResult("The new password must differ from the current password.", [nameof(NewPassword)]);
    }
}
