using System.Text.Json;
using System.Text.RegularExpressions;
using StockLab.Application.DTOs.AiTrader;
using static StockLab.Infrastructure.AiTraderMetadata.MetadataJson;

namespace StockLab.Infrastructure.AiTraderMetadata;

internal static class PromotionMetadata
{
    internal static IReadOnlyList<AiBacktestCatalogItem> Parse(JsonElement envelope)
    {
        Keys(envelope, "schema_version sha256 payload");
        Require(Count(envelope, "schema_version") == 1 && Hash(envelope, "sha256") == Digest(envelope.GetProperty("payload")));
        var payload = envelope.GetProperty("payload");
        Keys(payload, "evaluation_id model_name active_model_version candidate_model_version decision reason_codes policy_snapshot request_sha256 evaluated_at_utc evidence");
        Uuid(payload, "evaluation_id");
        var name = Name(payload);
        var candidateVersion = Version(payload, "candidate_model_version");
        var activeVersion = payload.GetProperty("active_model_version").ValueKind == JsonValueKind.Null ? null : Version(payload, "active_model_version");
        Hash(payload, "request_sha256");
        var timestamp = Timestamp(payload, "evaluated_at_utc");
        var decision = Text(payload, "decision");
        Require(decision is "APPROVED" or "REJECTED" or "BLOCKED" && payload.GetProperty("reason_codes").ValueKind == JsonValueKind.Array);
        var reasons = payload.GetProperty("reason_codes").EnumerateArray().Select(Text).ToArray();
        Require((decision == "APPROVED" ? reasons.Length == 0 : reasons.Length > 0)
            && reasons.All(reason => Regex.IsMatch(reason, @"^[A-Z][A-Z0-9_]*\z", RegexOptions.CultureInvariant)));
        ValidatePolicy(payload.GetProperty("policy_snapshot"));
        var evidence = payload.GetProperty("evidence");
        // Blocked evaluations can lack model evidence entirely; they do not establish a backtest result.
        if (decision == "BLOCKED" && evidence.ValueKind == JsonValueKind.Object && !evidence.EnumerateObject().Any()) return [];
        Require(activeVersion != candidateVersion);
        Keys(evidence, "dataset active candidate benchmark metric_deltas");
        var data = evidence.GetProperty("dataset");
        Keys(data, "source_sha256 market_data_sha256 feature_rows_sha256 labels_sha256 evaluation_rows symbols feature_start feature_end target_end training_boundary requested_start requested_end forward_rows evidence_kind availability_rule backtest_config registry_path");
        foreach (var key in new[] { "source_sha256", "market_data_sha256", "feature_rows_sha256", "labels_sha256" }) Hash(data, key);
        var rows = Count(data, "evaluation_rows", true);
        var forwardRows = Count(data, "forward_rows");
        Require(forwardRows <= rows && Text(data, "evidence_kind") == (forwardRows == 0 ? "historical" : forwardRows == rows ? "prospective" : "mixed")
            && Text(data, "availability_rule") == "feature session date strictly after both trained_at_utc dates"
            && !string.IsNullOrWhiteSpace(Text(data, "registry_path")));
        Symbols(data);
        var start = Day(data, "feature_start");
        var end = Day(data, "target_end");
        Require(Day(data, "requested_start") <= start && start <= Day(data, "feature_end") && Day(data, "feature_end") < end
            && end <= Day(data, "requested_end") && end < DateOnly.FromDateTime(timestamp.UtcDateTime) && start >= Day(data, "training_boundary"));
        JsonAiBacktestCatalogReadService.ValidateConfig(data.GetProperty("backtest_config"));
        var results = new List<AiBacktestCatalogItem>();
        var boundaries = new List<DateOnly>();
        var supports = new List<(int Positive, int Negative)>();
        foreach (var role in new[] { "candidate", "active" })
        {
            var model = evidence.GetProperty(role);
            if (role == "active" && activeVersion is null) { Require(model.ValueKind == JsonValueKind.Null); continue; }
            Keys(model, "record artifact_sha256 metadata_sha256 runtime classification backtest benchmark benchmark_comparison evaluation_keys_sha256 predictions_sha256 decisions_sha256 equity_sha256 equity_dates benchmark_equity_sha256");
            foreach (var key in new[] { "artifact_sha256", "metadata_sha256", "evaluation_keys_sha256", "predictions_sha256", "decisions_sha256", "equity_sha256", "benchmark_equity_sha256" }) Hash(model, key);
            var record = model.GetProperty("record");
            var registered = JsonAiModelCatalogReadService.ParseRecord(record);
            Require(registered.ModelName == name && registered.ModelVersion == (role == "candidate" ? candidateVersion : activeVersion)
                && registered.TrainedAtUtc <= timestamp && registered.Status == (role == "candidate" ? "Archived" : "Active")
                && Hash(model, "metadata_sha256") == Digest(record, "status"));
            boundaries.Add(Day(record.GetProperty("training_report").GetProperty("split"), "split_date"));
            var classification = model.GetProperty("classification");
            JsonAiModelCatalogReadService.ValidateMetrics(classification, name);
            supports.Add((Count(classification, "support_positive"), Count(classification, "support_negative")));
            Require(Count(classification, "total_rows", true) == rows && Hash(model, "evaluation_keys_sha256") == Hash(data, "labels_sha256"));
            var runtime = model.GetProperty("runtime");
            Keys(runtime, "python scikit_learn numpy scipy joblib");
            foreach (var property in runtime.EnumerateObject()) Require(!string.IsNullOrWhiteSpace(Text(property.Value)));
            var report = model.GetProperty("backtest");
            var item = JsonAiBacktestCatalogReadService.ParseReport(report);
            var comparison = model.GetProperty("benchmark_comparison");
            var compared = JsonAiBacktestCatalogReadService.ParseComparison(model.GetProperty("benchmark"), comparison);
            Require(item.ModelName == name && item.StartDate == start && item.EndDate == end
                && compared.ModelName == name && compared.StartDate == start && compared.EndDate == end
                && Digest(report.GetProperty("symbols")) == Digest(data.GetProperty("symbols"))
                && Digest(report.GetProperty("config")) == Digest(data.GetProperty("backtest_config"))
                && Digest(model.GetProperty("benchmark")) == Digest(evidence.GetProperty("benchmark"))
                && Digest(model.GetProperty("equity_dates")) == Digest(comparison.GetProperty("equity_dates")));
            JsonAiBacktestCatalogReadService.ValidateEquityDates(model.GetProperty("equity_dates"), start, end);
            Close(item.InitialCapital, Number(data.GetProperty("backtest_config"), "initial_capital"));
            Close(item.FinalValue, compared.FinalValue);
            Close(item.TotalReturn, compared.TotalReturn);
            Close(item.MaxDrawdown, compared.MaxDrawdown);
            results.Add(item with
            {
                ModelVersion = registered.ModelVersion, BenchmarkReturn = compared.BenchmarkReturn,
                BenchmarkMaxDrawdown = compared.BenchmarkMaxDrawdown, ReturnDelta = compared.ReturnDelta,
                EvaluationSource = "model_promotion", SourceTimestampUtc = timestamp
            });
        }
        Require(boundaries.Max() == Day(data, "training_boundary") && supports.Distinct().Count() == 1);
        var deltas = evidence.GetProperty("metric_deltas");
        if (activeVersion is null) Keys(deltas, "");
        else
        {
            Keys(deltas, "accuracy_delta precision_delta recall_delta f1_delta total_return_delta max_drawdown_delta final_value_delta win_rate_delta executed_trades_delta");
            var active = evidence.GetProperty("active");
            var candidate = evidence.GetProperty("candidate");
            Require(Digest(active.GetProperty("equity_dates")) == Digest(candidate.GetProperty("equity_dates"))
                && Hash(active, "benchmark_equity_sha256") == Hash(candidate, "benchmark_equity_sha256"));
            foreach (var key in new[] { "accuracy", "precision", "recall", "f1" })
                Close(Number(deltas, key + "_delta"), Number(candidate.GetProperty("classification"), key) - Number(active.GetProperty("classification"), key));
            foreach (var key in new[] { "total_return", "max_drawdown", "final_value", "win_rate", "executed_trades" })
                Close(Number(deltas, key + "_delta"), Number(candidate.GetProperty("backtest"), key) - Number(active.GetProperty("backtest"), key));
        }
        return decision == "BLOCKED" ? [] : results;
    }

    private static void ValidatePolicy(JsonElement policy)
    {
        Keys(policy, "minimum_evaluation_rows minimum_accuracy minimum_f1 minimum_return_improvement maximum_drawdown_increase require_benchmark_comparison require_benchmark_outperformance minimum_sample_symbols minimum_forward_rows");
        Count(policy, "minimum_evaluation_rows", true);
        Count(policy, "minimum_sample_symbols", true);
        Count(policy, "minimum_forward_rows");
        Rate(policy, "minimum_accuracy");
        Rate(policy, "minimum_f1");
        Rate(policy, "maximum_drawdown_increase");
        Require(Number(policy, "minimum_return_improvement") >= 0);
        var comparison = Boolean(policy, "require_benchmark_comparison");
        Require(!Boolean(policy, "require_benchmark_outperformance") || comparison);
    }
}
