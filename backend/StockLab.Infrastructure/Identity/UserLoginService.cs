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
        var user = await dbContext.Users.SingleOrDefaultAsync(
            value => value.NormalizedEmail == normalizedEmail,
            cancellationToken);
        if (user is null)
        {
            return null;
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return verification is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded
            ? user
            : null;
    }
}
