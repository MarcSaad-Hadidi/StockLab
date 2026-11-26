using System.Net;
using System.ComponentModel.DataAnnotations;
using MimeKit;

namespace StockLab.Infrastructure.Identity;

public sealed class PasswordRecoveryOptions
{
    public const string SectionName = "PasswordRecovery";
    public bool Enabled { get; set; }
    public string FrontendBaseUrl { get; set; } = string.Empty;
    public int TokenLifetimeMinutes { get; set; } = 30;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string FromEmail { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Security { get; set; } = "StartTls";

    public bool IsValid(bool development)
    {
        if (!Enabled) return true;
        if (TokenLifetimeMinutes is < 1 or > 60 || SmtpPort is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(SmtpHost) || SmtpHost.Any(char.IsWhiteSpace) ||
            Security is not ("StartTls" or "SslOnConnect" or "None") ||
            !new EmailAddressAttribute().IsValid(FromEmail) || FromEmail.Any(char.IsControl) ||
            !MailboxAddress.TryParse(FromEmail, out var mailbox) || mailbox.Address != FromEmail ||
            FrontendBaseUrl.Any(char.IsWhiteSpace) || !Uri.TryCreate(FrontendBaseUrl, UriKind.Absolute, out var frontend) ||
            frontend.Query.Length != 0 || frontend.Fragment.Length != 0 || frontend.UserInfo.Length != 0 ||
            ((!string.IsNullOrEmpty(Username) || !string.IsNullOrEmpty(Password)) &&
                (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password)))) return false;
        var localSmtp = SmtpHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(SmtpHost, out var address) && IPAddress.IsLoopback(address);
        if (Security == "None" && !(development && localSmtp)) return false;
        return frontend.Scheme == Uri.UriSchemeHttps ||
            development && frontend.IsLoopback && frontend.Scheme == Uri.UriSchemeHttp;
    }
}
