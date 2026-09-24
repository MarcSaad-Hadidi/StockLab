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
