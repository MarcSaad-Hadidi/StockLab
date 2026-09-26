using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockLab.Api.Authentication;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.Portfolio;
using StockLab.Application.Interfaces;

namespace StockLab.Api.Controllers;

[ApiController]
[Route("api/portfolio")]
[Produces("application/json")]
[Authorize]
public sealed class PortfolioController(IPortfolioService portfolioService) : ControllerBase
{
    /// <summary>Gets the authenticated user's portfolio valued at acquisition cost.</summary>
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

        var portfolio = await portfolioService.GetPortfolioAsync(userId, cancellationToken);
        return portfolio is null
            ? NotFound(new ApiErrorResponse("portfolio_not_found", "The portfolio was not found."))
            : Ok(new PortfolioResponse(
                portfolio.CashBalance,
                portfolio.InvestedValue,
                portfolio.TotalValue,
                portfolio.Currency,
                portfolio.Positions.Select(position => new PortfolioPositionResponse(
                    position.Symbol, position.Quantity, position.AverageCost)).ToArray()));
    }
}
