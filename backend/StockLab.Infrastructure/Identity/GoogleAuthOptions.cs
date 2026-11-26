using System.Text.RegularExpressions;

namespace StockLab.Infrastructure.Identity;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string[] AllowedOrigins { get; set; } = [];

    public bool IsValid(bool development) => !Enabled ||
        ClientId.Length is > 28 and <= 254
        && Regex.IsMatch(ClientId, @"^[0-9]+-[A-Za-z0-9_-]+\.apps\.googleusercontent\.com$", RegexOptions.CultureInvariant)
        && !ClientId.Any(char.IsWhiteSpace) && !string.IsNullOrWhiteSpace(ClientSecret) && ClientSecret.Length <= 4096
        && AllowedOrigins.Length is > 0 and <= 20 && AllowedOrigins.All(value => IsCanonicalOrigin(value, development));

    public bool AllowsOrigin(string? origin) => origin is not null && AllowedOrigins.Contains(origin, StringComparer.Ordinal);

    private static bool IsCanonicalOrigin(string value, bool development) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.GetLeftPart(UriPartial.Authority) == value
        && string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrEmpty(uri.Host)
        && (uri.Scheme == "https" || development && uri.Scheme == "http" && uri.IsLoopback);
}
