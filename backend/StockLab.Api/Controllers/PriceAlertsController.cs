using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockLab.Api.Authentication;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.Alerts;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;

namespace StockLab.Api.Controllers;

[ApiController]
[Route("api/alerts")]
[Produces("application/json")]
[Authorize]
public sealed class PriceAlertsController(IPriceAlertService priceAlertService) : ControllerBase
{
    /// <summary>Lists the authenticated user's alerts, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PriceAlertResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PriceAlertResponse[]>> GetAllAsync(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized(UnauthorizedError());
        var alerts = await priceAlertService.GetAllAsync(userId, cancellationToken);
        return Ok(alerts.Select(ToResponse).ToArray());
    }

    /// <summary>Creates an active USD alert for the authenticated user.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PriceAlertResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PriceAlertResponse>> CreateAsync([FromBody] CreatePriceAlertRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized(UnauthorizedError());
        var alert = await priceAlertService.CreateAsync(userId, request.Symbol, request.Condition, request.TargetPrice, cancellationToken);
        return Created("/api/alerts", ToResponse(alert));
    }

    /// <summary>Changes the condition and target price of an active or disabled alert.</summary>
    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PriceAlertResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PriceAlertResponse>> UpdateAsync(Guid id, [FromBody] UpdatePriceAlertRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized(UnauthorizedError());
        try
        {
            var alert = await priceAlertService.UpdateAsync(userId, id, request.Condition, request.TargetPrice, cancellationToken);
            return alert is null ? NotFound(NotFoundError()) : Ok(ToResponse(alert));
        }
        catch (PriceAlertAlreadyTriggeredException) { return Conflict(TriggeredError()); }
        catch (PriceAlertUpdateConflictException) { return Conflict(UpdateConflictError()); }
    }

    /// <summary>Disables an active alert; repeating this operation leaves it unchanged.</summary>
    [HttpPost("{id:guid}/disable")]
    [ProducesResponseType(typeof(PriceAlertResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PriceAlertResponse>> DisableAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized(UnauthorizedError());
        try
        {
            var alert = await priceAlertService.DisableAsync(userId, id, cancellationToken);
            return alert is null ? NotFound(NotFoundError()) : Ok(ToResponse(alert));
        }
        catch (PriceAlertAlreadyTriggeredException) { return Conflict(TriggeredError()); }
        catch (PriceAlertUpdateConflictException) { return Conflict(UpdateConflictError()); }
    }

    /// <summary>Deletes an owned alert, including a triggered alert.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized(UnauthorizedError());
        try
        {
            return await priceAlertService.DeleteAsync(userId, id, cancellationToken)
                ? NoContent() : NotFound(NotFoundError());
        }
        catch (PriceAlertUpdateConflictException) { return Conflict(UpdateConflictError()); }
    }

    private static ApiErrorResponse UnauthorizedError() => new("unauthorized", "Authentication is required.");
    private static ApiErrorResponse NotFoundError() => new("alert_not_found", "The alert was not found.");
    private static ApiErrorResponse TriggeredError() => new("alert_already_triggered", "A triggered alert cannot be modified.");
    private static ApiErrorResponse UpdateConflictError() => new("alert_update_conflict", "The alert changed while the request was being processed.");
    private static PriceAlertResponse ToResponse(PriceAlert alert) => new(alert.Id, alert.Symbol, alert.Currency,
        alert.Condition, alert.TargetPrice, alert.Status, alert.TriggeredPrice,
        alert.TriggeredAtUtc is { } triggeredAt ? DateTime.SpecifyKind(triggeredAt, DateTimeKind.Utc) : null,
        DateTime.SpecifyKind(alert.CreatedAtUtc, DateTimeKind.Utc), DateTime.SpecifyKind(alert.UpdatedAtUtc, DateTimeKind.Utc));
}
