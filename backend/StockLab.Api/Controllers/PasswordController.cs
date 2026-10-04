using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StockLab.Api.Authentication;
using StockLab.Api.Configuration;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.Auth;
using StockLab.Application.Interfaces;

namespace StockLab.Api.Controllers;

[ApiController]
[Route("api/auth/password")]
[Authorize]
[Produces("application/json")]
public sealed class PasswordController(IUserPasswordService userPasswordService) : ControllerBase
{
    /// <summary>Changes the authenticated user's password after verifying the current password.</summary>
    [HttpPut]
    [EnableRateLimiting(LoginRateLimitOptions.PolicyName)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ChangeAsync([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new ApiErrorResponse("unauthorized", "Authentication is required."));
        var result = await userPasswordService.ChangeAsync(userId, request.CurrentPassword, request.NewPassword, cancellationToken);
        return result switch
        {
            PasswordChangeResult.Changed => NoContent(),
            PasswordChangeResult.UserNotFound => NotFound(new ApiErrorResponse("user_not_found", "The account was not found.")),
            PasswordChangeResult.InvalidCurrentPassword => BadRequest(new ApiErrorResponse("invalid_current_password", "The current password is incorrect.")),
            _ => Conflict(new ApiErrorResponse("password_update_conflict", "The account changed. Please try again."))
        };
    }
}
