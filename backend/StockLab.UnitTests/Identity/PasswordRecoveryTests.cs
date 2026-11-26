using Microsoft.Extensions.Options;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Identity;

namespace StockLab.UnitTests.Identity;

public sealed class PasswordRecoveryTests
{
    [Theory]
    [InlineData("StartTls", "https://stocklab.example", "smtp.example", false, true)]
    [InlineData("SslOnConnect", "https://stocklab.example", "smtp.example", false, true)]
    [InlineData("None", "http://127.0.0.1:5189", "127.0.0.1", true, true)]
    [InlineData("None", "https://stocklab.example", "smtp.example", true, false)]
    [InlineData("None", "https://stocklab.example", "127.0.0.1", false, false)]
    [InlineData("Auto", "https://stocklab.example", "smtp.example", false, false)]
    [InlineData("StartTls", "http://stocklab.example", "smtp.example", false, false)]
    [InlineData("StartTls", "https://stocklab.example/?return=bad", "smtp.example", false, false)]
    [InlineData("StartTls", "https://stocklab.example/#secret", "smtp.example", false, false)]
    [InlineData("StartTls", "https://user:password@stocklab.example", "smtp.example", false, false)]
    [InlineData("StartTls", " https://stocklab.example ", "smtp.example", false, false)]
    public void Recovery_configuration_enforces_safe_link_and_transport(string security, string frontend,
        string host, bool development, bool valid)
    {
        var options = Configured();
        options.Security = security;
        options.FrontendBaseUrl = frontend;
        options.SmtpHost = host;
        Assert.Equal(valid, options.IsValid(development));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Recovery_refuses_unbounded_or_missing_token_lifetime(int minutes)
    {
        var options = Configured();
        options.TokenLifetimeMinutes = minutes;
        Assert.False(options.IsValid(false));
    }

    [Fact]
    public async Task Recovery_queue_refuses_overflow_and_disabled_requests_without_checking_smtp_when_disabled()
    {
        var configuration = Configured();
        var sender = new TestRecoverySender();
        var queue = new PasswordRecoveryRequestQueue(Options.Create(configuration), sender);
        for (var i = 0; i < 100; i++) Assert.True(await queue.TryEnqueueAsync($"person{i}@example.com", "en", default));
        Assert.False(await queue.TryEnqueueAsync("overflow@example.com", "en", default));
        configuration.Enabled = false;
        sender.FailIfChecked = true;
        Assert.False(await new PasswordRecoveryRequestQueue(Options.Create(configuration), sender)
            .TryEnqueueAsync("person@example.com", "en", default));
    }

    private sealed class TestRecoverySender : IPasswordRecoveryEmailSender
    {
        public bool FailIfChecked { get; set; }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
            FailIfChecked ? throw new InvalidOperationException("Disabled recovery must skip SMTP") : Task.FromResult(true);
        public Task SendAsync(string email, string language, Guid userId, string token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static PasswordRecoveryOptions Configured() => new()
    {
        Enabled = true, FrontendBaseUrl = "https://stocklab.example", SmtpHost = "smtp.example",
        FromEmail = "no-reply@example.com", Username = "smtp-user", Password = "test-secret"
    };
}
