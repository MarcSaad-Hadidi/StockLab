using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using StockLab.Infrastructure.Persistence;
using StockLab.Domain.Entities;

namespace StockLab.UnitTests.Api;

public sealed partial class UserLoginApiTests
{
    [Fact]
    public async Task Password_change_revokes_every_old_session_but_preserves_other_accounts_and_fresh_login()
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100);
        await RegisterAsync(fixture, "Session owner", "sessions@example.com", "old-password");
        await RegisterAsync(fixture, "Other owner", "other-session@example.com", "other-password");
        var first = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "old-password");
        var second = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "old-password");
        var other = await LoginAndReadTokenAsync(fixture, "other-session@example.com", "other-password");
        using (var before = await SessionGetAsync(fixture, first)) Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using (var changed = await SessionPasswordAsync(fixture, first, "old-password", "new-password"))
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        foreach (var token in new[] { first, second })
        foreach (var path in new[] { "/tests/auth-protected", "/api/profile", "/api/portfolio", "/api/portfolio/transactions/history", "/api/watchlist", "/api/alerts", "/api/ai-trader/overview" })
        {
            using var rejected = await SessionGetAsync(fixture, token, path);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.Equal(UnauthorizedJson, await rejected.Content.ReadAsStringAsync());
        }
        using (var unchanged = await SessionGetAsync(fixture, other)) Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        using (var oldPassword = await fixture.Client.PostAsync("/api/auth/login", Json("{\"email\":\"sessions@example.com\",\"password\":\"old-password\"}")))
            Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        var fresh = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "new-password");
        using var accepted = await SessionGetAsync(fixture, fresh);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var again = await SessionPasswordAsync(fixture, first, "new-password", "another-password");
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        using var secondChange = await SessionPasswordAsync(fixture, fresh, "new-password", "third-password");
        Assert.Equal(HttpStatusCode.NoContent, secondChange.StatusCode);
        using var previousGeneration = await SessionGetAsync(fixture, fresh);
        Assert.Equal(HttpStatusCode.Unauthorized, previousGeneration.StatusCode);
        var latest = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "third-password");
        using var latestSession = await SessionGetAsync(fixture, latest);
        Assert.Equal(HttpStatusCode.OK, latestSession.StatusCode);
    }

    [Theory]
    [InlineData("wrong-password", false)]
    [InlineData("old-password", true)]
    public async Task A_rejected_password_change_does_not_revoke_the_valid_session(string currentPassword, bool failPersistence)
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100,
            saveChangesInterceptor: failPersistence ? new PasswordSaveFailureInterceptor(false) : null);
        await RegisterAsync(fixture, "Session owner", "sessions@example.com", "old-password");
        var token = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "old-password");
        using var rejected = await SessionPasswordAsync(fixture, token, currentPassword, "new-password");
        Assert.Equal(failPersistence ? HttpStatusCode.InternalServerError : HttpStatusCode.BadRequest, rejected.StatusCode);
        using var stillValid = await SessionGetAsync(fixture, token);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    [Fact]
    public async Task Ordinary_profile_and_language_updates_do_not_revoke_sessions()
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100);
        await RegisterAsync(fixture, "Session owner", "sessions@example.com", "old-password");
        var token = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "old-password");
        fixture.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var profile = await fixture.Client.PutAsync("/api/profile", Json("{\"displayName\":\"Renamed\",\"email\":\"renamed@example.com\"}"));
        using var language = await fixture.Client.PutAsync("/api/profile/language", Json("{\"preferredLanguage\":\"fr\"}"));
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal(HttpStatusCode.OK, language.StatusCode);
        using var stillValid = await SessionGetAsync(fixture, token);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    [Fact]
    public async Task A_login_verified_before_revocation_cannot_adopt_the_new_session_generation()
    {
        var hasher = new RevocationDuringVerificationHasher();
        await using var fixture = await LoginFixture.CreateAsync(passwordHasher: hasher, loginPermitLimit: 100);
        await RegisterAsync(fixture, "Session owner", "sessions@example.com", "old-password");
        hasher.AfterVerification = () =>
        {
            using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            var nextHash = hasher.HashPassword(new User(), "new-password");
            // Another request commits a password change after this login loaded/verified its old snapshot.
            db.Users.ExecuteUpdate(update => update.SetProperty(user => user.PasswordHash, nextHash)
                .SetProperty(user => user.SessionVersion, user => user.SessionVersion + 1));
        };
        var stale = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "old-password");
        using var rejected = await SessionGetAsync(fixture, stale);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        var fresh = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "new-password");
        using var accepted = await SessionGetAsync(fixture, fresh);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task Password_hash_upgrades_do_not_revoke_an_existing_session()
    {
        await using var fixture = await LoginFixture.CreateAsync(passwordHasher: new RehashingPasswordHasher(), loginPermitLimit: 100);
        await RegisterAsync(fixture, "Session owner", "sessions@example.com", "old-password");
        var first = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "old-password");
        var second = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "old-password");
        using var stillValid = await SessionGetAsync(fixture, first);
        using var alsoValid = await SessionGetAsync(fixture, second);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alsoValid.StatusCode);
    }

    [Fact]
    public async Task A_deleted_account_cannot_reuse_its_previously_valid_token()
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100);
        await RegisterAsync(fixture, "Deleted owner", "deleted-session@example.com", "old-password");
        var token = await LoginAndReadTokenAsync(fixture, "deleted-session@example.com", "old-password");
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            await db.Portfolios.ExecuteDeleteAsync();
            await db.Users.ExecuteDeleteAsync();
        }
        using var rejected = await SessionGetAsync(fixture, token);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [Fact]
    public async Task An_unavailable_session_store_fails_closed_without_reporting_an_invalid_session()
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100);
        await RegisterAsync(fixture, "Session owner", "sessions@example.com", "old-password");
        var token = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "old-password");
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            await db.Database.GetDbConnection().CloseAsync(); // Reopening this in-memory store loses its schema.
        }
        using var rejected = await SessionGetAsync(fixture, token);
        Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode);
        using var error = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal("internal_server_error", error.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/api/stocks/AAPL/quote")]
    public async Task Public_routes_remain_available_with_an_optional_bearer_header_during_a_session_store_outage(string path)
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100);
        await RegisterAsync(fixture, "Session owner", "sessions@example.com", "old-password");
        var token = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "old-password");
        using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Database.GetDbConnection().CloseAsync();
        using var anonymous = await fixture.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        using var optionalHeader = await SessionGetAsync(fixture, token, path);
        Assert.Equal(HttpStatusCode.OK, optionalHeader.StatusCode);
    }

    [Fact]
    public async Task A_fallback_authorization_policy_also_enforces_session_revocation()
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100);
        await RegisterAsync(fixture, "Session owner", "sessions@example.com", "old-password");
        var token = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "old-password");
        using (var scope = fixture.CreateScope())
        {
            // This test-only route has no authorization metadata; health probes are explicitly anonymous.
            scope.ServiceProvider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy =
                new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        }
        using var before = await SessionGetAsync(fixture, token, "/tests/fallback-protected");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using var changed = await SessionPasswordAsync(fixture, token, "old-password", "new-password");
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        using var rejected = await SessionGetAsync(fixture, token, "/tests/fallback-protected");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("malformed")]
    [InlineData("duplicate")]
    public async Task A_valid_signature_cannot_authorize_an_ambiguous_or_invalid_subject(string kind)
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100);
        var id = await RegisterAsync(fixture, "Session owner", "sessions@example.com", "old-password");
        var claims = new List<Claim> { new("session_version", "0") };
        if (kind != "missing") claims.Add(new(JwtRegisteredClaimNames.Sub,
            kind == "empty" ? Guid.Empty.ToString("D") : kind == "malformed" ? "not-an-id" : id.ToString("D")));
        if (kind == "duplicate") claims.Add(new(JwtRegisteredClaimNames.Sub, id.ToString("D")));
        var token = new JwtSecurityToken(TestIssuer, TestAudience, claims,
            fixture.ExpectedUtcNow.UtcDateTime.AddMinutes(-1), fixture.ExpectedUtcNow.UtcDateTime.AddMinutes(30),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)), SecurityAlgorithms.HmacSha256));
        using var rejected = await SessionGetAsync(fixture, new JwtSecurityTokenHandler().WriteToken(token));
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("1")]
    [InlineData("00")]
    [InlineData(" 0")]
    [InlineData("0.0")]
    [InlineData("9223372036854775808")]
    [InlineData("0,0")]
    public async Task Missing_malformed_duplicate_or_noncurrent_session_versions_cannot_authenticate(string? version)
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100);
        var id = await RegisterAsync(fixture, "Session owner", "sessions@example.com", "old-password");
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, id.ToString("D")) };
        if (version is not null)
            foreach (var item in version.Split(',')) claims.Add(new Claim("session_version", item));
        var token = new JwtSecurityToken(TestIssuer, TestAudience, claims,
            fixture.ExpectedUtcNow.UtcDateTime.AddMinutes(-1), fixture.ExpectedUtcNow.UtcDateTime.AddMinutes(30),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)), SecurityAlgorithms.HmacSha256));
        using var rejected = await SessionGetAsync(fixture, new JwtSecurityTokenHandler().WriteToken(token));
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal(UnauthorizedJson, await rejected.Content.ReadAsStringAsync());
        // Use the normal login contract as a legitimate control through the same boundary.
        var fresh = await LoginAndReadTokenAsync(fixture, "sessions@example.com", "old-password");
        using var accepted = await SessionGetAsync(fixture, fresh);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    private static async Task<HttpResponseMessage> SessionGetAsync(LoginFixture fixture, string token, string path = "/tests/auth-protected")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SessionPasswordAsync(LoginFixture fixture, string token, string currentPassword, string newPassword)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/auth/password");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = Json(JsonSerializer.Serialize(new { currentPassword, newPassword, confirmPassword = newPassword }));
        return await fixture.Client.SendAsync(request);
    }

    private sealed class RevocationDuringVerificationHasher : IPasswordHasher<User>
    {
        private readonly PasswordHasher<User> inner = new();
        public Action? AfterVerification { get; set; }
        public string HashPassword(User user, string password) => inner.HashPassword(user, password);
        public PasswordVerificationResult VerifyHashedPassword(User user, string hash, string password)
        {
            var result = inner.VerifyHashedPassword(user, hash, password);
            var callback = AfterVerification;
            AfterVerification = null;
            callback?.Invoke();
            return result;
        }
    }
}
