using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Identity;

public sealed class SmtpPasswordRecoveryEmailSender(IOptions<PasswordRecoveryOptions> options,
    TimeProvider timeProvider, ILogger<SmtpPasswordRecoveryEmailSender> logger) : IPasswordRecoveryEmailSender
{
    private static readonly TimeSpan ProbeLifetime = TimeSpan.FromSeconds(30);
    private readonly object probeLock = new();
    private Task<bool>? probe;
    private long probeStartedAt;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (probeLock)
        {
            // Share an in-flight probe and its result across every account and requester.
            if (probe is null || probe.IsCompleted && timeProvider.GetElapsedTime(probeStartedAt) >= ProbeLifetime)
            {
                probeStartedAt = timeProvider.GetTimestamp();
                probe = ProbeAsync();
            }
            return probe.WaitAsync(cancellationToken);
        }
    }

    private async Task<bool> ProbeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), timeProvider);
        using var client = new RecoverySmtpClient { Timeout = 5_000 };
        var stage = "connect_or_authenticate";
        try
        {
            await ConnectAsync(client, options.Value, timeout.Token);
            stage = "sender";
            var status = await client.CheckSenderAsync(options.Value.FromEmail, timeout.Token);
            if (status == SmtpStatusCode.Ok)
            {
                await client.DisconnectAsync(true, timeout.Token);
                return true;
            }
            LogFailure(stage, "SenderCheckRejected", (int)status);
        }
        catch (Exception exception)
        {
            LogFailure(stage, exception);
        }
        return false;
    }

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
        var stage = "connect_or_authenticate";
        try
        {
            await ConnectAsync(client, configuration, linked.Token);
            stage = "delivery";
            await client.SendAsync(message, linked.Token);
            stage = "disconnect";
            await client.DisconnectAsync(true, linked.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Recipient-specific failures cannot affect the response to any future Forgot request.
            if (stage == "connect_or_authenticate" || stage == "delivery" &&
                (exception is ServiceNotAuthenticatedException ||
                    exception is SmtpCommandException { ErrorCode: SmtpErrorCode.SenderNotAccepted } ||
                    exception is SmtpCommandException { StatusCode: SmtpStatusCode.ServiceNotAvailable }))
            {
                lock (probeLock)
                {
                    if (probe is { IsCompletedSuccessfully: true, Result: true }) probe = null;
                }
            }
            LogFailure(stage, exception);
        }
    }

    private static async Task ConnectAsync(SmtpClient client, PasswordRecoveryOptions configuration, CancellationToken cancellationToken)
    {
        var security = configuration.Security switch
        {
            "StartTls" => SecureSocketOptions.StartTls,
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            "None" => SecureSocketOptions.None,
            _ => throw new InvalidOperationException("Invalid recovery transport configuration.")
        };
        await client.ConnectAsync(configuration.SmtpHost, configuration.SmtpPort, security, cancellationToken);
        if (!string.IsNullOrEmpty(configuration.Username))
            await client.AuthenticateAsync(configuration.Username, configuration.Password, cancellationToken);
    }

    private void LogFailure(string stage, Exception exception) => LogFailure(stage, exception.GetType().Name,
        exception is SmtpCommandException command ? (int)command.StatusCode : null);

    private void LogFailure(string stage, string failure, int? status) =>
        logger.LogWarning("Password recovery SMTP {Stage} failed ({Failure}, SMTP status {Status}).", stage, failure, status);

    private sealed class RecoverySmtpClient : SmtpClient
    {
        public async Task<SmtpStatusCode> CheckSenderAsync(string sender, CancellationToken cancellationToken)
        {
            // Check relay/sender permissions without a recipient or message body.
            var reply = await SendCommandAsync($"MAIL FROM:<{sender}>", cancellationToken);
            if (reply.StatusCode != SmtpStatusCode.Ok) return reply.StatusCode;
            var reset = await SendCommandAsync("RSET", cancellationToken);
            return reset.StatusCode;
        }
    }
}
