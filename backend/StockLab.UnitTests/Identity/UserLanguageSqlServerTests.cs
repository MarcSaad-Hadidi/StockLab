using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StockLab.Application.Exceptions;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Identity;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Identity;

[Trait("Category", "SqlServer")]
public sealed class UserLanguageSqlServerTests
{
    private const string PreviousMigration = "20261026185004_AddAiPerformanceCycles";

    [LocalDbFact]
    public async Task Migration_backfills_existing_accounts_without_changing_identity_or_password()
    {
        await using var fixture = new Fixture();
        await using var db = fixture.Context();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Users (Id, DisplayName, Email, NormalizedEmail, PasswordHash, CreatedAtUtc, UpdatedAtUtc) VALUES ({id}, N'Existing account', N'legacy@example.com', N'LEGACY@EXAMPLE.COM', N'test-only-existing-hash', {now}, {now})");
        await db.Database.MigrateAsync();
        var account = await db.Users.AsNoTracking().SingleAsync();
        Assert.Equal("en", account.PreferredLanguage);
        Assert.Equal("Existing account", account.DisplayName);
        Assert.Equal("legacy@example.com", account.Email);
        Assert.Equal("test-only-existing-hash", account.PasswordHash);
        await new UserProfileService(db, TimeProvider.System).UpdateLanguageAsync(id, "fr", default);
        await using var fresh = fixture.Context();
        Assert.Equal("fr", (await fresh.Users.AsNoTracking().SingleAsync()).PreferredLanguage);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        Assert.Equal("legacy@example.com", await db.Database.SqlQueryRaw<string>("SELECT Email AS [Value] FROM Users").SingleAsync());
        await db.Database.MigrateAsync();
        Assert.Equal("en", (await fresh.Users.AsNoTracking().SingleAsync()).PreferredLanguage);
    }

    [LocalDbFact]
    public async Task Database_rejects_unsupported_or_noncanonical_language_codes()
    {
        await using var fixture = new Fixture();
        await using var db = fixture.Context();
        await db.Database.MigrateAsync();
        db.Users.Add(Account());
        await db.SaveChangesAsync();
        foreach (var invalid in new[] { "es", "FR", "" })
        {
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Users SET PreferredLanguage = {invalid}"));
            Assert.Equal("en", (await db.Users.AsNoTracking().SingleAsync()).PreferredLanguage);
        }
    }

    [LocalDbFact]
    public async Task Stale_language_updates_respect_sql_server_rowversion()
    {
        await using var fixture = new Fixture();
        await using var first = fixture.Context();
        await first.Database.MigrateAsync();
        var account = Account();
        first.Users.Add(account);
        await first.SaveChangesAsync();
        await using var stale = fixture.Context();
        await stale.Users.SingleAsync();
        await new UserProfileService(first, TimeProvider.System).UpdateLanguageAsync(account.Id, "fr", default);
        await Assert.ThrowsAsync<UserProfileUpdateConflictException>(() =>
            new UserProfileService(stale, TimeProvider.System).UpdateLanguageAsync(account.Id, "fr", default));
        Assert.Equal("fr", (await first.Users.AsNoTracking().SingleAsync()).PreferredLanguage);
    }

    private static User Account() => new()
    {
        Id = Guid.NewGuid(), DisplayName = "Language test", Email = "language@example.com",
        NormalizedEmail = "LANGUAGE@EXAMPLE.COM", PasswordHash = "test-only-hash",
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string connection = $"Server=(localdb)\\MSSQLLocalDB;Database=StockLabLanguageTests_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true";
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
