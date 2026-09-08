using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using StockLab.Api.DTOs;
using StockLab.Application.DTOs.MarketEnrichment;
using StockLab.Application.Interfaces;
namespace StockLab.Api.Controllers;

[ApiController]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), 400)]
[ProducesResponseType(typeof(ApiErrorResponse), 404)]
[ProducesResponseType(typeof(ApiErrorResponse), 429)]
[ProducesResponseType(typeof(ApiErrorResponse), 502)]
[ProducesResponseType(typeof(ApiErrorResponse), 503)]
[ProducesResponseType(typeof(ApiErrorResponse), 504)]
public sealed class MarketEnrichmentController(IMarketEnrichmentProvider enrichment) : ControllerBase
{
    [HttpGet("api/stocks/{symbol}/fundamentals")]
    [ProducesResponseType(typeof(StockFundamentals), 200)]
    public async Task<ActionResult<StockFundamentals>> Fundamentals([FromRoute, Required] string symbol, CancellationToken token)
    {
        var result = await enrichment.GetFundamentalsAsync(symbol, token);
        return result is null ? NotFound(new ApiErrorResponse("stock_not_found", "Stock fundamentals were not found.")) : Ok(result);
    }
    [HttpGet("api/stocks/{symbol}/logo")]
    [ProducesResponseType(typeof(CompanyLogo), 200)]
    public async Task<ActionResult<CompanyLogo>> Logo([FromRoute, Required] string symbol, CancellationToken token) => Ok(await enrichment.GetLogoAsync(symbol, token));
    [HttpGet("api/stocks/{symbol}/earnings")]
    [ProducesResponseType(typeof(StockEarnings), 200)]
    public async Task<ActionResult<StockEarnings>> Earnings([FromRoute, Required] string symbol, CancellationToken token) => Ok(await enrichment.GetEarningsAsync(symbol, token));
    [HttpGet("api/market/movers")]
    [ProducesResponseType(typeof(MarketMovers), 200)]
    public async Task<ActionResult<MarketMovers>> Movers(CancellationToken token) => Ok(await enrichment.GetMoversAsync(token));
}
