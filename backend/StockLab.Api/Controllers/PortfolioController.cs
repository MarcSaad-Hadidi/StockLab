using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockLab.Api.Authentication;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.Portfolio;
using StockLab.Application.DTOs.Trading;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;

namespace StockLab.Api.Controllers;

[ApiController]
[Route("api/portfolio")]
[Produces("application/json")]
[Authorize]
public sealed class PortfolioController(
    IPortfolioService portfolioService,
    IPaperTradingEngine paperTradingEngine,
    IMarketDataProvider marketDataProvider) : ControllerBase
{
    private const decimal MaxMoney = 999_999_999_999_999.9999m;

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

    /// <summary>Executes a simulated BUY or SELL order in the authenticated user's portfolio.</summary>
    [HttpPost("trades")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PaperTradeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PaperTradeResponse>> ExecuteTradeAsync(
        [FromBody] PlacePaperTradeRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized(new ApiErrorResponse("unauthorized", "Authentication is required."));
        }

        var portfolioId = await portfolioService.GetPortfolioIdAsync(userId, cancellationToken);
        if (portfolioId is null)
        {
            return NotFound(new ApiErrorResponse("portfolio_not_found", "The portfolio was not found."));
        }

        var existingResult = await paperTradingEngine.GetExistingAsync(
            userId, portfolioId.Value, request.OrderId, cancellationToken);
        if (existingResult is not null)
        {
            if (!MatchesExistingOrder(existingResult, request))
            {
                return Conflict(new ApiErrorResponse(
                    "duplicate_order", "This order conflicts with an existing order."));
            }

            return Ok(PaperTradeResponse.From(existingResult));
        }

        var quote = await marketDataProvider.GetQuoteAsync(request.Symbol, cancellationToken);
        if (quote is null)
        {
            return NotFound(new ApiErrorResponse(
                "stock_not_found", $"Stock symbol '{request.Symbol}' was not found."));
        }

        var executionPrice = decimal.Round(quote.Price, 4, MidpointRounding.AwayFromZero);
        if (executionPrice <= 0m || executionPrice > MaxMoney
            || request.Quantity > MaxMoney / executionPrice)
        {
            return UnprocessableEntity(new ApiErrorResponse(
                "invalid_order", "The order amount is outside the supported range."));
        }

        if (request.OrderType == "limit")
        {
            var limitPrice = decimal.Round(request.LimitPrice!.Value, 4, MidpointRounding.AwayFromZero);
            var limitReached = request.Side == "BUY"
                ? executionPrice <= limitPrice
                : executionPrice >= limitPrice;
            if (!limitReached)
            {
                return UnprocessableEntity(new ApiErrorResponse(
                    "limit_not_reached", "The current market price does not meet the limit price."));
            }
        }

        try
        {
            var result = await paperTradingEngine.ExecuteAsync(
                userId,
                portfolioId.Value,
                new Application.DTOs.Trading.PaperTradeRequest(
                    request.OrderId, request.Side, quote.Symbol, request.Quantity, executionPrice),
                cancellationToken);
            return Ok(PaperTradeResponse.From(result));
        }
        catch (PaperTradingException exception)
        {
            return exception.Category switch
            {
                PaperTradingFailure.PortfolioNotFound => NotFound(
                    new ApiErrorResponse("portfolio_not_found", "The portfolio was not found.")),
                PaperTradingFailure.InsufficientCash => UnprocessableEntity(
                    new ApiErrorResponse("insufficient_cash", "There is not enough cash for this order.")),
                PaperTradingFailure.InsufficientHoldings => UnprocessableEntity(
                    new ApiErrorResponse("insufficient_holdings", "There are not enough shares for this order.")),
                PaperTradingFailure.DuplicateOrder => Conflict(
                    new ApiErrorResponse("duplicate_order", "This order conflicts with an existing order.")),
                PaperTradingFailure.ConcurrencyConflict => Conflict(
                    new ApiErrorResponse("concurrency_conflict", "The portfolio changed while this order was executing.")),
                PaperTradingFailure.LimitPriceNotReached => UnprocessableEntity(
                    new ApiErrorResponse("limit_not_reached", "The current market price does not meet the limit price.")),
                _ => BadRequest(new ApiErrorResponse("invalid_order", "The order could not be executed."))
            };
        }
    }

    private static bool MatchesExistingOrder(
        PaperTradeResult existing,
        PlacePaperTradeRequest request) =>
        existing.Side == request.Side
        && existing.Symbol == request.Symbol
        && existing.Quantity == decimal.Round(request.Quantity, 8, MidpointRounding.AwayFromZero);
}
