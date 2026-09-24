using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;

namespace StockLab.Infrastructure.Identity;

public sealed class JwtAccessTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : IAccessTokenService
{
    public AccessTokenResult CreateAccessToken(User user)
    {
        var jwtOptions = options.Value;
        var issuedAtUtc = timeProvider.GetUtcNow();
        var expiresAtUtc = issuedAtUtc.AddMinutes(jwtOptions.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),
            new Claim(JwtRegisteredClaimNames.Iat,
                issuedAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
        };
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            jwtOptions.Issuer,
            jwtOptions.Audience,
            claims,
            issuedAtUtc.UtcDateTime,
            expiresAtUtc.UtcDateTime,
            credentials);

        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
