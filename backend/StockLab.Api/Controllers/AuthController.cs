using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.Auth;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;

namespace StockLab.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IUserRegistrationService userRegistrationService,
    IUserLoginService userLoginService,
    IAccessTokenService accessTokenService) : ControllerBase
{
    /// <summary>Creates a user account.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(RegisterUserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RegisterUserResponse>> RegisterAsync(
        [FromBody] RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await userRegistrationService.RegisterAsync(
                request.DisplayName, request.Email, request.Password, cancellationToken);

            return StatusCode(StatusCodes.Status201Created,
                new RegisterUserResponse(user.Id, user.DisplayName, user.Email, user.CreatedAtUtc));
        }
        catch (UserEmailAlreadyRegisteredException)
        {
            return Conflict(new ApiErrorResponse(
                "email_already_registered", "An account already exists for this email."));
        }
    }

    /// <summary>Authenticates a user and returns a signed access token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(LoginUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LoginUserResponse>> LoginAsync(
        [FromBody] LoginUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userLoginService.AuthenticateAsync(request.Email, request.Password, cancellationToken);
        if (user is null)
        {
            return Unauthorized(new ApiErrorResponse(
                "invalid_credentials", "The email or password is invalid."));
        }

        var accessToken = accessTokenService.CreateAccessToken(user);
        return Ok(new LoginUserResponse(
            accessToken.Token,
            "Bearer",
            accessToken.ExpiresAtUtc,
            new LoginUserIdentityResponse(user.Id, user.DisplayName, user.Email)));
    }
}
