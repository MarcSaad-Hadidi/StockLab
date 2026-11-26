using System.ComponentModel.DataAnnotations;

namespace StockLab.Api.DTOs.Auth;

public sealed class GoogleExchangeRequest
{
    [Required, StringLength(4096)] public string Code { get; init; } = "";
    [Required, StringLength(2), RegularExpression("^(en|fr)$")] public string Language { get; init; } = "en";
}

public sealed class GoogleLinkRequest
{
    [Required, StringLength(64), RegularExpression("^[a-f0-9]{64}$")] public string LinkToken { get; init; } = "";
    [Required, StringLength(1024)] public string Password { get; init; } = "";
    [Required, StringLength(2), RegularExpression("^(en|fr)$")] public string Language { get; init; } = "en";
}
