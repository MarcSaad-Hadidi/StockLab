using System.Net;
using System.Text.Json;

namespace StockLab.UnitTests.Api;

public sealed partial class UserLoginApiTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(61)]
    [InlineData(479)]
    public async Task Default_sessions_remain_usable_through_an_eight_hour_work_session(int elapsedMinutes)
    {
        // Issue through the real login service at an earlier instant. JWT validation still
        // uses the real current time, so no test-only lifetime validator can accept it.
        await using var fixture = await LoginFixture.CreateAsync(accessTokenMinutes: null,
            issuedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-elapsedMinutes));
        await RegisterAsync(fixture, "Session duration", "duration@example.test", "duration-password");
        using var login = await fixture.Client.PostAsync("/api/auth/login", Json("""
            {"email":"duration@example.test","password":"duration-password"}
            """));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("accessToken").GetString()!;
        var claims = ReadAndVerifyToken(token);
        Assert.Equal(28_800, claims.GetProperty("exp").GetInt64() - claims.GetProperty("iat").GetInt64());
        Assert.Equal(fixture.ExpectedUtcNow.AddHours(8), body.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset());
        using var profile = await SessionGetAsync(fixture, token, "/api/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    [Theory]
    [InlineData(481)]
    [InlineData(1441)]
    public async Task Default_sessions_are_rejected_after_their_original_eight_hour_deadline(int elapsedMinutes)
    {
        await using var fixture = await LoginFixture.CreateAsync(accessTokenMinutes: null,
            issuedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-elapsedMinutes));
        await RegisterAsync(fixture, "Expired session", "expired-duration@example.test", "duration-password");
        var token = await LoginAndReadTokenAsync(fixture, "expired-duration@example.test", "duration-password");
        using var rejected = await SessionGetAsync(fixture, token, "/api/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal(UnauthorizedJson, await rejected.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("1", 2, HttpStatusCode.Unauthorized)]
    [InlineData("60", 61, HttpStatusCode.Unauthorized)]
    [InlineData("120", 61, HttpStatusCode.OK)]
    [InlineData("1440", 1439, HttpStatusCode.OK)]
    public async Task Explicit_session_lifetimes_keep_their_own_signed_deadline(string minutes, int elapsedMinutes, HttpStatusCode expected)
    {
        await using var fixture = await LoginFixture.CreateAsync(accessTokenMinutes: minutes,
            issuedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-elapsedMinutes));
        await RegisterAsync(fixture, "Configured session", "configured-duration@example.test", "duration-password");
        var token = await LoginAndReadTokenAsync(fixture, "configured-duration@example.test", "duration-password");
        var claims = ReadAndVerifyToken(token);
        Assert.Equal(long.Parse(minutes) * 60, claims.GetProperty("exp").GetInt64() - claims.GetProperty("iat").GetInt64());
        using var profile = await SessionGetAsync(fixture, token, "/api/profile");
        Assert.Equal(expected, profile.StatusCode);
    }

    [Theory]
    [InlineData(120, false, HttpStatusCode.OK)]
    [InlineData(120, true, HttpStatusCode.Unauthorized)]
    [InlineData(481, false, HttpStatusCode.Unauthorized)]
    public async Task Backend_restart_keeps_only_unexpired_sessions_with_the_same_key(int elapsedMinutes, bool rotateKey, HttpStatusCode expected)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"stocklab-session-duration-{Guid.NewGuid():N}.db");
        try
        {
            string token;
            await using (var beforeRestart = await LoginFixture.CreateAsync(accessTokenMinutes: null,
                issuedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-elapsedMinutes), databasePath: databasePath))
            {
                await RegisterAsync(beforeRestart, "Restarted session", "restart-duration@example.test", "duration-password");
                token = await LoginAndReadTokenAsync(beforeRestart, "restart-duration@example.test", "duration-password");
                using var initial = await SessionGetAsync(beforeRestart, token, "/api/profile");
                Assert.Equal(elapsedMinutes < 480 ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, initial.StatusCode);
            }
            await using var afterRestart = await LoginFixture.CreateAsync(accessTokenMinutes: null, databasePath: databasePath,
                signingKey: rotateKey ? "different-test-only-signing-key-at-least-32-bytes" : TestSigningKey);
            using var resumed = await SessionGetAsync(afterRestart, token, "/api/profile");
            Assert.Equal(expected, resumed.StatusCode);
            if (expected == HttpStatusCode.Unauthorized)
                Assert.Equal(UnauthorizedJson, await resumed.Content.ReadAsStringAsync());
            var fresh = await LoginAndReadTokenAsync(afterRestart, "restart-duration@example.test", "duration-password");
            using var renewedLogin = await SessionGetAsync(afterRestart, fresh, "/api/profile");
            Assert.Equal(HttpStatusCode.OK, renewedLogin.StatusCode);
        }
        finally { File.Delete(databasePath); }
    }
}
