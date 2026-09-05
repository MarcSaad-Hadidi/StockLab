using Microsoft.AspNetCore.Mvc;
using StockLab.Api.DTOs;
using StockLab.Application.Interfaces;

namespace StockLab.Api.Controllers;

[ApiController]
[Route("api/stocks")]
[Produces("application/json")]
public sealed class StocksController(IMarketDataProvider marketDataProvider) : ControllerBase
{
    /// <summary>Gets the latest available quote for a stock symbol.</summary>
    [HttpGet("{symbol}/quote")]
    [ProducesResponseType(typeof(StockQuoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StockQuoteErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(StockQuoteErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockQuoteResponse>> GetQuoteAsync(
        [FromRoute] string? symbol,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return BadRequest(new StockQuoteErrorResponse("invalid_symbol", "A nonblank stock symbol is required."));
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        try
        {
            var quote = await marketDataProvider.GetQuoteAsync(normalizedSymbol, cancellationToken);
            if (quote is null)
            {
                return NotFound(new StockQuoteErrorResponse(
                    "stock_not_found", $"Stock symbol '{normalizedSymbol}' was not found."));
            }

            return Ok(new StockQuoteResponse(
                quote.Symbol, quote.Price, quote.Change, quote.ChangePercent, quote.Volume));
        }
        catch (ArgumentException)
        {
            return BadRequest(new StockQuoteErrorResponse("invalid_symbol", "The stock symbol is invalid."));
        }
    }
}
