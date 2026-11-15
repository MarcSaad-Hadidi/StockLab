using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Identity;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Identity;

[Trait("Category", "SqlServer")]
public sealed class PasswordRecoverySqlServerTests
{
    private const string Token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string PreviousMigration = "20261102041145_AddPersonalPortfolioPerformanceHistory";

    [LocalDbFact]
    public async Task Concurrent_uses_of_one_reset_token_commit_exactly_one_password_and_session_generation()
    {
        await using var fixture = new Fixture();
        var id = await fixture.Seed();
        await using var first = fixture.Context();
        await using var second = fixture.Context();
        await first.Users.SingleAsync();
        await second.Users.SingleAsync();
        var results = await Task.WhenAll(
            Service(first).ResetAsync(id, Token, "first-replacement", default),
            Service(second).ResetAsync(id, Token, "second-replacement", default));
        Assert.Single(results, success => success);
        await using var fresh = fixture.Context();
        var user = await fresh.Users.AsNoTracking().SingleAsync();
        var hasher = new PasswordHasher<User>();
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, user.PasswordHash,
            results[0] ? "first-replacement" : "second-replacement"));
        Assert.Null(user.PasswordResetTokenHash);
        Assert.Null(user.PasswordResetExpiresAtUtc);
        Assert.Equal(1, user.SessionVersion);
        Assert.False(await new UserSessionValidator(fresh).IsCurrentAsync(id, 0, default));
        Assert.False(await Service(fresh).ResetAsync(id, Token, "replayed-password", default));
    }

    [LocalDbTheory]
    [InlineData("email")]
    [InlineData("password")]
    public async Task Reset_loaded_before_account_change_cannot_overwrite_newer_sql_rowversion(string change)
    {
        await using var fixture = new Fixture();
        var id = await fixture.Seed();
        await using var stale = fixture.Context();
        await stale.Users.SingleAsync();
        await using var winner = fixture.Context();
        if (change == "email")
            await new UserProfileService(winner, TimeProvider.System).UpdateAsync(id, "Recovery", "new@example.com", default);
        else
            await new UserPasswordService(winner, new PasswordHasher<User>(), TimeProvider.System)
                .ChangeAsync(id, "original-password", "authenticated-change", default);
        Assert.False(await Service(stale).ResetAsync(id, Token, "stale-reset-password", default));
        await using var fresh = fixture.Context();
        var user = await fresh.Users.AsNoTracking().SingleAsync();
        Assert.Null(user.PasswordResetTokenHash);
        Assert.Equal(change == "email" ? "new@example.com" : "known@example.com", user.Email);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<User>().VerifyHashedPassword(user,
            user.PasswordHash, change == "email" ? "original-password" : "authenticated-change"));
    }

    [LocalDbFact]
    public async Task Migration_preserves_existing_accounts_and_rollback_only_discards_recovery_state()
    {
        await using var fixture = new Fixture();
        await using var db = fixture.Context();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Users (Id, DisplayName, Email, NormalizedEmail, PasswordHash, CreatedAtUtc, UpdatedAtUtc) VALUES ({id}, N'Existing account', N'legacy@example.com', N'LEGACY@EXAMPLE.COM', N'existing-hash', {now}, {now})");
        await db.Database.MigrateAsync();
        var user = await db.Users.SingleAsync();
        Assert.Equal("existing-hash", user.PasswordHash);
        Assert.Equal("legacy@example.com", user.Email);
        Assert.Null(user.PasswordResetTokenHash);
        Assert.Null(user.PasswordResetExpiresAtUtc);
        Assert.Null(user.PasswordResetRequestedAtUtc);
        user.PasswordResetTokenHash = SHA256.HashData(Encoding.ASCII.GetBytes(Token));
        user.PasswordResetRequestedAtUtc = now;
        user.PasswordResetExpiresAtUtc = now.AddMinutes(30);
        await db.SaveChangesAsync();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        Assert.Equal("existing-hash", await db.Users.Select(value => value.PasswordHash).SingleAsync());
        Assert.Equal(0, await db.Users.Select(value => value.SessionVersion).SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID(N'Users') AND name LIKE N'PasswordReset%'").SingleAsync());
        await db.Database.MigrateAsync();
        Assert.Null(await db.Users.Select(value => value.PasswordResetTokenHash).SingleAsync());
    }

    private static UserPasswordRecoveryService Service(StockLabDbContext db) =>
        new(db, new PasswordHasher<User>(), TimeProvider.System);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string connection = $"Server=(localdb)\\MSSQLLocalDB;Database=StockLabRecoveryTests_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true";
        public StockLabDbContext Context() => new(new DbContextOptionsBuilder<StockLabDbContext>().UseSqlServer(connection).Options);
        public async Task<Guid> Seed()
        {
            await using var db = Context();
            await db.Database.MigrateAsync();
            var now = DateTime.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid(), DisplayName = "Recovery", Email = "known@example.com", NormalizedEmail = "KNOWN@EXAMPLE.COM",
                CreatedAtUtc = now, UpdatedAtUtc = now, PasswordResetTokenHash = SHA256.HashData(Encoding.ASCII.GetBytes(Token)),
                PasswordResetRequestedAtUtc = now, PasswordResetExpiresAtUtc = now.AddMinutes(30)
            };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "original-password");
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = Context();
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class LocalDbFactAttribute : FactAttribute
    {
        public LocalDbFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }

    private sealed class LocalDbTheoryAttribute : TheoryAttribute
    {
        public LocalDbTheoryAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
}
