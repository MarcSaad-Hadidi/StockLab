using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using StockLab.Api.DTOs;
using StockLab.Application.Interfaces;

namespace StockLab.Api.Controllers;

[ApiController]
[Route("api/stocks")]
[Produces("application/json")]
public sealed class StocksController(IMarketDataProvider marketDataProvider) : ControllerBase
{
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
