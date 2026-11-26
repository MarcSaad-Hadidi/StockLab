using StockLab.Application.DTOs;
using StockLab.Domain.Entities;

namespace StockLab.Application.Interfaces;

public interface IUserProfileService
{
    Task<User?> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<User?> UpdateAsync(Guid userId, string displayName, string email, CancellationToken cancellationToken);

    Task<User?> UpdatePreferencesAsync(Guid userId, UserPreferencesPatch patch, CancellationToken cancellationToken);

    Task<User?> UpdateLanguageAsync(Guid userId, string preferredLanguage, CancellationToken cancellationToken,
        byte[]? expectedVersion = null);
}
