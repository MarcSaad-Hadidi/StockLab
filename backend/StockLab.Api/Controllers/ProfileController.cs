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
            : ProfileResult(user);
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
                : ProfileResult(user);
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

    /// <summary>Sets the authenticated user's account language without changing identity fields.</summary>
    [HttpPut("language")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UserProfileResponse>> UpdateLanguageAsync(
        [FromBody] UpdateUserLanguageRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new ApiErrorResponse("unauthorized", "Authentication is required."));
        if (!TryReadExpectedVersion(out var expectedVersion))
            return BadRequest(new ApiValidationErrorResponse("validation_error", "The request contains invalid data.",
                new Dictionary<string, string[]> { ["If-Match"] = ["Supply one strong ETag returned by the profile API."] }));
        try
        {
            var user = await userProfileService.UpdateLanguageAsync(userId, request.PreferredLanguage, cancellationToken,
                expectedVersion);
            return user is null ? NotFound(ProfileNotFound()) : ProfileResult(user);
        }
        catch (UserProfileUpdateConflictException)
        {
            return StatusCode(expectedVersion is null ? StatusCodes.Status409Conflict : StatusCodes.Status412PreconditionFailed,
                new ApiErrorResponse("profile_update_conflict", "The profile was modified by another request."));
        }
    }

    private ActionResult<UserProfileResponse> ProfileResult(User user)
    {
        Response.Headers.ETag = $"\"{Convert.ToBase64String(user.Version)}\"";
        return Ok(ToResponse(user));
    }

    private bool TryReadExpectedVersion(out byte[]? version)
    {
        version = null;
        if (!Request.Headers.TryGetValue("If-Match", out var values)) return true;
        // SQL rowversions are exactly 8 bytes: 12 base64 characters plus quotes.
        // Bound input before decoding; weak, wildcard and multiple tags are unsupported.
        if (values.Count != 1 || values[0] is not { Length: 14 } value
            || value[0] != '"' || value[^1] != '"') return false;
        Span<byte> decoded = stackalloc byte[8];
        var encoded = value[1..^1];
        if (!Convert.TryFromBase64String(encoded, decoded, out var count) || count != 8
            || Convert.ToBase64String(decoded) != encoded) return false;
        version = decoded.ToArray();
        return true;
    }

    private static ApiErrorResponse ProfileNotFound() =>
        new("profile_not_found", "The profile was not found.");

    private static UserProfileResponse ToResponse(User user) =>
        new(user.Id, user.DisplayName, user.Email,
            DateTime.SpecifyKind(user.CreatedAtUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(user.UpdatedAtUtc, DateTimeKind.Utc), user.PreferredLanguage);
}
