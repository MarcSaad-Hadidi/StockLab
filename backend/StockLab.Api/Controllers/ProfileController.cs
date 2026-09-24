using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockLab.Api.Authentication;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.Profile;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;

namespace StockLab.Api.Controllers;

[ApiController]
[Route("api/profile")]
[Produces("application/json")]
[Authorize]
public sealed class ProfileController(IUserProfileService userProfileService) : ControllerBase
{
    /// <summary>Gets the authenticated user's profile.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UserProfileResponse>> GetAsync(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized(new ApiErrorResponse("unauthorized", "Authentication is required."));
        }

        var user = await userProfileService.GetAsync(userId, cancellationToken);
        return user is null
            ? NotFound(ProfileNotFound())
            : Ok(ToResponse(user));
    }

    /// <summary>Updates the authenticated user's display name and email.</summary>
    [HttpPut]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UserProfileResponse>> UpdateAsync(
        [FromBody] UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized(new ApiErrorResponse("unauthorized", "Authentication is required."));
        }

        try
        {
            var user = await userProfileService.UpdateAsync(
                userId, request.DisplayName, request.Email, cancellationToken);
            return user is null
                ? NotFound(ProfileNotFound())
                : Ok(ToResponse(user));
        }
        catch (UserEmailAlreadyRegisteredException)
        {
            return Conflict(new ApiErrorResponse(
                "email_already_registered", "An account already exists for this email."));
        }
        catch (UserProfileUpdateConflictException)
        {
            return Conflict(new ApiErrorResponse(
                "profile_update_conflict", "The profile was modified by another request."));
        }
    }

    private static ApiErrorResponse ProfileNotFound() =>
        new("profile_not_found", "The profile was not found.");

    private static UserProfileResponse ToResponse(User user) =>
        new(user.Id, user.DisplayName, user.Email,
            DateTime.SpecifyKind(user.CreatedAtUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(user.UpdatedAtUtc, DateTimeKind.Utc));
}
