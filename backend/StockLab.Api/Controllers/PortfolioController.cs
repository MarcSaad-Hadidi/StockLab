using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockLab.Api.Authentication;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.Portfolio;
using StockLab.Application.DTOs.Portfolio;
using StockLab.Application.Interfaces;

namespace StockLab.Api.Controllers;

[ApiController]
[Route("api/portfolio")]
[Produces("application/json")]
[Authorize]
public sealed class PortfolioController(IPortfolioService portfolioService) : ControllerBase
{
    /// <summary>Gets the authenticated user's paper-trading portfolio.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PortfolioResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PortfolioResponse>> GetAsync(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized(new ApiErrorResponse("unauthorized", "Authentication is required."));
        }

        var summary = await portfolioService.GetAsync(userId, cancellationToken);
        return summary is null
            ? NotFound(new ApiErrorResponse("portfolio_not_found", "The portfolio was not found."))
            : Ok(ToResponse(summary));
    }

    private static PortfolioResponse ToResponse(PortfolioSummary summary) => new(
        summary.PortfolioId,
        summary.Currency,
        summary.InitialCapital,
        summary.CashAvailable,
        summary.InvestedValue,
        summary.TotalValue,
        summary.Positions.Select(position => new PortfolioPositionResponse(
            position.Symbol,
            position.Quantity,
            position.AveragePrice,
            position.Value)).ToArray());
}
