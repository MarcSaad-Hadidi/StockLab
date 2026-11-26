using System.Net;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Identity;
using StockLab.Infrastructure.Persistence;
using StockLab.UnitTests.Identity;

namespace StockLab.UnitTests.Api;

public sealed partial class UserLoginApiTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("different-signing-key", false)]
    [InlineData("audience", false)]
    [InlineData("expired", false)]
    [InlineData("unverified-email", false)]
    [InlineData("authorized-party", false)]
    public async Task Public_google_endpoint_uses_real_code_exchange_and_crypto_before_creating_a_session(string? fault, bool success)
    {
        using var signed = new GoogleIdentityProviderTests.SignedTokenFixture();
        var transport = new GoogleIdentityProviderTests.TestOnlyTokenHandler(JsonSerializer.Serialize(new
        { id_token = signed.Token(fault), access_token = "discard-this-access-token", refresh_token = "discard-this-refresh-token" }));
        await using var fixture = await LoginFixture.CreateAsync(extraConfiguration: GoogleConfiguration(), configureServices: services =>
        {
            // Substitute only Google's HTTP response and public keys inside this isolated test host.
            // The production IGoogleIdentityProvider, exchange, validator, account service and JWT path all run.
            services.RemoveAll<GoogleIdTokenValidator>();
            services.AddSingleton(signed.Validator);
            services.AddHttpClient("GoogleOAuth").ConfigurePrimaryHttpMessageHandler(() => transport);
        });
        using var response = await GooglePost(fixture, "exchange", new { code = "private-auth-code", language = "fr" });
        Assert.Equal(success ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("discard-this-access-token", body);
        Assert.DoesNotContain("discard-this-refresh-token", body);
        using var parsed = JsonDocument.Parse(body);
        Assert.Equal(success, parsed.RootElement.TryGetProperty("accessToken", out _));
        if (!success) await AssertGoogleError(response, "google_invalid_response");
        using var scope = fixture.CreateScope();
        Assert.Equal(success ? 1 : 0, await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task Disabled_google_discovery_and_posts_do_not_resolve_database_or_identity_dependencies()
    {
        await using var fixture = await LoginFixture.CreateAsync(extraConfiguration: new() { ["ConnectionStrings:StockLab"] = null },
            configureServices: services =>
            {
                services.RemoveAll<IGoogleAuthenticationService>();
                services.AddScoped<IGoogleAuthenticationService>(_ => throw new InvalidOperationException("Google database must not be resolved while disabled."));
            });
        using var config = await fixture.Client.GetAsync("/api/auth/google/config?origin=https%3A%2F%2Ffrontend.example.test");
        Assert.Equal(HttpStatusCode.OK, config.StatusCode);
        using var exchange = await GooglePost(fixture, "exchange", new { code = "code", language = "en" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exchange.StatusCode);
        using var link = await GooglePost(fixture, "link", new { linkToken = new string('a', 64), password = "password", language = "en" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, link.StatusCode);
    }

    [Fact]
    public async Task Disabled_google_auth_exposes_no_client_and_never_authenticates_a_fake_code()
    {
        await using var fixture = await LoginFixture.CreateAsync();
        using var config = await fixture.Client.GetAsync("/api/auth/google/config?origin=https%3A%2F%2Ffrontend.example.test");
        Assert.Equal(HttpStatusCode.OK, config.StatusCode);
        Assert.Equal("{\"enabled\":false}", await config.Content.ReadAsStringAsync());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/google/exchange")
        { Content = Json("{\"code\":\"test-google-code\",\"language\":\"fr\"}") };
        request.Headers.Add("Origin", "https://frontend.example.test");
        request.Headers.Add("X-Requested-With", "StockLab.GoogleOAuth");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("google_unavailable", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Google_new_user_gets_normal_portfolio_nullable_password_language_and_revocable_jwt()
    {
        await using var fixture = await GoogleFixture(new("subject-new", "person@external.test", "Google Person"));
        using var exchange = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "fr" });
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        using var json = JsonDocument.Parse(await exchange.Content.ReadAsStringAsync());
        Assert.Equal("Bearer", json.RootElement.GetProperty("tokenType").GetString());
        Assert.Equal("fr", json.RootElement.GetProperty("user").GetProperty("preferredLanguage").GetString());
        var token = json.RootElement.GetProperty("accessToken").GetString();
        fixture.Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var protectedResult = await fixture.Client.GetAsync("/tests/auth-protected");
        Assert.Equal(HttpStatusCode.OK, protectedResult.StatusCode);
        using var passwordChange = await fixture.Client.PutAsync("/api/auth/password", Json(JsonSerializer.Serialize(new
        { currentPassword = "anything", newPassword = "replacement-password", confirmPassword = "replacement-password" })));
        Assert.Equal(HttpStatusCode.BadRequest, passwordChange.StatusCode);
        await AssertGoogleError(passwordChange, "password_not_set");
        using var passwordLogin = await fixture.Client.PostAsync("/api/auth/login", Json("{\"email\":\"person@external.test\",\"password\":\"anything\"}"));
        Assert.Equal(HttpStatusCode.Unauthorized, passwordLogin.StatusCode);
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var user = await db.Users.Include(value => value.Portfolio).SingleAsync();
        Assert.Null(user.PasswordHash);
        Assert.Equal("subject-new", user.GoogleSubject);
        Assert.Equal("USD", user.Portfolio!.Currency);
        Assert.Equal(100_000m, user.Portfolio.CashBalance);
        Assert.Equal(100_000m, user.Portfolio.InitialCapital);
        user.SessionVersion++;
        await db.SaveChangesAsync();
        using var revoked = await fixture.Client.GetAsync("/tests/auth-protected");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task Google_subject_preserves_existing_local_profile_language_email_and_one_portfolio()
    {
        var provider = new TestOnlyGoogleProvider(new("stable-subject", "first@gmail.com", "Google Name"));
        await using var fixture = await GoogleFixture(provider: provider);
        using var first = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "fr" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            var user = await db.Users.SingleAsync();
            user.Email = "local@example.test"; user.NormalizedEmail = "LOCAL@EXAMPLE.TEST"; user.DisplayName = "Local Name";
            await db.SaveChangesAsync();
        }
        provider.Identity = new("stable-subject", "new-google@gmail.com", "Changed Google Name");
        using var again = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "en" });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        using var body = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
        var identity = body.RootElement.GetProperty("user");
        Assert.Equal("local@example.test", identity.GetProperty("email").GetString());
        Assert.Equal("Local Name", identity.GetProperty("displayName").GetString());
        Assert.Equal("fr", identity.GetProperty("preferredLanguage").GetString());
        using var verify = fixture.CreateScope();
        var stored = verify.ServiceProvider.GetRequiredService<StockLabDbContext>();
        Assert.Equal(1, await stored.Users.CountAsync());
        Assert.Equal(1, await stored.Portfolios.CountAsync());
    }

    [Fact]
    public async Task Existing_email_requires_password_proof_then_links_once_and_keeps_existing_account_data()
    {
        await using var fixture = await GoogleFixture();
        var id = await RegisterAsync(fixture, "Existing", "PERSON@example.test", "existing-password");
        using var exchange = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "fr" });
        Assert.Equal(HttpStatusCode.Conflict, exchange.StatusCode);
        var ticket = await ReadGoogleTicket(exchange);
        using (var scope = fixture.CreateScope())
            Assert.Null((await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.SingleAsync()).GoogleSubject);
        using var wrong = await GooglePost(fixture, "link", new { linkToken = ticket, password = "wrong-password", language = "fr" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        await AssertGoogleError(wrong, "invalid_credentials");
        using var linked = await GooglePost(fixture, "link", new { linkToken = ticket, password = "existing-password", language = "fr" });
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        using var body = JsonDocument.Parse(await linked.Content.ReadAsStringAsync());
        Assert.Equal(id, body.RootElement.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal("en", body.RootElement.GetProperty("user").GetProperty("preferredLanguage").GetString());
        using var replay = await GooglePost(fixture, "link", new { linkToken = ticket, password = "existing-password", language = "fr" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        await AssertGoogleError(replay, "google_link_expired");
        using var verify = fixture.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<StockLabDbContext>();
        Assert.Equal(1, await db.Portfolios.CountAsync());
        Assert.Equal("test-only-subject", (await db.Users.SingleAsync()).GoogleSubject);
        await LoginAndReadTokenAsync(fixture, "person@example.test", "existing-password");
    }

    [Theory]
    [InlineData("password")]
    [InlineData("email")]
    [InlineData("session")]
    public async Task Outstanding_google_link_proof_is_invalidated_by_account_changes(string change)
    {
        await using var fixture = await GoogleFixture();
        var id = await RegisterAsync(fixture, "Existing", "person@example.test", "existing-password");
        using var exchange = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "en" });
        var ticket = await ReadGoogleTicket(exchange);
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            if (change == "password")
                await scope.ServiceProvider.GetRequiredService<IUserPasswordService>().ChangeAsync(id, "existing-password", "replacement-password", default);
            else if (change == "email")
                await scope.ServiceProvider.GetRequiredService<IUserProfileService>().UpdateAsync(id, "Existing", "other@example.test", default);
            else { var user = await db.Users.SingleAsync(); user.SessionVersion++; await db.SaveChangesAsync(); }
        }
        using var linked = await GooglePost(fixture, "link", new { linkToken = ticket, password = "existing-password", language = "en" });
        Assert.Equal(HttpStatusCode.BadRequest, linked.StatusCode);
        await AssertGoogleError(linked, "google_link_expired");
    }

    [Fact]
    public async Task Google_ticket_rejects_a_sixth_guess_even_with_the_right_password()
    {
        await using var fixture = await GoogleFixture();
        await RegisterAsync(fixture, "Existing", "person@example.test", "existing-password");
        using var exchange = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "en" });
        var ticket = await ReadGoogleTicket(exchange);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await GooglePost(fixture, "link", new { linkToken = ticket, password = "wrong", language = "en" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }
        using var sixth = await GooglePost(fixture, "link", new { linkToken = ticket, password = "existing-password", language = "en" });
        Assert.Equal(HttpStatusCode.BadRequest, sixth.StatusCode);
        await AssertGoogleError(sixth, "google_link_expired");
    }

    [Fact]
    public async Task Google_link_ticket_expires_after_five_minutes_even_with_the_correct_password()
    {
        var clock = new TestOnlyGoogleClock(DateTimeOffset.UtcNow);
        await using var fixture = await GoogleFixture(timeProvider: clock);
        await RegisterAsync(fixture, "Existing", "person@example.test", "existing-password");
        using var exchange = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "en" });
        var ticket = await ReadGoogleTicket(exchange);
        clock.Now = clock.Now.AddMinutes(5);
        using var expired = await GooglePost(fixture, "link", new { linkToken = ticket, password = "existing-password", language = "en" });
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        await AssertGoogleError(expired, "google_link_expired");
        using var scope = fixture.CreateScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.SingleAsync()).GoogleSubject);
    }

    [Fact]
    public async Task Google_link_ticket_is_bound_to_original_origin_even_when_both_origins_are_allowed()
    {
        var config = GoogleConfiguration(); config["GoogleAuth:AllowedOrigins:1"] = "https://second.example.test";
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100, extraConfiguration: config, configureServices: services =>
        {
            services.RemoveAll<IGoogleIdentityProvider>();
            services.AddSingleton<IGoogleIdentityProvider>(new TestOnlyGoogleProvider(new("test-only-subject", "person@example.test", "Person")));
        });
        await RegisterAsync(fixture, "Existing", "person@example.test", "existing-password");
        using var exchange = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "en" });
        var ticket = await ReadGoogleTicket(exchange);
        using var wrongOrigin = await GooglePost(fixture, "link", new { linkToken = ticket, password = "existing-password", language = "en" }, "https://second.example.test");
        Assert.Equal(HttpStatusCode.BadRequest, wrongOrigin.StatusCode);
        await AssertGoogleError(wrongOrigin, "google_link_expired");
        using var correctOrigin = await GooglePost(fixture, "link", new { linkToken = ticket, password = "existing-password", language = "en" });
        Assert.Equal(HttpStatusCode.OK, correctOrigin.StatusCode);
    }

    [Fact]
    public async Task Existing_email_bound_to_another_google_subject_is_never_overwritten()
    {
        await using var fixture = await GoogleFixture();
        using var created = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "en" });
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            var user = await db.Users.SingleAsync(); user.GoogleSubject = "different-subject"; await db.SaveChangesAsync();
        }
        using var collision = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "en" });
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        await AssertGoogleError(collision, "google_account_conflict");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("https://evil.example.test", true)]
    [InlineData("https://frontend.example.test/", true)]
    [InlineData("https://frontend.example.test", false)]
    public async Task Google_exchange_requires_exact_allowed_origin_and_custom_header(string? origin, bool header)
    {
        await using var fixture = await GoogleFixture();
        using var response = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "en" }, origin, header);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertGoogleError(response, "validation_error");
        using var scope = fixture.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.AnyAsync());
    }

    [Fact]
    public async Task Google_config_returns_client_id_only_to_allowlisted_origin()
    {
        await using var fixture = await GoogleFixture();
        using var allowed = await fixture.Client.GetAsync("/api/auth/google/config?origin=https%3A%2F%2Ffrontend.example.test");
        Assert.Equal("no-store", allowed.Headers.CacheControl?.ToString());
        var json = await allowed.Content.ReadAsStringAsync();
        Assert.Contains("123-test.apps.googleusercontent.com", json);
        Assert.DoesNotContain("test-client-secret", json);
        using var denied = await fixture.Client.GetAsync("/api/auth/google/config?origin=https%3A%2F%2Fevil.test");
        Assert.Equal("{\"enabled\":false}", await denied.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("{}", "code")]
    [InlineData("{\"code\":\"code\",\"language\":\"de\"}", "language")]
    public async Task Google_exchange_uses_global_field_validation(string payload, string field)
    {
        await using var fixture = await GoogleFixture();
        using var response = await fixture.Client.PostAsync("/api/auth/google/exchange", Json(payload));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(field, json.RootElement.GetProperty("errors").EnumerateObject().Select(value => value.Name), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Google_only_recovery_sets_real_password_disconnects_google_and_revokes_old_session()
    {
        await using var smtp = new RecoverySmtpServer();
        await using var fixture = await GoogleFixture(recoveryConfiguration: SmtpRecoveryConfiguration(smtp.Port));
        using var created = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "en" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString();
        using var passwordBeforeRecovery = await fixture.Client.PostAsync("/api/auth/login",
            Json("{\"email\":\"person@example.test\",\"password\":\"replacement-password\"}"));
        Assert.Equal(HttpStatusCode.Unauthorized, passwordBeforeRecovery.StatusCode);

        using var known = await Forgot(fixture, "person@example.test", "fr");
        using var unknown = await Forgot(fixture, "unknown@example.test", "fr");
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Empty(await known.Content.ReadAsStringAsync());
        Assert.Empty(await unknown.Content.ReadAsStringAsync());
        var message = await smtp.NextMessageAsync();
        Assert.Equal("person@example.test", Assert.Single(message.To.Mailboxes).Address);
        Assert.Equal("Réinitialisez votre mot de passe StockLab", message.Subject);
        var text = Assert.IsType<string>(message.TextBody);
        var linkLine = text.Split('\n').Select(line => line.Trim()).Single(line => line.StartsWith("http", StringComparison.Ordinal));
        var link = new Uri(linkLine);
        Assert.Equal("http://127.0.0.1:5189/reset-password", link.GetLeftPart(UriPartial.Path));
        Assert.Empty(link.Query);
        var parameters = QueryHelpers.ParseQuery(link.Fragment[1..]);
        using var reset = await Reset(fixture, Guid.Parse(parameters["userId"].ToString()), parameters["token"].ToString());
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(1, smtp.RecipientCount);

        fixture.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var rejected = await fixture.Client.GetAsync("/tests/auth-protected");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        using var retryGoogle = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "en" });
        Assert.Equal(HttpStatusCode.Conflict, retryGoogle.StatusCode);
        await ReadGoogleTicket(retryGoogle);
        using var verify = fixture.CreateScope();
        var stored = await verify.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.SingleAsync();
        Assert.Null(stored.GoogleSubject);
        Assert.NotNull(stored.PasswordHash);
        Assert.Equal(1, stored.SessionVersion);
        await LoginAndReadTokenAsync(fixture, "person@example.test", "replacement-password");
    }

    [Fact]
    public async Task Google_exchange_rate_limit_returns_retry_after_without_creating_more_accounts()
    {
        await using var fixture = await GoogleFixture();
        for (var i = 0; i < 10; i++)
        {
            using var response = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "en" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var limited = await GooglePost(fixture, "exchange", new { code = "provider-code", language = "en" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        await AssertGoogleError(limited, "too_many_requests");
    }

    private static Task<LoginFixture> GoogleFixture(GoogleIdentity? identity = null, TestOnlyGoogleProvider? provider = null,
        TimeProvider? timeProvider = null, Dictionary<string, string?>? recoveryConfiguration = null)
    {
        var settings = GoogleConfiguration();
        if (recoveryConfiguration is not null)
            foreach (var pair in recoveryConfiguration) settings.Add(pair.Key, pair.Value);
        return LoginFixture.CreateAsync(loginPermitLimit: 100, timeProvider: timeProvider, extraConfiguration: settings, configureServices: services =>
        {
            // Isolated test assembly only. Production registration always uses the real Google transport and validator.
            services.RemoveAll<IGoogleIdentityProvider>();
            services.AddSingleton<IGoogleIdentityProvider>(provider ?? new TestOnlyGoogleProvider(identity ?? new("test-only-subject", "person@example.test", "Test Person")));
        });
    }

    private static Dictionary<string, string?> GoogleConfiguration() => new()
    {
        ["GoogleAuth:Enabled"] = "true", ["GoogleAuth:ClientId"] = "123-test.apps.googleusercontent.com",
        ["GoogleAuth:ClientSecret"] = "test-client-secret", ["GoogleAuth:AllowedOrigins:0"] = "https://frontend.example.test"
    };

    private static async Task<HttpResponseMessage> GooglePost(LoginFixture fixture, string route, object payload,
        string? origin = "https://frontend.example.test", bool header = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/google/" + route) { Content = Json(JsonSerializer.Serialize(payload)) };
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (header) request.Headers.Add("X-Requested-With", "StockLab.GoogleOAuth");
        return await fixture.Client.SendAsync(request);
    }

    private static async Task<string> ReadGoogleTicket(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("google_link_required", body.RootElement.GetProperty("error").GetString());
        Assert.False(body.RootElement.TryGetProperty("accessToken", out _));
        return body.RootElement.GetProperty("linkToken").GetString()!;
    }

    private static async Task AssertGoogleError(HttpResponseMessage response, string error)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(error, body.RootElement.GetProperty("error").GetString());
    }

    private sealed class TestOnlyGoogleProvider(GoogleIdentity identity) : IGoogleIdentityProvider
    {
        public GoogleIdentity Identity { get; set; } = identity;
        public Task<GoogleIdentity> ExchangeAsync(string code, string origin, CancellationToken cancellationToken) =>
            code == "provider-code" && origin == "https://frontend.example.test"
                ? Task.FromResult(Identity) : throw new GoogleAuthenticationException("google_invalid_response");
    }

    private sealed class TestOnlyGoogleClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
