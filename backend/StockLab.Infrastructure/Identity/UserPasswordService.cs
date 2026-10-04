using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Identity;

public sealed class UserPasswordService(StockLabDbContext dbContext, IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider) : IUserPasswordService
{
    public async Task<PasswordChangeResult> ChangeAsync(Guid userId, string currentPassword, string newPassword,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(value => value.Id == userId, cancellationToken);
        if (user is null) return PasswordChangeResult.UserNotFound;
        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            return PasswordChangeResult.InvalidCurrentPassword;
        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        user.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent profile or password change must never be silently overwritten.
            return PasswordChangeResult.Conflict;
        }
        return PasswordChangeResult.Changed;
    }
}
