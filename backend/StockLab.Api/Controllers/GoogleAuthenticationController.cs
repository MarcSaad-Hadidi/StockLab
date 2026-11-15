using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using StockLab.Api.Configuration;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.Auth;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Identity;

namespace StockLab.Api.Controllers;

[ApiController, AllowAnonymous, Route("api/auth/google"), Produces("application/json")]
public sealed class GoogleAuthenticationController(IOptions<GoogleAuthOptions> options) : ControllerBase
{
    [HttpGet("config")]
    public IActionResult Config([FromQuery] string? origin)
    {
        Response.Headers.CacheControl = "no-store";
        return options.Value.Enabled && options.Value.AllowsOrigin(origin)
            ? Ok(new { enabled = true, clientId = options.Value.ClientId }) : Ok(new { enabled = false });
    }

    [HttpPost("exchange"), Consumes("application/json"), EnableRateLimiting("GoogleOAuth")]
    public async Task<IActionResult> Exchange(GoogleExchangeRequest request, CancellationToken cancellationToken)
    {
        var rejected = ValidateRequest();
        if (rejected is not null) return rejected;
        try
        {
            var result = await HttpContext.RequestServices.GetRequiredService<IGoogleAuthenticationService>()
                .ExchangeAsync(request.Code, Request.Headers.Origin.ToString(), request.Language, cancellationToken);
            return result.User is { } user ? Login(user) : Conflict(new
            { error = "google_link_required", linkToken = result.LinkToken, expiresAtUtc = result.ExpiresAtUtc });
        }
        catch (GoogleAuthenticationException exception) { return Error(exception.Error); }
    }

    [HttpPost("link"), Consumes("application/json"), EnableRateLimiting(LoginRateLimitOptions.PolicyName)]
    public async Task<IActionResult> Link(GoogleLinkRequest request, CancellationToken cancellationToken)
    {
        var rejected = ValidateRequest();
        if (rejected is not null) return rejected;
        try
        {
            return Login(await HttpContext.RequestServices.GetRequiredService<IGoogleAuthenticationService>()
                .LinkAsync(request.LinkToken, request.Password, Request.Headers.Origin.ToString(), cancellationToken));
        }
        catch (GoogleAuthenticationException exception) { return Error(exception.Error); }
    }

    private IActionResult? ValidateRequest()
    {
        Response.Headers.CacheControl = "no-store";
        if (!options.Value.Enabled) return Error("google_unavailable");
        if (Request.Headers.Origin.Count != 1 || !options.Value.AllowsOrigin(Request.Headers.Origin.ToString())
            || Request.Headers["X-Requested-With"].Count != 1 || Request.Headers["X-Requested-With"].ToString() != "StockLab.GoogleOAuth")
            return BadRequest(new ApiValidationErrorResponse("validation_error", "The request contains invalid data.",
                new Dictionary<string, string[]> { ["origin"] = ["Use an allowed origin and the Google OAuth request header."] }));
        return null;
    }

    private IActionResult Login(User user)
    {
        var token = HttpContext.RequestServices.GetRequiredService<IAccessTokenService>().CreateAccessToken(user);
        return Ok(new LoginUserResponse(token.Token, "Bearer", token.ExpiresAtUtc,
            new LoginUserIdentityResponse(user.Id, user.DisplayName, user.Email, user.PreferredLanguage)));
    }

    private ObjectResult Error(string error) => StatusCode(error switch
    {
        "google_unavailable" => 503,
        "google_account_conflict" => 409,
        "google_link_expired" => 400,
        _ => 401
    }, new ApiErrorResponse(error, error switch
    {
        "google_unavailable" => "Google sign-in is currently unavailable.",
        "google_account_conflict" => "This Google account cannot be linked to this account.",
        "google_link_expired" => "The linking request expired. Start Google sign-in again.",
        "invalid_credentials" => "The password is invalid.",
        _ => "Google could not verify this sign-in. Try again."
    }));
}
