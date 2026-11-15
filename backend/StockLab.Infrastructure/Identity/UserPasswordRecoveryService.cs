using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Identity;

public sealed class UserPasswordRecoveryService(StockLabDbContext dbContext,
    IPasswordHasher<User> passwordHasher, TimeProvider timeProvider) : IUserPasswordRecoveryService
{
    public async Task<bool> ResetAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || token.Length != 64 || token.Any(value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            return false;
        var user = await dbContext.Users.SingleOrDefaultAsync(value => value.Id == userId, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(token));
        if (user?.PasswordResetTokenHash is not { Length: 32 } hash ||
            user.PasswordResetExpiresAtUtc is not { } expiry || expiry <= now ||
            !CryptographicOperations.FixedTimeEquals(hash, digest)) return false;
        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        user.SessionVersion = checked(user.SessionVersion + 1);
        user.PasswordResetTokenHash = null;
        user.PasswordResetExpiresAtUtc = null;
        user.UpdatedAtUtc = now;
        try
        {
            // SQL rowversion protects the token consume and password/session change in one write.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return false; }
        return true;
    }
}
