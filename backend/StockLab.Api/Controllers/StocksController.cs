using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using StockLab.Api.DTOs;
using StockLab.Application.Interfaces;
using StockLab.Application.DTOs.MarketData;

namespace StockLab.Api.Controllers;

[ApiController]
[Route("api/stocks")]
[Produces("application/json")]
public sealed class StocksController(IMarketDataProvider marketDataProvider) : ControllerBase
{
    /// <summary>Gets historical bars with opening times in the UTC range [from, to).</summary>
    [HttpGet("{symbol}/history")]
    [ProducesResponseType(typeof(StockHistoryResponse), StatusCodes.Status200OK)]
    // Both validation errors and unsupported intervals share error/message;
    // validation responses additionally include field-level errors.
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<StockHistoryResponse>> GetHistoryAsync(
        [FromRoute, Required] string symbol,
        [FromQuery] StockHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var request = new StockHistoryRequest(normalizedSymbol,
            query.From!.Value, query.To!.Value, query.Interval!.Value);
        var history = await marketDataProvider.GetHistoryAsync(request, cancellationToken);
        if (history is null)
        {
            return NotFound(new ApiErrorResponse(
                "stock_not_found", $"Stock symbol '{normalizedSymbol}' was not found."));
        }

        return Ok(new StockHistoryResponse(history.Symbol, history.Currency, history.Interval.ToString(),
            history.Bars.Select(bar => new StockHistoryBarResponse(
                bar.OpenTimeUtc, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume)).ToArray()));
    }

    /// <summary>Searches stocks by ticker or company name.</summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(StockSearchResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<StockSearchResponse[]>> SearchStocksAsync(
        [FromQuery, Required] string query,
        CancellationToken cancellationToken)
    {
        var results = await marketDataProvider.SearchStocksAsync(query, cancellationToken);
        return Ok(results.Select(stock => new StockSearchResponse(
            stock.Symbol, stock.CompanyName, stock.Exchange, stock.Currency)).ToArray());
    }

    /// <summary>Gets the latest available quote for a stock symbol.</summary>
    [HttpGet("{symbol}/quote")]
    [ProducesResponseType(typeof(StockQuoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<StockQuoteResponse>> GetQuoteAsync(
        [FromRoute, Required] string symbol,
        CancellationToken cancellationToken)
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var quote = await marketDataProvider.GetQuoteAsync(normalizedSymbol, cancellationToken);
        if (quote is null)
        {
            return NotFound(new ApiErrorResponse(
                "stock_not_found", $"Stock symbol '{normalizedSymbol}' was not found."));
        }

        return Ok(new StockQuoteResponse(
            quote.Symbol, quote.Price, quote.Change, quote.ChangePercent, quote.Volume));
    }
}
