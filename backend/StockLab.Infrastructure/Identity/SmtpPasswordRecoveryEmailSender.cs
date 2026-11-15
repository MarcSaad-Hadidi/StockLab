using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Identity;

public sealed class SmtpPasswordRecoveryEmailSender(IOptions<PasswordRecoveryOptions> options,
    TimeProvider timeProvider) : IPasswordRecoveryEmailSender
{
    public async Task SendAsync(string email, string language, Guid userId, string token, CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        var link = $"{configuration.FrontendBaseUrl.TrimEnd('/')}/reset-password#userId={userId:D}&token={token}";
        var french = language == "fr";
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(configuration.FromEmail));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = french ? "Réinitialisez votre mot de passe StockLab" : "Reset your StockLab password";
        message.Body = new TextPart("plain")
        {
            Text = french
                ? $"Pour choisir un nouveau mot de passe StockLab, ouvrez ce lien :\n\n{link}\n\nCe lien expire dans {configuration.TokenLifetimeMinutes} minutes et ne peut être utilisé qu'une seule fois. Si vous n'avez pas demandé cette réinitialisation, ignorez ce courriel."
                : $"To choose a new StockLab password, open this link:\n\n{link}\n\nThis link expires in {configuration.TokenLifetimeMinutes} minutes and can only be used once. If you did not request this reset, ignore this email."
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var client = new SmtpClient { Timeout = 20_000 };
        var security = configuration.Security switch
        {
            "StartTls" => SecureSocketOptions.StartTls,
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            "None" => SecureSocketOptions.None,
            _ => throw new InvalidOperationException("Invalid recovery transport configuration.")
        };
        await client.ConnectAsync(configuration.SmtpHost, configuration.SmtpPort, security, linked.Token);
        if (!string.IsNullOrEmpty(configuration.Username))
            await client.AuthenticateAsync(configuration.Username, configuration.Password, linked.Token);
        await client.SendAsync(message, linked.Token);
        await client.DisconnectAsync(true, linked.Token);
    }
}
