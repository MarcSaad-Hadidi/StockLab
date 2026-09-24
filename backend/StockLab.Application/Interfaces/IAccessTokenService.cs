using StockLab.Domain.Entities;

namespace StockLab.Application.Interfaces;

public interface IAccessTokenService
{
    AccessTokenResult CreateAccessToken(User user);
}

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAtUtc);
