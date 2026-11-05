using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockLab.Api.Authentication;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.Portfolio;
using StockLab.Application.Interfaces;

namespace StockLab.Api.Controllers;

[ApiController, Authorize, Route("api/portfolio/performance/history")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PortfolioPerformanceHistoryController(IPortfolioPerformanceHistoryService history) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PortfolioPerformanceHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortfolioPerformanceHistoryResponse>> GetAsync(
        [FromQuery] PortfolioPerformanceHistoryRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new ApiErrorResponse("unauthorized", "Authentication is required."));
        var result = await history.GetAsync(userId, request.Range, cancellationToken);
        return result is null ? NotFound(new ApiErrorResponse("portfolio_not_found", "The portfolio was not found."))
            : Ok(new PortfolioPerformanceHistoryResponse(result.Range, result.Currency, result.FromUtc,
                result.ToUtc, result.SampleCount, result.Resolution, result.FirstRecordedAtUtc, result.LastRecordedAtUtc,
                result.Points.Select(point => new PortfolioPerformanceHistoryPointResponse(
                    point.RecordedAtUtc, point.TotalValue.ToString("0.############", CultureInfo.InvariantCulture),
                    point.Source, point.QuoteAsOfUtc)).ToArray()));
    }
}
