using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.Auth;
using StockLab.Application.Interfaces;

namespace StockLab.Api.Controllers;

[ApiController, AllowAnonymous, Route("api/auth/password"), Produces("application/json")]
public sealed class PasswordRecoveryController(IPasswordRecoveryRequestQueue queue) : ControllerBase
{
    [HttpPost("forgot"), EnableRateLimiting("PasswordForgot"), Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiRateLimitErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public IActionResult Forgot(ForgotPasswordRequest request) => queue.TryEnqueue(request.Email, request.Language)
        ? Accepted()
        : StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiErrorResponse(
            "password_recovery_unavailable", "Password recovery is temporarily unavailable. Try again later."));

    [HttpPost("reset"), EnableRateLimiting("PasswordReset"), Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiRateLimitErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Reset(ResetPasswordRequest request,
        [FromServices] IUserPasswordRecoveryService recovery, CancellationToken cancellationToken) =>
        await recovery.ResetAsync(request.UserId, request.Token ?? string.Empty, request.NewPassword, cancellationToken)
            ? NoContent()
            : BadRequest(new ApiErrorResponse("invalid_reset_token", "This password reset link is invalid or expired."));
}
