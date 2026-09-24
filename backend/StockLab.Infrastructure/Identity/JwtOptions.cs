using System.Text;

namespace StockLab.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const string ValidationMessage = "JWT issuer, audience, signing key and positive access token lifetime are required; the signing key must be at least 32 UTF-8 bytes.";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Issuer) &&
        !string.IsNullOrWhiteSpace(Audience) &&
        !string.IsNullOrWhiteSpace(SigningKey) &&
        Encoding.UTF8.GetByteCount(SigningKey) >= 32 &&
        AccessTokenMinutes > 0;
}
