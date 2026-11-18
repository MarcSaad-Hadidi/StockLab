using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using StockLab.Application.Exceptions;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Identity;

// Process-local, bounded, short-lived proof requests. Restart requires starting Google sign-in again.
public sealed class GoogleLinkTicketStore(TimeProvider timeProvider) : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 4096 });

    internal (string Token, DateTimeOffset ExpiresAtUtc) Issue(User user, string subject, string origin)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var expires = timeProvider.GetUtcNow().AddMinutes(5);
        cache.Set(token, new Ticket(user.Id, subject, user.NormalizedEmail, user.SessionVersion, user.PasswordHash, origin, expires),
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5), Size = 1 });
        return (token, expires);
    }

    internal Ticket Get(string token, string origin)
    {
        if (token.Length != 64 || !cache.TryGetValue<Ticket>(token, out var ticket) || ticket is null
            || ticket.ExpiresAtUtc <= timeProvider.GetUtcNow() || ticket.Origin != origin)
            throw new GoogleAuthenticationException("google_link_expired");
        return ticket;
    }

    internal void Remove(string token) => cache.Remove(token);
    public void Dispose() => cache.Dispose();

    internal sealed class Ticket(Guid userId, string subject, string email, long sessionVersion, string? passwordHash,
        string origin, DateTimeOffset expiresAtUtc)
    {
        internal Guid UserId { get; } = userId;
        internal string Subject { get; } = subject;
        internal string Email { get; } = email;
        internal long SessionVersion { get; } = sessionVersion;
        internal string? PasswordHash { get; } = passwordHash;
        internal string Origin { get; } = origin;
        internal DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal int Attempts { get; set; }
        internal bool Consumed { get; set; }
    }
}
