using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StockLab.Api.Configuration;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.AiTrader;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;

namespace StockLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ai-trader")]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiValidationErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
public sealed class AiTraderController(IAiTraderPerformanceService performance,
    IAiTraderPortfolioService portfolio, IAiCurrentPositionsService positions, IAiTradesHistoryService trades,
    IAiDecisionHistoryService decisions, IAiDecisionOutcomeReadService outcomes,
    IAiRejectedDecisionHistoryService rejections, IAiTraderValuationHistoryService history,
    IAiModelCatalogReadService models, IAiBacktestCatalogReadService backtests,
    IOptions<MarketDataProviderOptions> marketData, IHostEnvironment environment) : ControllerBase
{
    private const string ValuationContract = "Read-only system-owned USD portfolio. Money and quantities are invariant decimal strings; return, winRate and maxDrawdown are numeric fractions (0.12 = 12%). Nullable metrics remain null. Technical timestamps are UTC ISO 8601. No portfolio initialization or historical capture. Missing portfolio: 404; unavailable quotes: controlled provider error.";

    [HttpGet("overview")]
    [EndpointSummary("Current AI Trader overview")]
    [EndpointDescription(ValuationContract)]
    [ProducesResponseType(typeof(AiTraderOverviewResponse), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 409)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    [ProducesResponseType(typeof(ApiErrorResponse), 502)]
    [ProducesResponseType(typeof(ApiErrorResponse), 504)]
    public async Task<ActionResult<AiTraderOverviewResponse>> Overview(CancellationToken cancellationToken)
    {
        RequireRealMarketData();
        return Ok(AiTraderOverviewResponse.From(await performance.GetMetricsAsync(cancellationToken)));
    }

    [HttpGet("positions")]
    [EndpointSummary("Open AI positions with current unrealized P&L")]
    [EndpointDescription(ValuationContract + " Existing empty portfolio returns 200 []. All prices must be available in portfolio currency.")]
    [ProducesResponseType(typeof(AiTraderPositionResponse[]), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    [ProducesResponseType(typeof(ApiErrorResponse), 502)]
    [ProducesResponseType(typeof(ApiErrorResponse), 504)]
    public async Task<ActionResult<AiTraderPositionResponse[]>> Positions(CancellationToken cancellationToken)
    {
        RequireRealMarketData();
        // #71 uses InvalidOperationException for both missing state and invalid quotes; distinguish before valuation.
        try { await portfolio.GetStateAsync(cancellationToken, initializeIfMissing: false); }
        catch (InvalidOperationException ex) when (ex.GetBaseException() is not DbException)
        {
            throw new AiTraderPerformanceException(AiTraderPerformanceFailure.PortfolioNotFound);
        }
        return Ok((await positions.GetCurrentAsync(cancellationToken)).Select(AiTraderPositionResponse.From).ToArray());
    }

    [HttpGet("trades")]
    [EndpointSummary("Recent committed AI trades, newest first")]
    [EndpointDescription("Actual execution prices and original model identities; no market requests. Money and quantities are decimal strings. BUY and unvalued legacy SELL realizedPnL remain null; unlinked legacy decision remains null. Empty history returns 200 [].")]
    [ProducesResponseType(typeof(AiTraderTradeResponse[]), 200)]
    public async Task<ActionResult<AiTraderTradeResponse[]>> Trades(
        [FromQuery, Range(1, 200), Description("Maximum 1..200 trades. Default 20.")] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Ok((await trades.GetRecentAsync(limit, cancellationToken)).Select(AiTraderTradeResponse.From).ToArray());

    [HttpGet("trades/{tradeId}")]
    [EndpointSummary("Persisted AI trade details")]
    [EndpointDescription("Exact immutable execution and original decision, with nullable realizedPnL and legacy association. Monetary decimals are strings; confidence is a fraction. A nonempty GUID is required; unknown ID returns 404.")]
    [ProducesResponseType(typeof(AiTraderTradeResponse), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    public async Task<ActionResult<AiTraderTradeResponse>> Trade(Guid tradeId, CancellationToken cancellationToken)
    {
        if (tradeId == Guid.Empty) return BadRequest(new ApiValidationErrorResponse("invalid_request",
            "A nonempty trade ID is required.", new Dictionary<string, string[]> { [nameof(tradeId)] = ["The value is missing or invalid."] }));
        var value = await trades.GetByIdAsync(tradeId, cancellationToken);
        return value is null ? NotFound(new ApiErrorResponse("ai_trader_trade_not_found", "The AI trade was not found."))
            : Ok(AiTraderTradeResponse.From(value));
    }

    [HttpGet("decisions")]
    [EndpointSummary("Original ML decisions with persisted outcomes")]
    [EndpointDescription("Newest first by decisionDate, recordedAtUtc and decisionId. Outcomes: EXECUTED, REJECTED, HOLD or UNRESOLVED. Confidence is a numeric fraction. decisionDate is YYYY-MM-DD; recordedAtUtc is UTC. Exact historical modelVersion is preserved. Empty history returns 200 [].")]
    [ProducesResponseType(typeof(AiTraderDecisionResponse[]), 200)]
    public async Task<ActionResult<AiTraderDecisionResponse[]>> Decisions(
        [FromQuery, Range(1, 200), Description("Maximum 1..200 decisions. Default 20.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var values = await decisions.GetRecentAsync(limit, cancellationToken);
        var results = await outcomes.GetAsync(values.Select(d => d.Id).ToArray(), cancellationToken);
        return Ok(values.Select(d => AiTraderDecisionResponse.From(d, Outcome(results, d.Id))).ToArray());
    }

    [HttpGet("decisions/{decisionId}")]
    [EndpointSummary("Original ML decision and recorded execution or rejection")]
    [EndpointDescription("Nonempty GUID required; unknown ID returns 404. No risk evaluation or prediction runs. Includes official rejection code or tradeId when persisted, numeric confidence fraction and exact historical model identity.")]
    [ProducesResponseType(typeof(AiTraderDecisionResponse), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    public async Task<ActionResult<AiTraderDecisionResponse>> Decision(Guid decisionId, CancellationToken cancellationToken)
    {
        if (decisionId == Guid.Empty) return BadRequest(new ApiValidationErrorResponse("invalid_request",
            "A nonempty decision ID is required.", new Dictionary<string, string[]> { [nameof(decisionId)] = ["The value is missing or invalid."] }));
        var value = await decisions.GetByIdAsync(decisionId, cancellationToken);
        if (value is null) return NotFound(new ApiErrorResponse("ai_trader_decision_not_found", "The AI decision was not found."));
        var results = await outcomes.GetAsync([decisionId], cancellationToken);
        return Ok(AiTraderDecisionResponse.From(value, Outcome(results, decisionId)));
    }

    [HttpGet("rejected-decisions")]
    [EndpointSummary("Recent recorded AI risk rejections")]
    [EndpointDescription("Newest first by rejectedAtUtc, decisionDate and decisionId; official rejection codes and original model identity. Confidence is a fraction; technical timestamps are UTC. Empty history returns 200 [].")]
    [ProducesResponseType(typeof(AiTraderRejectedDecisionResponse[]), 200)]
    public async Task<ActionResult<AiTraderRejectedDecisionResponse[]>> RejectedDecisions(
        [FromQuery, Range(1, 200), Description("Maximum 1..200 rejections. Default 20.")] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Ok((await rejections.GetRecentAsync(limit, cancellationToken)).Select(AiTraderRejectedDecisionResponse.From).ToArray());

    [HttpGet("performance")]
    [EndpointSummary("Complete current AI paper trading performance")]
    [EndpointDescription(ValuationContract + " Realized statistics with incomplete legacy SELL coverage remain null. Drawdown describes persisted samples only; historicalCoverage states their extent.")]
    [ProducesResponseType(typeof(AiTraderPerformanceResponse), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 409)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    [ProducesResponseType(typeof(ApiErrorResponse), 502)]
    [ProducesResponseType(typeof(ApiErrorResponse), 504)]
    public async Task<ActionResult<AiTraderPerformanceResponse>> Performance(CancellationToken cancellationToken)
    {
        RequireRealMarketData();
        return Ok(AiTraderPerformanceResponse.From(await performance.GetMetricsAsync(cancellationToken)));
    }

    [HttpGet("performance/history")]
    [EndpointSummary("Persisted historical equity observations")]
    [EndpointDescription("Inclusive UTC bounds; start <= end. Latest limit observations returned chronologically. Money is decimal strings; return is a numeric fraction. No current quotes, interpolation or capture. Available empty history: 200 []; missing portfolio: 404; storage unavailable: 503.")]
    [ProducesResponseType(typeof(AiTraderEquityResponse[]), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 409)]
    public async Task<ActionResult<AiTraderEquityResponse[]>> History([FromQuery] AiTraderHistoryQuery query,
        CancellationToken cancellationToken) => Ok((await history.GetHistoryAsync(query.StartUtc(), query.EndUtc(),
            query.Limit, cancellationToken)).Select(AiTraderEquityResponse.From).ToArray());

    [HttpGet("models")]
    [EndpointSummary("Validated registered ML model versions")]
    [EndpointDescription("Reads a private server-configured #75 registry export. Active/Archived status is per family; no global model is inferred. Classification metrics are numeric fractions; timestamps UTC; periods daily dates. Available empty registry: 200 []; missing/unconfigured: 503; invalid: 502. No artifact or secret fields.")]
    [ProducesResponseType(typeof(AiTraderModelResponse[]), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 502)]
    public async Task<ActionResult<AiTraderModelResponse[]>> Models(CancellationToken cancellationToken) =>
        Ok((await models.GetAsync(cancellationToken)).Select(AiTraderModelResponse.From).ToArray());

    [HttpGet("backtests")]
    [EndpointSummary("Validated persisted historical ML simulations")]
    [EndpointDescription("Reads configured #73/#74/#78 exports; never runs Python. Money is decimal strings; returns, win rate and drawdown are numeric fractions. Historical version is null unless proven by evidence. Unavailable metrics remain null; no Sharpe ratio or profit factor is fabricated. Empty export: 200 []; missing/unconfigured: 503; invalid: 502.")]
    [ProducesResponseType(typeof(AiTraderBacktestResponse[]), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 502)]
    public async Task<ActionResult<AiTraderBacktestResponse[]>> Backtests(CancellationToken cancellationToken) =>
        Ok((await backtests.GetAsync(cancellationToken)).Select(AiTraderBacktestResponse.From).ToArray());

    private static AiDecisionOutcome Outcome(IReadOnlyDictionary<Guid, AiDecisionOutcome> results, Guid id) =>
        results.TryGetValue(id, out var value) ? value
            : throw new AiDecisionHistoryException(AiDecisionHistoryFailure.PersistenceFailure);

    private void RequireRealMarketData()
    {
        if (!environment.IsDevelopment() && marketData.Value.Provider == "Mock")
            throw new MarketDataProviderException(MarketDataProviderFailure.ProviderUnavailable);
    }
}
