using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace StockLab.Api.Authentication;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        userId = Guid.Empty;
        var subjects = principal.FindAll(JwtRegisteredClaimNames.Sub).Take(2).ToArray();
        return subjects.Length == 1 && Guid.TryParseExact(subjects[0].Value, "D", out userId) && userId != Guid.Empty;
    }
}
