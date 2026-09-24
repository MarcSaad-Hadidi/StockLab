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
    private const string DefaultCurrency = "USD";
    private const decimal DefaultInitialCapital = 100_000m;

    public async Task<User> RegisterAsync(string displayName, string email, string password,
        CancellationToken cancellationToken)
    {
        var trimmedDisplayName = displayName.Trim();
        var trimmedEmail = email.Trim();
        var normalizedEmail = EmailAddressNormalizer.Normalize(trimmedEmail);

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

        user.Portfolio = new Portfolio
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            Currency = DefaultCurrency,
            InitialCapital = DefaultInitialCapital,
            CashBalance = DefaultInitialCapital,
            CreatedAtUtc = now
        };

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
