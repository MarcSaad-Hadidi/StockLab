using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using StockLab.Api.Authentication;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.Portfolio;
using StockLab.Application.DTOs.Trading;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Application.Trading;

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
                portfolio.InitialCapital,
                portfolio.InvestedValue,
                portfolio.TotalValue,
                portfolio.Currency,
                portfolio.Positions.Select(position => new PortfolioPositionResponse(
                    position.Symbol, position.Quantity, position.AverageCost)).ToArray()));
    }

    /// <summary>Gets the authenticated user's portfolio valued with current market quotes.</summary>
    [HttpGet("performance")]
    [ProducesResponseType(typeof(PortfolioPerformanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<PortfolioPerformanceResponse>> GetPerformanceAsync(
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new ApiErrorResponse("unauthorized", "Authentication is required."));

        var performance = await portfolioService.GetPerformanceAsync(userId, cancellationToken);
        return performance is null
            ? NotFound(new ApiErrorResponse("portfolio_not_found", "The portfolio was not found."))
            : Ok(new PortfolioPerformanceResponse(
                performance.CashBalance,
                performance.InitialCapital,
                performance.InvestedValue,
                performance.PositionsMarketValue,
                performance.TotalValue,
                performance.TotalPnl,
                performance.ReturnPercent,
                performance.Currency,
                performance.Positions.Select(position => new PortfolioPerformancePositionResponse(
                    position.Symbol,
                    position.Quantity,
                    position.AverageCost,
                    position.CurrentPrice,
                    position.MarketValue,
                    position.Pnl,
                    position.PnlPercent)).ToArray()));
    }

    /// <summary>Gets recent simulated transactions for the authenticated user's portfolio.</summary>
    [HttpGet("transactions")]
    [ProducesResponseType(typeof(PortfolioTransactionResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PortfolioTransactionResponse[]>> GetRecentTransactionsAsync(
        [FromQuery, Range(1, 50)] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized(new ApiErrorResponse("unauthorized", "Authentication is required."));
        }

        var transactions = await portfolioService.GetRecentTransactionsAsync(userId, limit, cancellationToken);
        return transactions is null
            ? NotFound(new ApiErrorResponse("portfolio_not_found", "The portfolio was not found."))
            : Ok(transactions.Select(transaction => new PortfolioTransactionResponse(
                transaction.Id,
                transaction.Side,
                transaction.Symbol,
                transaction.Quantity,
                transaction.ExecutionPrice,
                transaction.TotalAmount,
                DateTime.SpecifyKind(transaction.ExecutedAtUtc, DateTimeKind.Utc))).ToArray());
    }

    /// <summary>Gets a filtered page of the full history with all-time aggregates and portfolio currency.</summary>
    [HttpGet("transactions/history")]
    [ProducesResponseType(typeof(TransactionHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionHistoryResponse>> GetTransactionHistoryAsync(
        [FromQuery] TransactionHistoryRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new ApiErrorResponse("unauthorized", "Authentication is required."));

        var history = await portfolioService.GetTransactionHistoryAsync(userId, request.ToQuery(), cancellationToken);
        return history is null
            ? NotFound(new ApiErrorResponse("portfolio_not_found", "The portfolio was not found."))
            : Ok(new TransactionHistoryResponse(history.Items.Select(row => new PortfolioTransactionResponse(
                row.Id, row.Side, row.Symbol, row.Quantity, row.ExecutionPrice, row.TotalAmount,
                DateTime.SpecifyKind(row.ExecutedAtUtc, DateTimeKind.Utc))).ToArray(),
                history.Page, history.PageSize, history.TotalCount, history.Currency, history.Summary));
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
            if (!MatchesExistingOrderTerms(existingResult, request))
            {
                return Conflict(new ApiErrorResponse(
                    "duplicate_order", "This order conflicts with an existing order."));
            }

            // The original request can be replayed even when market data is unavailable.
            if (request.Symbol == (existingResult.RequestedSymbol ?? existingResult.Symbol)
                || request.Symbol == existingResult.Symbol)
            {
                return Ok(PaperTradeResponse.From(existingResult));
            }
        }

        var quote = await marketDataProvider.GetQuoteAsync(request.Symbol, cancellationToken);
        if (quote is null)
        {
            return NotFound(new ApiErrorResponse(
                "stock_not_found", $"Stock symbol '{request.Symbol}' was not found."));
        }

        var tradingSymbol = TradingSymbol.FromQuote(quote);
        if (tradingSymbol is null)
        {
            return UnprocessableEntity(new ApiErrorResponse(
                "invalid_order", "The stock listing could not be identified."));
        }

        if (existingResult is not null)
        {
            // A different alias is equivalent only if the provider resolves the same listing.
            var existingSymbol = existingResult.Symbol;
            if (!existingSymbol.Contains(':'))
            {
                var legacyQuote = await marketDataProvider.GetQuoteAsync(existingSymbol, cancellationToken);
                existingSymbol = legacyQuote is null ? null : TradingSymbol.FromQuote(legacyQuote);
            }
            return existingSymbol == tradingSymbol
                ? Ok(PaperTradeResponse.From(existingResult))
                : Conflict(new ApiErrorResponse("duplicate_order", "This order conflicts with an existing order."));
        }

        var portfolioCurrency = await portfolioService.GetPortfolioCurrencyAsync(userId, cancellationToken);
        if (portfolioCurrency is null)
        {
            return NotFound(new ApiErrorResponse("portfolio_not_found", "The portfolio was not found."));
        }

        if (!string.Equals(quote.Currency.Trim(), portfolioCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return UnprocessableEntity(new ApiErrorResponse(
                "currency_mismatch", "The stock quote currency does not match your portfolio currency."));
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
                    request.OrderId, request.Side, tradingSymbol, request.Quantity, executionPrice)
                {
                    RequestedSymbol = request.Symbol,
                    OrderType = request.OrderType,
                    LimitPrice = request.OrderType == "limit"
                        ? decimal.Round(request.LimitPrice!.Value, 4, MidpointRounding.AwayFromZero)
                        : null
                },
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

    private static bool MatchesExistingOrderTerms(
        PaperTradeResult existing,
        PlacePaperTradeRequest request) =>
        existing.Side == request.Side
        && existing.Quantity == decimal.Round(request.Quantity, 8, MidpointRounding.AwayFromZero)
        && existing.OrderType == request.OrderType
        && existing.LimitPrice == (request.OrderType == "limit"
            ? decimal.Round(request.LimitPrice!.Value, 4, MidpointRounding.AwayFromZero)
            : null);
}
