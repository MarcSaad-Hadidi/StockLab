using StockLab.Domain.Entities;

namespace StockLab.Application.Interfaces;

public sealed record GoogleIdentity(string Subject, string Email, string DisplayName);
public sealed record GoogleAuthenticationResult(User? User = null, string? LinkToken = null, DateTimeOffset? ExpiresAtUtc = null);

public interface IGoogleIdentityProvider
{
    Task<GoogleIdentity> ExchangeAsync(string code, string origin, CancellationToken cancellationToken);
}

public interface IGoogleAuthenticationService
{
    Task<GoogleAuthenticationResult> ExchangeAsync(string code, string origin, string language, CancellationToken cancellationToken);
    Task<User> LinkAsync(string token, string password, string origin, CancellationToken cancellationToken);
}
