using System.Text.Json;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using static StockLab.Infrastructure.AiTraderMetadata.MetadataJson;

namespace StockLab.Infrastructure.AiTraderMetadata;

public sealed class JsonAiModelCatalogReadService(IOptions<AiTraderMetadataOptions> options) : IAiModelCatalogReadService
{
    public async Task<IReadOnlyList<AiModelCatalogItem>> GetAsync(CancellationToken cancellationToken = default)
    {
        using var document = await ReadAsync(options.Value.RegistryPath, "model", cancellationToken);
        try
        {
            var root = document.RootElement;
            Keys(root, "schema_version models");
            Require(Count(root, "schema_version") == 1 && root.GetProperty("models").ValueKind == JsonValueKind.Array);
            var models = new List<AiModelCatalogItem>();
            foreach (var record in root.GetProperty("models").EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                models.Add(ParseRecord(record));
            }
            Require(models.Select(item => (item.ModelName, item.ModelVersion)).Distinct().Count() == models.Count
                && models.Select(item => item.TrainingRunId).Distinct().Count() == models.Count);
            foreach (var family in models.GroupBy(item => item.ModelName))
            {
                Require(family.Select(item => item.ModelType).Distinct().Count() == 1 && family.Count(item => item.Status == "Active") <= 1);
                var versions = family.Select(item => int.Parse(item.ModelVersion.AsSpan(1))).Order().ToArray();
                Require(versions.SequenceEqual(Enumerable.Range(1, versions.Length)));
            }
            return models;
        }
        catch (Exception error) when (error is FormatException or KeyNotFoundException or InvalidOperationException or OverflowException)
        { throw new CatalogUnavailableException("model", true); }
    }

    internal static AiModelCatalogItem ParseRecord(JsonElement record)
    {
        Keys(record, "model_name model_version version_number training_run_id model_type trained_at_utc registered_at_utc status training_report metrics holdout_sha256");
        var name = Name(record);
        var version = Version(record, "model_version");
        Require("v" + Count(record, "version_number", true).ToString("D6", System.Globalization.CultureInfo.InvariantCulture) == version);
        var run = Uuid(record, "training_run_id");
        var trained = Timestamp(record, "trained_at_utc");
        Require(Timestamp(record, "registered_at_utc") >= trained);
        var status = Text(record, "status");
        Require(status is "Active" or "Archived");
        Hash(record, "holdout_sha256");
        var type = Text(record, "model_type");
        Require(type is "logistic_regression" or "random_forest" && name == type + "_baseline");
        var metrics = record.GetProperty("metrics");
        ValidateMetrics(metrics, name);
        var report = record.GetProperty("training_report");
        var shared = "model_name feature_columns target_name target_definition prediction_timing split_rule dataset split scaler hyperparameters test_accuracy ";
        Keys(report, shared + (type == "logistic_regression" ? "converged iterations" : "feature_importances random_state baseline_model_name baseline_accuracy accuracy_delta"));
        Require(Name(report) == name
            && report.GetProperty("feature_columns").ValueKind == JsonValueKind.Array
            && report.GetProperty("feature_columns").EnumerateArray().Select(Text).SequenceEqual(Features)
            && Text(report, "target_name") == "target_up_1d"
            && Text(report, "target_definition") == "1 if the next observed clean session close is higher; 0 if equal or lower, per symbol."
            && Text(report, "prediction_timing") == "Available only after the feature session close; predicts the next observed session close direction."
            && Text(report, "split_rule") == "Latest ceil(test_fraction * unique feature dates) dates are test; train feature and target dates must precede split_date."
            && report.GetProperty("hyperparameters").ValueKind == JsonValueKind.Object && report.GetProperty("hyperparameters").EnumerateObject().Any());
        Require(Rate(report, "test_accuracy") == Rate(metrics, "accuracy"));
        var split = report.GetProperty("split");
        Keys(split, "split_date test_fraction train_rows test_rows purged_boundary_rows train_start train_end test_start test_end train_rows_per_symbol test_rows_per_symbol train_positive_rate test_positive_rate");
        var start = Day(split, "train_start");
        var end = Day(split, "train_end");
        var testStart = Day(split, "test_start");
        var testEnd = Day(split, "test_end");
        Require(start <= end && end < testStart && Day(split, "split_date") == testStart && testStart <= testEnd
            && Rate(split, "test_fraction") is > 0 and < 1 && Rate(split, "train_positive_rate") is > 0 and < 1);
        var trainRows = Count(split, "train_rows", true);
        var testRows = Count(split, "test_rows", true);
        Require(testRows == Count(metrics, "total_rows", true));
        Close(Rate(split, "test_positive_rate"), (decimal)Count(metrics, "support_positive") / testRows);
        Require(SymbolCounts(split.GetProperty("train_rows_per_symbol")) == trainRows
            && SymbolCounts(split.GetProperty("test_rows_per_symbol")) == testRows);
        var dataset = report.GetProperty("dataset");
        Keys(dataset, "input_rows supervised_rows unlabeled_rows_removed_per_symbol");
        var rows = Count(dataset, "supervised_rows", true);
        Require((long)Count(dataset, "input_rows", true) == rows + SymbolCounts(dataset.GetProperty("unlabeled_rows_removed_per_symbol"))
            && rows == (long)trainRows + testRows + Count(split, "purged_boundary_rows"));
        if (type == "logistic_regression")
        {
            Require(Text(report, "scaler") == "StandardScaler" && Boolean(report, "converged")
                && report.GetProperty("iterations").ValueKind == JsonValueKind.Array && report.GetProperty("iterations").GetArrayLength() > 0);
            foreach (var iteration in report.GetProperty("iterations").EnumerateArray())
                Require(iteration.ValueKind == JsonValueKind.Number && iteration.TryGetInt32(out var count) && count > 0);
        }
        else
        {
            Require(report.GetProperty("scaler").ValueKind == JsonValueKind.Null);
            var importances = report.GetProperty("feature_importances");
            Keys(importances, string.Join(' ', Features));
            Require(importances.EnumerateObject().Select(property => property.Name).SequenceEqual(Features));
            Close(Features.Sum(feature => Rate(importances, feature)), 1);
            Require(Text(report, "baseline_model_name") == "logistic_regression_baseline"
                && report.GetProperty("random_state").ValueKind == JsonValueKind.Number
                && report.GetProperty("random_state").TryGetInt32(out var randomState)
                && Count(report.GetProperty("hyperparameters"), "random_state") == randomState);
            Close(Number(report, "accuracy_delta"), Rate(report, "test_accuracy") - Rate(report, "baseline_accuracy"));
        }
        return new(name, version, type, run, trained, status, Real(metrics, "accuracy"), Real(metrics, "precision"),
            Real(metrics, "recall"), Real(metrics, "f1"), new(start, end), new(testStart, testEnd));
    }

    internal static void ValidateMetrics(JsonElement metrics, string name)
    {
        Keys(metrics, "model_name accuracy precision recall f1 true_positive true_negative false_positive false_negative support_positive support_negative total_rows");
        Require(Name(metrics) == name);
        var tp = (long)Count(metrics, "true_positive");
        var tn = (long)Count(metrics, "true_negative");
        var fp = (long)Count(metrics, "false_positive");
        var fn = (long)Count(metrics, "false_negative");
        var total = Count(metrics, "total_rows", true);
        Require(tp + tn + fp + fn == total && tp + fn == Count(metrics, "support_positive") && tn + fp == Count(metrics, "support_negative"));
        Close(Rate(metrics, "accuracy"), (decimal)(tp + tn) / total);
        Close(Rate(metrics, "precision"), tp + fp == 0 ? 0 : (decimal)tp / (tp + fp));
        Close(Rate(metrics, "recall"), tp + fn == 0 ? 0 : (decimal)tp / (tp + fn));
        Close(Rate(metrics, "f1"), 2 * tp + fp + fn == 0 ? 0 : (decimal)(2 * tp) / (2 * tp + fp + fn));
    }

    private static long SymbolCounts(JsonElement counts)
    {
        Require(counts.ValueKind == JsonValueKind.Object && counts.EnumerateObject().Any());
        long total = 0;
        foreach (var item in counts.EnumerateObject())
        {
            Require(System.Text.RegularExpressions.Regex.IsMatch(item.Name, @"^[A-Z0-9][A-Z0-9.\^/-]{0,31}(?::[A-Z0-9][A-Z0-9._-]{0,15})?\z"));
            total += Count(counts, item.Name);
        }
        return total;
    }

    private static readonly string[] Features = ["return_1d", "ma_5", "ma_20", "rsi_14", "volume", "momentum_10", "volatility_20"];
}
