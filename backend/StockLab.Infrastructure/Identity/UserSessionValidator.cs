using Microsoft.EntityFrameworkCore;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Identity;

public sealed class UserSessionValidator(StockLabDbContext dbContext) : IUserSessionValidator
{
    public Task<bool> IsCurrentAsync(Guid userId, long sessionVersion, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.SessionVersion == sessionVersion,
            cancellationToken);
}
