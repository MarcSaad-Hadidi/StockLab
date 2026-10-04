using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockLab.Api.Authentication;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.Watchlist;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;

namespace StockLab.Api.Controllers;

[ApiController]
[Route("api/watchlist")]
[Produces("application/json")]
[Authorize]
public sealed class WatchlistController(IWatchlistService watchlistService) : ControllerBase
{
    /// <summary>Lists the authenticated user's watchlist, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(WatchlistItemResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<WatchlistItemResponse[]>> GetAsync(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized(UnauthorizedError());
        }

        var items = await watchlistService.GetAsync(userId, cancellationToken);
        return Ok(items.Select(ToResponse).ToArray());
    }

    /// <summary>Adds a symbol to the authenticated user's watchlist.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WatchlistItemResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<WatchlistItemResponse>> AddAsync(
        [FromBody] AddWatchlistItemRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized(UnauthorizedError());
        }

        try
        {
            var item = await watchlistService.AddAsync(userId, request.Symbol, cancellationToken);
            return Created("/api/watchlist", ToResponse(item));
        }
        catch (WatchlistItemAlreadyExistsException)
        {
            return Conflict(new ApiErrorResponse(
                "watchlist_item_already_exists", "This symbol is already in your watchlist."));
        }
    }

    /// <summary>Removes a symbol from the authenticated user's watchlist.</summary>
    [HttpDelete("{symbol}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RemoveAsync([FromRoute] string symbol, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized(UnauthorizedError());
        }

        var trimmedSymbol = symbol.Trim();
        if (string.IsNullOrWhiteSpace(trimmedSymbol) || trimmedSymbol.Length > 32 || trimmedSymbol.Contains('/')
            || trimmedSymbol is "." or ".." || trimmedSymbol.Any(char.IsControl))
        {
            return BadRequest(new ApiValidationErrorResponse(
                "validation_error", "The request contains invalid data.",
                new Dictionary<string, string[]> { ["symbol"] = ["The value is missing or invalid."] }));
        }

        return await watchlistService.RemoveAsync(userId, trimmedSymbol, cancellationToken)
            ? NoContent()
            : NotFound(new ApiErrorResponse("watchlist_item_not_found", "The symbol is not in your watchlist."));
    }

    private static ApiErrorResponse UnauthorizedError() =>
        new("unauthorized", "Authentication is required.");

    private static WatchlistItemResponse ToResponse(Watchlist item) =>
        new(item.Symbol, DateTime.SpecifyKind(item.CreatedAtUtc, DateTimeKind.Utc));
}
