using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Identity;

namespace StockLab.Api.Authentication;

internal static class SessionTokenValidation
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var endpoint = context.HttpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || (endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count is not > 0
                && endpoint?.Metadata.GetMetadata<AuthorizationPolicy>() is null
                && await context.HttpContext.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>()
                    .GetFallbackPolicyAsync() is null))
        {
            // Public routes need no session store and must not receive an unchecked authenticated principal.
            context.NoResult();
            return;
        }
        var principal = context.Principal;
        var versions = principal?.FindAll(JwtAccessTokenService.SessionVersionClaim).Take(2).ToArray();
        if (principal is null || !principal.TryGetUserId(out var userId) || versions is not { Length: 1 }
            || !long.TryParse(versions[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || versions[0].Value != version.ToString(CultureInfo.InvariantCulture))
        {
            context.Fail("Invalid session.");
            return;
        }
        // Do not cache this lookup: revocation must be observed by the next request on every API instance.
        // Unexpected storage failures propagate to the sanitized 500 handler, without logging the user out.
        var validator = context.HttpContext.RequestServices.GetRequiredService<IUserSessionValidator>();
        if (!await validator.IsCurrentAsync(userId, version, context.HttpContext.RequestAborted))
            context.Fail("Invalid session.");
    }
}
