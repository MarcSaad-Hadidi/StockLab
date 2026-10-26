using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Trading;

public sealed class AiDecisionPipelineService(IAiDecisionInferenceProvider inference,
    IAiDecisionHistoryService history, TimeProvider clock) : IAiDecisionPipelineService
{
    public async Task<IReadOnlyList<AiDecisionRecord>> RunAsync(DateOnly decisionDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (decisionDate == default || decisionDate >= today)
            throw new ArgumentException("A closed session before today's UTC date is required.", nameof(decisionDate));
        var batch = await inference.InferAsync(decisionDate, cancellationToken);
        if (batch is null || batch.DecisionDate != decisionDate || batch.Decisions is null
            || batch.Decisions.Count is < 1 or > 100 || batch.Decisions.Any(d => d is null)
            || batch.Decisions.Select(d => d.Symbol).Distinct(StringComparer.Ordinal).Count() != batch.Decisions.Count)
            throw new ArgumentException("Invalid inference batch.");

        // Validate every row first: an invalid suffix must never leave a persisted prefix.
        var requests = batch.Decisions.Select(row =>
        {
            var request = AiDecisionHistoryService.Validate(new(Guid.NewGuid(), row.Symbol, row.Signal,
                row.Confidence, decisionDate, batch.ModelName, batch.ModelVersion), today);
            var identity = JsonSerializer.Serialize(new[] { "stocklab-ai-decision-v1", request.ModelName,
                request.ModelVersion, request.Symbol, decisionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
            return request with { DecisionId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16)) };
        }).ToArray();
        var recorded = new List<AiDecisionRecord>(requests.Length);
        foreach (var request in requests)
            recorded.Add(await history.RecordAsync(request, cancellationToken));
        return recorded;
    }
}
