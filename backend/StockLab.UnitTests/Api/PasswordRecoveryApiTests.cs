using System.Net;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Identity;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Api;

public sealed partial class UserLoginApiTests
{
    [Fact]
    public async Task Failed_email_delivery_keeps_worker_running_and_logs_no_address_token_or_credentials()
    {
        var sender = new RecordingRecoverySender(failFirst: true);
        var logger = new RecoveryWorkerLogger();
        await using var fixture = await RecoveryFixture(sender, workerLogger: logger);
        var firstId = await RegisterAsync(fixture, "First", "private-first@example.com", "original-password");
        var secondId = await RegisterAsync(fixture, "Second", "second@example.com", "original-password");
        using var firstRequest = await Forgot(fixture, "private-first@example.com");
        Assert.Equal(HttpStatusCode.Accepted, firstRequest.StatusCode);
        var failed = await sender.Next();
        using var secondRequest = await Forgot(fixture, "second@example.com", "fr");
        Assert.Equal(HttpStatusCode.Accepted, secondRequest.StatusCode);
        var delivered = await sender.Next();
        Assert.Equal(firstId, failed.UserId);
        Assert.Equal(secondId, delivered.UserId);
        Assert.Equal("fr", delivered.Language);
        var log = Assert.Single(logger.Messages);
        Assert.DoesNotContain("private-first@example.com", log);
        Assert.DoesNotContain(failed.Token, log);
        Assert.DoesNotContain("private-smtp-password", log);
        using var scope = fixture.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.AsNoTracking()
            .SingleAsync(user => user.Id == firstId);
        Assert.NotNull(stored.PasswordResetTokenHash);
        Assert.Equal(0, stored.SessionVersion);
        await LoginAndReadTokenAsync(fixture, "private-first@example.com", "original-password");
    }

    [Fact]
    public async Task Recovery_delivers_localized_token_then_reset_persists_password_revokes_session_and_consumes_token()
    {
        var sender = new RecordingRecoverySender();
        await using var fixture = await RecoveryFixture(sender);
        var id = await RegisterAsync(fixture, "Recovery", "known@example.com", "original-password");
        var accessToken = await LoginAndReadTokenAsync(fixture, "known@example.com", "original-password");
        using var known = await Forgot(fixture, "  KNOWN@example.com  ", "fr");
        using var unknown = await Forgot(fixture, "unknown@example.com", "fr");
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal("", await known.Content.ReadAsStringAsync());
        Assert.Equal("", await unknown.Content.ReadAsStringAsync());
        var delivery = await sender.Next();
        Assert.Equal(id, delivery.UserId);
        Assert.Equal("known@example.com", delivery.Email);
        Assert.Equal("fr", delivery.Language);
        Assert.Matches("^[a-f0-9]{64}$", delivery.Token);
        using (var scope = fixture.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.AsNoTracking().SingleAsync();
            Assert.Equal(SHA256.HashData(Encoding.ASCII.GetBytes(delivery.Token)), user.PasswordResetTokenHash);
            Assert.Equal(fixture.ExpectedUtcNow.UtcDateTime.AddMinutes(30), user.PasswordResetExpiresAtUtc);
        }
        using var response = await Reset(fixture, id, delivery.Token);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        using var replay = await Reset(fixture, id, delivery.Token);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        using var protectedRequest = new HttpRequestMessage(HttpMethod.Get, "/tests/auth-protected");
        protectedRequest.Headers.Authorization = new("Bearer", accessToken);
        using var staleSession = await fixture.Client.SendAsync(protectedRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, staleSession.StatusCode);
        await LoginAndReadTokenAsync(fixture, "known@example.com", "replacement-password");
        using var oldPassword = await fixture.Client.PostAsync("/api/auth/login", Json("{\"email\":\"known@example.com\",\"password\":\"original-password\"}"));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        using var verifier = fixture.CreateScope();
        var stored = await verifier.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.AsNoTracking().SingleAsync();
        Assert.Null(stored.PasswordResetTokenHash);
        Assert.Null(stored.PasswordResetExpiresAtUtc);
        Assert.Equal(1, stored.SessionVersion);
    }

    [Fact]
    public async Task Expired_tampered_and_other_users_tokens_cannot_change_password()
    {
        var sender = new RecordingRecoverySender();
        await using var fixture = await RecoveryFixture(sender);
        var id = await RegisterAsync(fixture, "Recovery", "known@example.com", "original-password");
        using var forgot = await Forgot(fixture, "known@example.com");
        var delivery = await sender.Next();
        foreach (var attempt in new[] { (Guid.NewGuid(), delivery.Token), (id, new string('a', 64)) })
        {
            using var invalid = await Reset(fixture, attempt.Item1, attempt.Item2);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            var user = await db.Users.SingleAsync();
            user.PasswordResetExpiresAtUtc = fixture.ExpectedUtcNow.UtcDateTime;
            await db.SaveChangesAsync();
        }
        using var expired = await Reset(fixture, id, delivery.Token);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        await LoginAndReadTokenAsync(fixture, "known@example.com", "original-password");
    }

    [Theory]
    [InlineData("email")]
    [InlineData("password")]
    public async Task Email_and_authenticated_password_changes_invalidate_issued_reset_tokens(string change)
    {
        var sender = new RecordingRecoverySender();
        await using var fixture = await RecoveryFixture(sender);
        var id = await RegisterAsync(fixture, "Recovery", "known@example.com", "original-password");
        using var forgot = await Forgot(fixture, "known@example.com");
        var delivery = await sender.Next();
        using (var scope = fixture.CreateScope())
        {
            if (change == "email")
                await scope.ServiceProvider.GetRequiredService<IUserProfileService>().UpdateAsync(id, "Recovery", "changed@example.com", default);
            else
                await scope.ServiceProvider.GetRequiredService<IUserPasswordService>().ChangeAsync(id, "original-password", "authenticated-replacement", default);
        }
        using var invalid = await Reset(fixture, id, delivery.Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Failed_reset_persistence_returns_error_and_preserves_existing_password_token_and_session()
    {
        var sender = new RecordingRecoverySender();
        var failure = new FailRecoveryPasswordSaveInterceptor();
        await using var fixture = await RecoveryFixture(sender, failure);
        var id = await RegisterAsync(fixture, "Recovery", "known@example.com", "original-password");
        using var forgot = await Forgot(fixture, "known@example.com");
        var delivery = await sender.Next();
        failure.Fail = true;
        using var failed = await Reset(fixture, id, delivery.Token);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        using (var scope = fixture.CreateScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.AsNoTracking().SingleAsync();
            Assert.NotNull(stored.PasswordResetTokenHash);
            Assert.Equal(0, stored.SessionVersion);
        }
        await LoginAndReadTokenAsync(fixture, "known@example.com", "original-password");
        failure.Fail = false;
        using var retried = await Reset(fixture, id, delivery.Token);
        Assert.Equal(HttpStatusCode.NoContent, retried.StatusCode);
    }

    [Fact]
    public async Task Recovery_and_reset_have_independent_ip_limits_and_login_remains_separate()
    {
        await using var fixture = await LoginFixture.CreateAsync();
        for (var index = 0; index < 10; index++)
        {
            using var forgot = await Forgot(fixture, "unknown@example.com");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, forgot.StatusCode);
            using var reset = await Reset(fixture, Guid.NewGuid(), new string('a', 64));
            Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        }
        using var limitedForgot = await Forgot(fixture, "unknown@example.com");
        using var limitedReset = await Reset(fixture, Guid.NewGuid(), new string('a', 64));
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedForgot.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedReset.StatusCode);
        Assert.InRange(limitedForgot.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 900);
        using var login = await fixture.Client.PostAsync("/api/auth/login", Json("{\"email\":\"unknown@example.com\",\"password\":\"original-password\"}"));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Recovery_cooldown_suppresses_floods_and_a_later_request_replaces_the_previous_token()
    {
        var sender = new RecordingRecoverySender();
        await using var fixture = await RecoveryFixture(sender);
        var id = await RegisterAsync(fixture, "Recovery", "known@example.com", "original-password");
        async Task Process()
        {
            using var scope = fixture.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PasswordRecoveryRequestProcessor>()
                .ProcessAsync(new("known@example.com", "en"), default);
        }
        await Process();
        var first = await sender.Next();
        await Process();
        Assert.False(sender.HasDelivery);
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            var user = await db.Users.SingleAsync();
            user.PasswordResetRequestedAtUtc = fixture.ExpectedUtcNow.UtcDateTime.AddSeconds(-60);
            await db.SaveChangesAsync();
        }
        await Process();
        var second = await sender.Next();
        Assert.NotEqual(first.Token, second.Token);
        using var old = await Reset(fixture, id, first.Token);
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        using var current = await Reset(fixture, id, second.Token);
        Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
    }

    [Theory]
    [InlineData("PasswordRecovery:SmtpHost", "")]
    [InlineData("PasswordRecovery:FromEmail", "invalid")]
    [InlineData("PasswordRecovery:Security", "Auto")]
    [InlineData("PasswordRecovery:TokenLifetimeMinutes", "61")]
    public async Task Enabled_invalid_recovery_configuration_prevents_startup(string key, string value)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => LoginFixture.CreateAsync(extraConfiguration: new()
        {
            ["PasswordRecovery:Enabled"] = "true", ["PasswordRecovery:FrontendBaseUrl"] = "https://stocklab.example",
            ["PasswordRecovery:SmtpHost"] = "smtp.example", ["PasswordRecovery:FromEmail"] = "no-reply@example.com",
            [key] = value
        }));
    }

    [Fact]
    public async Task Disabled_recovery_returns_identical_unavailable_for_known_and_unknown_accounts()
    {
        await using var fixture = await LoginFixture.CreateAsync();
        await RegisterAsync(fixture, "Recovery", "known@example.com", "original-password");
        using var known = await fixture.Client.PostAsync("/api/auth/password/forgot",
            Json("{\"email\":\"known@example.com\",\"language\":\"en\"}"));
        using var unknown = await fixture.Client.PostAsync("/api/auth/password/forgot",
            Json("{\"email\":\"unknown@example.com\",\"language\":\"fr\"}"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await known.Content.ReadAsStringAsync());
        Assert.Equal("password_recovery_unavailable", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Forgot_does_not_resolve_database_backed_reset_service_before_queueing()
    {
        await using var fixture = await LoginFixture.CreateAsync(configureServices: services =>
        {
            services.RemoveAll<IUserPasswordRecoveryService>();
            services.AddScoped<IUserPasswordRecoveryService>(_ => throw new InvalidOperationException("Database unavailable"));
        });
        using var response = await Forgot(fixture, "known@example.com");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("{}", "Email")]
    [InlineData("{\"email\":\"invalid\",\"language\":\"en\"}", "Email")]
    public async Task Recovery_rejects_invalid_email_using_standard_validation(string payload, string field)
    {
        await using var fixture = await LoginFixture.CreateAsync();
        using var response = await fixture.Client.PostAsync("/api/auth/password/forgot", Json(payload));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_error", body.RootElement.GetProperty("error").GetString());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Reset_rejects_unknown_user_and_malformed_tokens_with_same_safe_error()
    {
        await using var fixture = await LoginFixture.CreateAsync();
        foreach (var token in new[] { new string('a', 64), "invalid", "", new string('A', 64) })
        {
            using var response = await fixture.Client.PostAsync("/api/auth/password/reset", Json(JsonSerializer.Serialize(new
            {
                userId = Guid.NewGuid(), token, newPassword = "replacement-password", confirmPassword = "replacement-password"
            })));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("invalid_reset_token", body.RootElement.GetProperty("error").GetString());
        }
    }

    [Theory]
    [InlineData("short", "short", "NewPassword")]
    [InlineData("replacement-password", "different-password", "ConfirmPassword")]
    public async Task Reset_validates_password_strength_and_confirmation(string password, string confirmation, string field)
    {
        await using var fixture = await LoginFixture.CreateAsync();
        using var response = await fixture.Client.PostAsync("/api/auth/password/reset", Json(JsonSerializer.Serialize(new
        {
            userId = Guid.NewGuid(), token = new string('a', 64), newPassword = password, confirmPassword = confirmation
        })));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_error", body.RootElement.GetProperty("error").GetString());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }

    private static Task<LoginFixture> RecoveryFixture(RecordingRecoverySender sender, SaveChangesInterceptor? interceptor = null,
        ILogger<PasswordRecoveryWorker>? workerLogger = null) =>
        LoginFixture.CreateAsync(saveChangesInterceptor: interceptor, extraConfiguration: new()
        {
            ["PasswordRecovery:Enabled"] = "true", ["PasswordRecovery:FrontendBaseUrl"] = "http://127.0.0.1:5189/",
            ["PasswordRecovery:SmtpHost"] = "127.0.0.1", ["PasswordRecovery:SmtpPort"] = "1025",
            ["PasswordRecovery:FromEmail"] = "no-reply@example.com", ["PasswordRecovery:Security"] = "None"
        }, configureServices: services =>
        {
            services.RemoveAll<IPasswordRecoveryEmailSender>();
            services.AddSingleton<IPasswordRecoveryEmailSender>(sender);
            if (workerLogger is not null) services.AddSingleton(workerLogger);
        });

    private static Task<HttpResponseMessage> Forgot(LoginFixture fixture, string email, string language = "en") =>
        fixture.Client.PostAsync("/api/auth/password/forgot", Json(JsonSerializer.Serialize(new { email, language })));
    private static Task<HttpResponseMessage> Reset(LoginFixture fixture, Guid userId, string token) =>
        fixture.Client.PostAsync("/api/auth/password/reset", Json(JsonSerializer.Serialize(new
        {
            userId, token, newPassword = "replacement-password", confirmPassword = "replacement-password"
        })));

    private sealed record RecoveryDelivery(string Email, string Language, Guid UserId, string Token);
    private sealed class RecordingRecoverySender(bool failFirst = false) : IPasswordRecoveryEmailSender
    {
        private readonly Channel<RecoveryDelivery> deliveries = Channel.CreateUnbounded<RecoveryDelivery>();
        private int attempts;
        public bool HasDelivery => deliveries.Reader.TryPeek(out _);
        public async Task SendAsync(string email, string language, Guid userId, string token, CancellationToken cancellationToken)
        {
            await deliveries.Writer.WriteAsync(new(email, language, userId, token), cancellationToken);
            if (failFirst && Interlocked.Increment(ref attempts) == 1)
                throw new InvalidOperationException($"SMTP failed for {email}: {token}; credential=private-smtp-password");
        }
        public Task<RecoveryDelivery> Next() => deliveries.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class RecoveryWorkerLogger : ILogger<PasswordRecoveryWorker>
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception) + exception);
    }

    private sealed class FailRecoveryPasswordSaveInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fail && eventData.Context!.ChangeTracker.Entries<User>().Any(entry =>
                    entry.State == EntityState.Modified && entry.Property(user => user.PasswordHash).IsModified))
                throw new DbUpdateException("private failure marker");
            return ValueTask.FromResult(result);
        }
    }
}
