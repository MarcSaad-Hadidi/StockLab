using StockLab.Domain.Entities;

namespace StockLab.Application.Interfaces;

public interface IUserProfileService
{
    Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<User?> UpdateAsync(Guid userId, string displayName, string email, CancellationToken cancellationToken);
}
