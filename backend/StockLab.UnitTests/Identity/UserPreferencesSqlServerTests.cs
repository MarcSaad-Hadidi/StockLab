using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StockLab.Application.DTOs;
using StockLab.Application.Exceptions;
using StockLab.Infrastructure.Identity;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Identity;

[Trait("Category", "SqlServer")]
public sealed class UserPreferencesSqlServerTests
{
    private const string PreviousMigration = "20261115035556_AddGoogleAuthentication";

    [LocalDbFact]
    public async Task Migration_backfills_preferences_and_stale_writes_do_not_clobber_a_newer_save()
    {
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database=StockLabPreferencesTests_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlServer(connection).Options;
        await using var db = new StockLabDbContext(options);
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            var id = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Users (Id, DisplayName, Email, NormalizedEmail, PasswordHash, PreferredLanguage, CreatedAtUtc, UpdatedAtUtc) VALUES ({id}, N'Existing account', N'legacy@example.test', N'LEGACY@EXAMPLE.TEST', N'test-only-hash', N'fr', {now}, {now})");

            await migrator.MigrateAsync();
            await using var stale = new StockLabDbContext(options);
            var existing = await stale.Users.SingleAsync();
            Assert.False(existing.EmailNotifications);
            Assert.False(existing.TradeAlerts);
            Assert.False(existing.MarketingCommunications);
            Assert.False(existing.DarkMode);

            var first = new UserProfileService(db, TimeProvider.System);
            await first.UpdatePreferencesAsync(id, new(true, null, null, true), default);
            await Assert.ThrowsAsync<UserProfileUpdateConflictException>(() =>
                new UserProfileService(stale, TimeProvider.System)
                    .UpdatePreferencesAsync(id, new UserPreferencesPatch(null, true, null, null), default));

            await using var retry = new StockLabDbContext(options);
            await new UserProfileService(retry, TimeProvider.System)
                .UpdatePreferencesAsync(id, new UserPreferencesPatch(null, true, null, null), default);
            var stored = await retry.Users.AsNoTracking().SingleAsync();
            Assert.True(stored.TradeAlerts);
            Assert.True(stored.EmailNotifications);
            Assert.True(stored.DarkMode);
            Assert.False(stored.MarketingCommunications);
            Assert.Equal("fr", stored.PreferredLanguage);
            Assert.Equal("legacy@example.test", stored.Email);

            await migrator.MigrateAsync(PreviousMigration);
            Assert.Equal("legacy@example.test",
                await db.Database.SqlQueryRaw<string>("SELECT Email AS [Value] FROM Users").SingleAsync());
        }
        finally
        {
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
