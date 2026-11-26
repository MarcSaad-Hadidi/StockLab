using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Identity;
using StockLab.Infrastructure.Persistence;

namespace StockLab.UnitTests.Identity;

[Trait("Category", "SqlServer")]
public sealed class GoogleAuthenticationSqlServerTests
{
    private const string Origin = "https://frontend.example.test";
    private const string PreviousMigration = "20261115004323_AddPasswordRecovery";
    private const string ResetToken = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [LocalDbFact]
    public async Task Migration_preserves_password_users_and_filtered_binary_subject_index_allows_multiple_nulls()
    {
        await using var fixture = new Fixture();
        await using var db = fixture.Context();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Users (Id,DisplayName,Email,NormalizedEmail,PasswordHash,CreatedAtUtc,UpdatedAtUtc) VALUES ({id},N'Existing',N'old@example.test',N'OLD@EXAMPLE.TEST',N'existing-hash',{now},{now})");
        await db.Database.MigrateAsync();
        var existing = await db.Users.SingleAsync();
        Assert.Equal("existing-hash", existing.PasswordHash);
        Assert.Null(existing.GoogleSubject);
        await fixture.Seed("second@example.test");
        var upper = await fixture.Google("CaseSensitiveSubject", "upper@gmail.com");
        var lower = await fixture.Google("casesensitivesubject", "lower@gmail.com");
        Assert.NotEqual(upper.Id, lower.Id);
        await using var verify = fixture.Context();
        Assert.Equal(4, await verify.Users.CountAsync());
        Assert.Equal(2, await verify.Users.CountAsync(value => value.GoogleSubject == null));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Users SET PasswordHash = NULL WHERE GoogleSubject IS NULL"));
    }

    [LocalDbFact]
    public async Task Migration_rollback_refuses_to_erase_google_only_sign_in_and_preserves_schema_atomically()
    {
        await using var fixture = new Fixture();
        var user = await fixture.Google("rollback-subject", "rollback@gmail.com");
        await using var db = fixture.Context();
        var failure = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync(PreviousMigration));
        Assert.Contains("Google-only", failure.Message, StringComparison.OrdinalIgnoreCase);
        // Later migrations may already have rolled back successfully. Read only columns
        // that exist at the Google-auth migration, not the current User model.
        var stored = await db.Users.AsNoTracking().Where(value => value.Id == user.Id)
            .Select(value => new { value.GoogleSubject, value.PasswordHash }).SingleAsync();
        Assert.Equal("rollback-subject", stored.GoogleSubject);
        Assert.Null(stored.PasswordHash);
        Assert.Equal("20261115035556_AddGoogleAuthentication", (await db.Database.GetAppliedMigrationsAsync()).Last());
    }

    [LocalDbFact]
    public async Task Migration_rollback_with_password_accounts_preserves_hashes_and_sessions()
    {
        await using var fixture = new Fixture();
        var id = await fixture.Seed("rollback@example.test");
        await using var db = fixture.Context();
        var user = await db.Users.SingleAsync(); user.SessionVersion = 2; user.GoogleSubject = "linked-subject";
        await db.SaveChangesAsync();
        var hash = user.PasswordHash;
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var stored = await db.Users.AsNoTracking().Select(value => new { value.PasswordHash, value.SessionVersion }).SingleAsync();
        Assert.Equal(hash, stored.PasswordHash);
        Assert.Equal(2, stored.SessionVersion);
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Null((await db.Users.SingleAsync(value => value.Id == id)).GoogleSubject);
    }

    [LocalDbFact]
    public async Task Concurrent_first_google_sign_ins_create_one_user_and_one_normal_portfolio()
    {
        await using var fixture = new Fixture();
        await using (var schema = fixture.Context()) await schema.Database.MigrateAsync();
        var barrier = new TwoSaveBarrier();
        await using var first = fixture.Context(barrier);
        await using var second = fixture.Context(barrier);
        var identity = new GoogleIdentity("same-subject", "race@gmail.com", "Google Race");
        var results = await Task.WhenAll(fixture.Service(first, identity).ExchangeAsync("code", Origin, "fr", default),
            fixture.Service(second, identity).ExchangeAsync("code", Origin, "fr", default));
        Assert.Equal(results[0].User!.Id, results[1].User!.Id);
        await using var verify = fixture.Context();
        Assert.Equal(1, await verify.Users.CountAsync());
        Assert.Equal(1, await verify.Portfolios.CountAsync());
    }

    [LocalDbFact]
    public async Task Two_subject_tickets_for_one_account_cannot_overwrite_the_winning_google_link()
    {
        await using var fixture = new Fixture();
        await fixture.Seed("same@example.test");
        var firstIdentity = new GoogleIdentity("first-subject", "same@example.test", "First");
        var secondIdentity = new GoogleIdentity("second-subject", "same@example.test", "Second");
        var ticket1 = await fixture.Ticket(firstIdentity);
        var ticket2 = await fixture.Ticket(secondIdentity);
        var barrier = new TwoSaveBarrier();
        await using var first = fixture.Context(barrier);
        await using var second = fixture.Context(barrier);
        var results = await Task.WhenAll(Link(fixture.Service(first, firstIdentity), ticket1), Link(fixture.Service(second, secondIdentity), ticket2));
        Assert.Single(results, value => value.Success);
        Assert.Single(results, value => value.Error == "google_account_conflict");
        await using var verify = fixture.Context();
        Assert.Contains((await verify.Users.SingleAsync()).GoogleSubject, new[] { "first-subject", "second-subject" });
    }

    [LocalDbFact]
    public async Task One_subject_ticketed_for_two_accounts_is_bound_by_unique_index_to_only_one_account()
    {
        await using var fixture = new Fixture();
        await fixture.Seed("first@example.test");
        await fixture.Seed("second@example.test");
        var firstIdentity = new GoogleIdentity("same-subject", "first@example.test", "First");
        var secondIdentity = new GoogleIdentity("same-subject", "second@example.test", "Second");
        var ticket1 = await fixture.Ticket(firstIdentity);
        var ticket2 = await fixture.Ticket(secondIdentity);
        var barrier = new TwoSaveBarrier();
        await using var first = fixture.Context(barrier);
        await using var second = fixture.Context(barrier);
        var results = await Task.WhenAll(Link(fixture.Service(first, firstIdentity), ticket1), Link(fixture.Service(second, secondIdentity), ticket2));
        Assert.Single(results, value => value.Success);
        Assert.Single(results, value => value.Error == "google_account_conflict");
        await using var verify = fixture.Context();
        Assert.Equal(1, await verify.Users.CountAsync(value => value.GoogleSubject == "same-subject"));
    }

    [LocalDbFact]
    public async Task One_ticket_has_only_one_success_even_under_parallel_requests()
    {
        await using var fixture = new Fixture();
        await fixture.Seed("parallel@example.test");
        var identity = new GoogleIdentity("parallel-subject", "parallel@example.test", "Parallel");
        var ticket = await fixture.Ticket(identity);
        await using var first = fixture.Context();
        await using var second = fixture.Context();
        var results = await Task.WhenAll(Link(fixture.Service(first, identity), ticket), Link(fixture.Service(second, identity), ticket));
        Assert.Single(results, value => value.Success);
        Assert.Single(results, value => value.Error == "google_link_expired");
    }

    [LocalDbFact]
    public async Task Password_recovery_blocks_a_previously_loaded_password_proof_from_restoring_google()
    {
        await using var fixture = new Fixture();
        var id = await fixture.Seed("reset@example.test", resetToken: true);
        var identity = new GoogleIdentity("old-google-subject", "reset@example.test", "Reset");
        var ticket = await fixture.Ticket(identity);
        await using var staleLink = fixture.Context();
        await staleLink.Users.SingleAsync();
        await using (var reset = fixture.Context())
            Assert.True(await new UserPasswordRecoveryService(reset, new PasswordHasher<User>(), TimeProvider.System)
                .ResetAsync(id, ResetToken, "recovered-password", default));
        var result = await Link(fixture.Service(staleLink, identity), ticket);
        Assert.False(result.Success);
        await using var verify = fixture.Context();
        var user = await verify.Users.AsNoTracking().SingleAsync();
        Assert.Null(user.GoogleSubject);
        Assert.Equal(1, user.SessionVersion);
        Assert.NotNull(await new UserLoginService(verify, new PasswordHasher<User>()).AuthenticateAsync("reset@example.test", "recovered-password", default));
    }

    [LocalDbFact]
    public async Task Concurrent_reset_and_google_link_commit_one_consistent_credential_generation()
    {
        await using var fixture = new Fixture();
        var id = await fixture.Seed("reset-race@example.test", resetToken: true);
        var identity = new GoogleIdentity("old-google-subject", "reset-race@example.test", "Race");
        var ticket = await fixture.Ticket(identity);
        var barrier = new TwoSaveBarrier();
        await using var linkDb = fixture.Context(barrier);
        await using var resetDb = fixture.Context(barrier);
        var resetTask = new UserPasswordRecoveryService(resetDb, new PasswordHasher<User>(), TimeProvider.System)
            .ResetAsync(id, ResetToken, "recovered-password", default);
        var linkTask = Link(fixture.Service(linkDb, identity), ticket);
        await Task.WhenAll(resetTask, linkTask);
        Assert.NotEqual(resetTask.Result, linkTask.Result.Success);
        await using var verify = fixture.Context();
        var user = await verify.Users.AsNoTracking().SingleAsync();
        if (resetTask.Result)
        {
            Assert.Null(user.GoogleSubject); Assert.Equal(1, user.SessionVersion); Assert.Null(user.PasswordResetTokenHash);
        }
        else
        {
            Assert.Equal("old-google-subject", user.GoogleSubject); Assert.Equal(0, user.SessionVersion);
            Assert.True(await new UserPasswordRecoveryService(verify, new PasswordHasher<User>(), TimeProvider.System)
                .ResetAsync(id, ResetToken, "recovered-password", default));
            user = await verify.Users.AsNoTracking().SingleAsync();
            Assert.Null(user.GoogleSubject); Assert.Equal(1, user.SessionVersion);
        }
    }

    private static async Task<(bool Success, string? Error)> Link(IGoogleAuthenticationService service, string token)
    {
        try { await service.LinkAsync(token, "existing-password", Origin, default); return (true, null); }
        catch (GoogleAuthenticationException failure) { return (false, failure.Error); }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string connection = $"Server=(localdb)\\MSSQLLocalDB;Database=StockLabGoogleTests_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true";
        private readonly GoogleLinkTicketStore tickets = new(TimeProvider.System);
        public StockLabDbContext Context(SaveChangesInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlServer(connection);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            return new(options.Options);
        }
        public GoogleAuthenticationService Service(StockLabDbContext db, GoogleIdentity identity) =>
            new(db, new TestOnlyIdentity(identity), tickets, new PasswordHasher<User>(), TimeProvider.System);
        public async Task<User> Google(string subject, string email)
        {
            await using var db = Context(); await db.Database.MigrateAsync();
            return (await Service(db, new(subject, email, "Google User")).ExchangeAsync("code", Origin, "en", default)).User!;
        }
        public async Task<Guid> Seed(string email, bool resetToken = false)
        {
            await using var db = Context(); await db.Database.MigrateAsync();
            var user = await new UserRegistrationService(db, new PasswordHasher<User>(), TimeProvider.System)
                .RegisterAsync("Existing User", email, "existing-password", default);
            if (resetToken)
            {
                user.PasswordResetTokenHash = SHA256.HashData(Encoding.ASCII.GetBytes(ResetToken));
                user.PasswordResetExpiresAtUtc = DateTime.UtcNow.AddMinutes(30); await db.SaveChangesAsync();
            }
            return user.Id;
        }
        public async Task<string> Ticket(GoogleIdentity identity)
        {
            await using var db = Context();
            return (await Service(db, identity).ExchangeAsync("code", Origin, "en", default)).LinkToken!;
        }
        public async ValueTask DisposeAsync()
        {
            tickets.Dispose(); await using var db = Context(); await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class TwoSaveBarrier : SaveChangesInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrivals) == 2) gate.TrySetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }
    private sealed class TestOnlyIdentity(GoogleIdentity identity) : IGoogleIdentityProvider
    {
        public Task<GoogleIdentity> ExchangeAsync(string code, string origin, CancellationToken cancellationToken) => Task.FromResult(identity);
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
