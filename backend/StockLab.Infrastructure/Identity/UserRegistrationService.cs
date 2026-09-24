using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Identity;

public sealed class UserRegistrationService(
    StockLabDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider) : IUserRegistrationService
{
    public async Task<User> RegisterAsync(string displayName, string email, string password,
        CancellationToken cancellationToken)
    {
        var trimmedDisplayName = displayName.Trim();
        var trimmedEmail = email.Trim();
        var normalizedEmail = trimmedEmail.ToUpper(CultureInfo.InvariantCulture);

        if (await dbContext.Users.AsNoTracking()
                .AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            throw new UserEmailAlreadyRegisteredException();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = trimmedDisplayName,
            Email = trimmedEmail,
            NormalizedEmail = normalizedEmail,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);

        dbContext.Users.Add(user);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            if (await dbContext.Users.AsNoTracking()
                    .AnyAsync(existingUser => existingUser.NormalizedEmail == normalizedEmail, cancellationToken))
            {
                throw new UserEmailAlreadyRegisteredException();
            }

            throw;
        }

        return user;
    }
}
