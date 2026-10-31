using System.ComponentModel.DataAnnotations;

namespace StockLab.Api.DTOs.Profile;

public sealed class UpdateUserLanguageRequest
{
    [Required]
    [StringLength(2)]
    [RegularExpression("^(en|fr)$")]
    public string PreferredLanguage { get; init; } = string.Empty;
}
