using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Api;

public sealed partial class UserLoginApiTests
{
    [Fact]
    public async Task Password_change_replaces_the_password_used_by_real_login()
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100);
        await RegisterAsync(fixture, "Password Test", "password@example.com", "old-password");
        using var login = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new { email = "password@example.com", password = "old-password" })));
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        fixture.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("accessToken").GetString());
        using var changed = await fixture.Client.PutAsync("/api/auth/password", Json(JsonSerializer.Serialize(new { currentPassword = "old-password", newPassword = "new-password", confirmPassword = "new-password" })));
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        fixture.Client.DefaultRequestHeaders.Authorization = null;
        using var oldLogin = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new { email = "password@example.com", password = "old-password" })));
        using var newLogin = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new { email = "password@example.com", password = "new-password" })));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    private static async Task SignInForPasswordChangeAsync(LoginFixture fixture)
    {
        await RegisterAsync(fixture, "Password Test", "password@example.com", "old-password");
        using var response = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new { email = "password@example.com", password = "old-password" })));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        fixture.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("accessToken").GetString());
    }

    [Theory]
    [InlineData("wrong-password", "new-password", "new-password", "invalid_current_password")]
    [InlineData("old-password", "short", "short", "validation_error")]
    [InlineData("old-password", "new-password", "different-password", "validation_error")]
    [InlineData("", "new-password", "new-password", "validation_error")]
    [InlineData("old-password", "old-password", "old-password", "validation_error")]
    [InlineData("old-password", "        ", "        ", "validation_error")]
    public async Task Password_change_invalid_requests_preserve_the_existing_hash(string currentPassword, string newPassword, string confirmPassword, string expectedError)
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100);
        await SignInForPasswordChangeAsync(fixture);
        string hash;
        using (var scope = fixture.CreateScope()) hash = (await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.AsNoTracking().SingleAsync()).PasswordHash;
        using var response = await fixture.Client.PutAsync("/api/auth/password", Json(JsonSerializer.Serialize(new { currentPassword, newPassword, confirmPassword })));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal(expectedError, body.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain(hash, text);
        Assert.DoesNotContain("old-password", text);
        Assert.DoesNotContain("new-password", text);
        using var verificationScope = fixture.CreateScope();
        Assert.Equal(hash, (await verificationScope.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.AsNoTracking().SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task Password_change_requires_authentication()
    {
        await using var fixture = await LoginFixture.CreateAsync();
        using var response = await fixture.Client.PutAsync("/api/auth/password", Json("{}"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.InternalServerError, "internal_server_error")]
    [InlineData(true, HttpStatusCode.Conflict, "password_update_conflict")]
    public async Task Password_change_failed_persistence_never_reports_success(bool concurrency, HttpStatusCode status, string error)
    {
        await using var fixture = await LoginFixture.CreateAsync(saveChangesInterceptor: new PasswordSaveFailureInterceptor(concurrency), loginPermitLimit: 100);
        await SignInForPasswordChangeAsync(fixture);
        using var response = await fixture.Client.PutAsync("/api/auth/password", Json(JsonSerializer.Serialize(new { currentPassword = "old-password", newPassword = "new-password", confirmPassword = "new-password" })));
        Assert.Equal(status, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(error, body.RootElement.GetProperty("error").GetString());
        fixture.Client.DefaultRequestHeaders.Authorization = null;
        using var oldLogin = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new { email = "password@example.com", password = "old-password" })));
        using var newLogin = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new { email = "password@example.com", password = "new-password" })));
        Assert.Equal(HttpStatusCode.OK, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, newLogin.StatusCode);
    }

    [Fact]
    public async Task Password_change_uses_the_authenticated_user_and_does_not_modify_other_accounts()
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100);
        await SignInForPasswordChangeAsync(fixture);
        var otherId = await RegisterAsync(fixture, "Other User", "other@example.com", "old-password");
        using var response = await fixture.Client.PutAsync("/api/auth/password", Json(JsonSerializer.Serialize(new { userId = otherId, currentPassword = "old-password", newPassword = "new-password", confirmPassword = "new-password" })));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        fixture.Client.DefaultRequestHeaders.Authorization = null;
        using var otherLogin = await fixture.Client.PostAsync("/api/auth/login", Json(JsonSerializer.Serialize(new { email = "other@example.com", password = "old-password" })));
        Assert.Equal(HttpStatusCode.OK, otherLogin.StatusCode);
    }


    [Fact]
    public async Task Password_change_missing_account_returns_not_found()
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 100);
        await SignInForPasswordChangeAsync(fixture);
        using (var scope = fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
            await context.Portfolios.ExecuteDeleteAsync();
            await context.Users.ExecuteDeleteAsync();
        }
        using var response = await fixture.Client.PutAsync("/api/auth/password", Json(JsonSerializer.Serialize(new { currentPassword = "old-password", newPassword = "new-password", confirmPassword = "new-password" })));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("user_not_found", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Password_change_attempts_are_rate_limited_without_changing_the_hash()
    {
        await using var fixture = await LoginFixture.CreateAsync(loginPermitLimit: 2);
        await SignInForPasswordChangeAsync(fixture);
        string hash;
        using (var scope = fixture.CreateScope()) hash = (await scope.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.AsNoTracking().SingleAsync()).PasswordHash;
        var payload = JsonSerializer.Serialize(new { currentPassword = "wrong-password", newPassword = "new-password", confirmPassword = "new-password" });
        using var first = await fixture.Client.PutAsync("/api/auth/password", Json(payload));
        using var second = await fixture.Client.PutAsync("/api/auth/password", Json(payload));
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        using var verificationScope = fixture.CreateScope();
        Assert.Equal(hash, (await verificationScope.ServiceProvider.GetRequiredService<StockLabDbContext>().Users.AsNoTracking().SingleAsync()).PasswordHash);
    }

    private sealed class PasswordSaveFailureInterceptor(bool concurrency) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<User>().Any(entry => entry.State == EntityState.Modified && entry.Property(user => user.PasswordHash).IsModified) == true)
            {
                if (concurrency) throw new DbUpdateConcurrencyException("Test password concurrency failure.");
                throw new DbUpdateException("Test password persistence failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
