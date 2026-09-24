using Microsoft.AspNetCore.Mvc;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.Auth;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;

namespace StockLab.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(IUserRegistrationService userRegistrationService) : ControllerBase
{
    /// <summary>Creates a user account.</summary>
    [HttpPost("register")]
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
}
