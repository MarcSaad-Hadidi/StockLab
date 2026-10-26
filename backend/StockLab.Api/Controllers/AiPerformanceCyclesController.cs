using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockLab.Api.DTOs;
using StockLab.Api.DTOs.AiTrader;
using StockLab.Application.Interfaces;

namespace StockLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ai-trader/performance/cycles")]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiValidationErrorResponse), 400)]
[ProducesResponseType(typeof(ApiErrorResponse), 401)]
[ProducesResponseType(typeof(ApiErrorResponse), 500)]
[ProducesResponseType(typeof(ApiErrorResponse), 503)]
public sealed class AiPerformanceCyclesController(IAiPerformanceCycleService cycles) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("Committed periodic AI portfolio observations")]
    [EndpointDescription("Newest UTC slot first; latest 1..200 cycles. Read only: no quotes, capture or trading. Money uses decimal strings. Empty history returns 200 [].")]
    [ProducesResponseType(typeof(AiPerformanceCycleResponse[]), 200)]
    public async Task<ActionResult<AiPerformanceCycleResponse[]>> Recent([FromQuery, Range(1, 200)] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Ok((await cycles.GetRecentAsync(limit, cancellationToken)).Select(AiPerformanceCycleResponse.From).ToArray());

    [HttpGet("{captureId}")]
    [EndpointSummary("Recorded valuation and original trade/model context")]
    [EndpointDescription("Exact capture ID with an offset page of 1..200 immutable trade observations. Includes nullable legacy decision/model/realized P&L. Total portfolio return is aggregate, not a model-specific causal return. No current prices or training runs.")]
    [ProducesResponseType(typeof(AiPerformanceCycleDetailResponse), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    public async Task<ActionResult<AiPerformanceCycleDetailResponse>> Detail(Guid captureId,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0, [FromQuery, Range(1, 200)] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (captureId == Guid.Empty) return BadRequest(new ApiValidationErrorResponse("invalid_request",
            "A nonempty capture ID is required.", new Dictionary<string, string[]> {
                [nameof(captureId)] = ["The value is missing or invalid."] }));
        var value = await cycles.GetByIdAsync(captureId, offset, limit, cancellationToken);
        return value is null ? NotFound(new ApiErrorResponse("ai_performance_cycle_not_found", "The performance cycle was not found."))
            : Ok(AiPerformanceCycleDetailResponse.From(value));
    }
}
