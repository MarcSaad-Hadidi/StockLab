using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Identity;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Identity;

[Trait("Category", "SqlServer")]
public sealed class SessionRevocationSqlServerTests
{
    private const string PreviousMigration = "20261031191523_AddUserPreferredLanguage";

    [LocalDbFact]
    public async Task Migration_backfills_existing_accounts_and_refuses_to_discard_revocations_on_rollback()
    {
        await using var fixture = new Fixture();
        await using var db = fixture.Context();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Users (Id, DisplayName, Email, NormalizedEmail, PasswordHash, CreatedAtUtc, UpdatedAtUtc) VALUES ({id}, N'Existing account', N'session@example.com', N'SESSION@EXAMPLE.COM', N'test-only-existing-hash', {now}, {now})");
        await db.Database.MigrateAsync();
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID(N'Users') AND name = N'SessionVersion'").SingleAsync());
        var account = await db.Users.AsNoTracking().SingleAsync();
        Assert.Equal(0, account.SessionVersion);
        Assert.Equal("test-only-existing-hash", account.PasswordHash);
        Assert.Equal("en", account.PreferredLanguage);
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Users SET SessionVersion = -1"));
        await db.Database.ExecuteSqlRawAsync("UPDATE Users SET SessionVersion = 1");
        var latest = (await db.Database.GetAppliedMigrationsAsync()).Last();
        var rejected = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync(PreviousMigration));
        Assert.Contains("revocation", rejected.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(latest, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, (await db.Users.AsNoTracking().SingleAsync()).SessionVersion);
    }

    [LocalDbFact]
    public async Task Concurrent_password_changes_commit_one_hash_and_one_session_generation()
    {
        await using var fixture = new Fixture();
        await using var winner = fixture.Context();
        await winner.Database.MigrateAsync();
        var hasher = new PasswordHasher<User>();
        var now = DateTime.UtcNow;
        var account = new User { Id = Guid.NewGuid(), DisplayName = "Concurrent account", Email = "session@example.com",
            NormalizedEmail = "SESSION@EXAMPLE.COM", CreatedAtUtc = now, UpdatedAtUtc = now };
        account.PasswordHash = hasher.HashPassword(account, "old-password");
        winner.Users.Add(account);
        await winner.SaveChangesAsync();
        await using var stale = fixture.Context();
        await stale.Users.SingleAsync();
        Assert.Equal(PasswordChangeResult.Changed,
            await new UserPasswordService(winner, hasher, TimeProvider.System).ChangeAsync(account.Id, "old-password", "winner-password", default));
        Assert.Equal(PasswordChangeResult.Conflict,
            await new UserPasswordService(stale, hasher, TimeProvider.System).ChangeAsync(account.Id, "old-password", "loser-password", default));
        await using var fresh = fixture.Context();
        var stored = await fresh.Users.AsNoTracking().SingleAsync();
        Assert.Equal(1, stored.SessionVersion);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(stored, stored.PasswordHash, "winner-password"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(stored, stored.PasswordHash, "loser-password"));
        Assert.False(await new UserSessionValidator(fresh).IsCurrentAsync(account.Id, 0, default));
        Assert.True(await new UserSessionValidator(fresh).IsCurrentAsync(account.Id, 1, default));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string connection = $"Server=(localdb)\\MSSQLLocalDB;Database=StockLabSessionTests_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true";
        public StockLabDbContext Context() => new(new DbContextOptionsBuilder<StockLabDbContext>().UseSqlServer(connection).Options);
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
}
