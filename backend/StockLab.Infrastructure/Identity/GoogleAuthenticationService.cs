using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Identity;

public sealed class GoogleAuthenticationService(StockLabDbContext db, IGoogleIdentityProvider provider,
    GoogleLinkTicketStore tickets, IPasswordHasher<User> hasher, TimeProvider time) : IGoogleAuthenticationService
{
    public async Task<GoogleAuthenticationResult> ExchangeAsync(string code, string origin, string language,
        CancellationToken cancellationToken)
    {
        var identity = await provider.ExchangeAsync(code, origin, cancellationToken);
        var existing = await Resolve(identity, origin, cancellationToken);
        if (existing is not null) return existing;
        var now = time.GetUtcNow().UtcDateTime;
        var user = new User
        {
            Id = Guid.NewGuid(), DisplayName = identity.DisplayName, Email = identity.Email,
            NormalizedEmail = EmailAddressNormalizer.Normalize(identity.Email), GoogleSubject = identity.Subject,
            PasswordHash = null, PreferredLanguage = language, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        user.Portfolio = new Portfolio
        {
            Id = Guid.NewGuid(), UserId = user.Id, User = user, Currency = "USD", InitialCapital = 100_000m,
            CashBalance = 100_000m, CreatedAtUtc = now
        };
        db.Users.Add(user);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            // A concurrent Google/manual registration must resolve through the same subject/proof rules.
            var winner = await Resolve(identity, origin, cancellationToken);
            if (winner is not null) return winner;
            throw;
        }
        return new(user);
    }

    private async Task<GoogleAuthenticationResult?> Resolve(GoogleIdentity identity, string origin, CancellationToken cancellationToken)
    {
        // Subject is the stable identity. Never replace local profile/email from a later Google assertion.
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(value => value.GoogleSubject == identity.Subject, cancellationToken);
        if (user is not null) return new(user);
        var normalized = EmailAddressNormalizer.Normalize(identity.Email);
        user = await db.Users.AsNoTracking().SingleOrDefaultAsync(value => value.NormalizedEmail == normalized, cancellationToken);
        if (user is null) return null;
        if (user.GoogleSubject is not null || user.PasswordHash is null)
            throw new GoogleAuthenticationException("google_account_conflict");
        var ticket = tickets.Issue(user, identity.Subject, origin);
        return new(null, ticket.Token, ticket.ExpiresAtUtc);
    }

    public async Task<User> LinkAsync(string token, string password, string origin, CancellationToken cancellationToken)
    {
        var ticket = tickets.Get(token, origin);
        await ticket.Gate.WaitAsync(cancellationToken);
        try
        {
            if (ticket.Consumed || ticket.Attempts >= 5 || ticket.ExpiresAtUtc <= time.GetUtcNow())
                throw new GoogleAuthenticationException("google_link_expired");
            var user = await db.Users.SingleOrDefaultAsync(value => value.Id == ticket.UserId, cancellationToken);
            if (user is null || user.NormalizedEmail != ticket.Email || user.SessionVersion != ticket.SessionVersion
                || user.PasswordHash is null || user.PasswordHash != ticket.PasswordHash)
            {
                tickets.Remove(token);
                throw new GoogleAuthenticationException("google_link_expired");
            }
            if (user.GoogleSubject is not null || await db.Users.AnyAsync(value => value.GoogleSubject == ticket.Subject, cancellationToken))
                throw new GoogleAuthenticationException("google_account_conflict");
            ticket.Attempts++;
            if (hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
            {
                if (ticket.Attempts >= 5) tickets.Remove(token);
                throw new GoogleAuthenticationException("invalid_credentials");
            }
            user.GoogleSubject = ticket.Subject;
            user.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                tickets.Remove(token);
                throw new GoogleAuthenticationException("google_account_conflict");
            }
            ticket.Consumed = true;
            tickets.Remove(token);
            return user;
        }
        finally { ticket.Gate.Release(); }
    }
}
