using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Identity;

public sealed class UserLoginService(
    StockLabDbContext dbContext,
    IPasswordHasher<User> passwordHasher) : IUserLoginService
{
    public async Task<User?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailAddressNormalizer.Normalize(email);
        User? user = await dbContext.Users.SingleOrDefaultAsync(
            value => value.NormalizedEmail == normalizedEmail,
            cancellationToken);
        if (user is null)
        {
            return null;
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification is not (PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded))
        {
            return null;
        }

        for (var rehashAttempt = 0;
             verification == PasswordVerificationResult.SuccessRehashNeeded && rehashAttempt < 2;
             rehashAttempt++)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.Entry(user).State = EntityState.Detached;
                user = await dbContext.Users.SingleOrDefaultAsync(
                    value => value.NormalizedEmail == normalizedEmail,
                    cancellationToken);
                if (user is null)
                {
                    return null;
                }

                verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
                if (verification is not (PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded))
                {
                    return null;
                }
            }
        }

        return user;
    }
}
