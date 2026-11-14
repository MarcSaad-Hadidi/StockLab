using Microsoft.EntityFrameworkCore;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Identity;

public sealed class UserProfileService(
    StockLabDbContext dbContext,
    TimeProvider timeProvider) : IUserProfileService
{
    public async Task<User?> UpdateLanguageAsync(Guid userId, string preferredLanguage, CancellationToken cancellationToken,
        byte[]? expectedVersion = null)
    {
        if (preferredLanguage is not ("en" or "fr"))
            throw new ArgumentOutOfRangeException(nameof(preferredLanguage));
        var user = await dbContext.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null) return null;
        if (expectedVersion is not null && !user.Version.AsSpan().SequenceEqual(expectedVersion))
            throw new UserProfileUpdateConflictException();
        user.PreferredLanguage = preferredLanguage;
        user.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        // A same-language choice is still a newer intent and must advance SQL rowversion,
        // even when the clock has not advanced and EF sees no changed values.
        dbContext.Entry(user).Property(item => item.PreferredLanguage).IsModified = true;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new UserProfileUpdateConflictException();
        }
        return user;
    }

    public Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task<User?> UpdateAsync(
        Guid userId,
        string displayName,
        string email,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(
            existingUser => existingUser.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var trimmedDisplayName = displayName.Trim();
        var trimmedEmail = email.Trim();
        var normalizedEmail = EmailAddressNormalizer.Normalize(trimmedEmail);
        if (await dbContext.Users.AsNoTracking().AnyAsync(
                existingUser => existingUser.Id != userId
                    && existingUser.NormalizedEmail == normalizedEmail,
                cancellationToken))
        {
            throw new UserEmailAlreadyRegisteredException();
        }

        user.DisplayName = trimmedDisplayName;
        user.Email = trimmedEmail;
        user.NormalizedEmail = normalizedEmail;
        user.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new UserProfileUpdateConflictException();
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            if (await dbContext.Users.AsNoTracking().AnyAsync(
                    existingUser => existingUser.Id != userId
                        && existingUser.NormalizedEmail == normalizedEmail,
                    cancellationToken))
            {
                throw new UserEmailAlreadyRegisteredException();
            }

            throw;
        }

        return user;
    }
}
