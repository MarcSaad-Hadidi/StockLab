using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using StockLab.Application.Exceptions;
using StockLab.Infrastructure.AiTraderMetadata;

namespace StockLab.UnitTests.AiTraderMetadata;

public sealed class JsonCatalogTests : IDisposable
{
    // Test-only #73/#74/#75/#78 Python exports; these fixtures must never configure a deployed catalog.
    private readonly string directory = Path.Combine(Path.GetTempPath(), "stocklab-catalog-tests-" + Guid.NewGuid());

    public JsonCatalogTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task Registry_preserves_real_training_metrics_and_periods()
    {
        var result = Assert.Single(await Models(Fixture("registry")).GetAsync());
        Assert.Equal("logistic_regression_baseline", result.ModelName);
        Assert.Equal("v000001", result.ModelVersion);
        Assert.Equal("logistic_regression", result.ModelType);
        Assert.Equal(Guid.Parse("0574227c-30bd-47ed-8f11-645a4b2360f0"), result.TrainingRunId);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 14, 0, 0, TimeSpan.Zero), result.TrainedAtUtc);
        Assert.Equal("Archived", result.Status);
        Assert.Equal(.875, result.Accuracy);
        Assert.Equal(1, result.Precision);
        Assert.Equal(.75, result.Recall);
        Assert.Equal(.8571428571428571, result.F1);
        Assert.Equal(new DateOnly(2024, 1, 29), result.TrainingPeriod.StartDate);
        Assert.Equal(new DateOnly(2024, 5, 16), result.EvaluationPeriod.EndDate);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("holdout_sha256", json);
        Assert.DoesNotContain("hyperparameters", json);
        Assert.DoesNotContain(directory, json);
    }

    [Fact]
    public async Task Registry_preserves_multiple_versions_and_active_per_family()
    {
        var registry = JsonNode.Parse(Fixture("registry"))!;
        var models = registry["models"]!.AsArray();
        var second = models[0]!.DeepClone();
        second["model_version"] = "v000002";
        second["version_number"] = 2;
        second["training_run_id"] = "10000000-0000-4000-8000-000000000001";
        second["status"] = "Active";
        models.Add(second);
        var forest = models[0]!.DeepClone();
        forest["model_name"] = "random_forest_baseline";
        forest["model_type"] = "random_forest";
        forest["training_run_id"] = "10000000-0000-4000-8000-000000000002";
        forest["status"] = "Active";
        forest["metrics"]!["model_name"] = "random_forest_baseline";
        var report = forest["training_report"]!;
        report["model_name"] = "random_forest_baseline";
        report["scaler"] = null;
        report.AsObject().Remove("converged");
        report.AsObject().Remove("iterations");
        report["feature_importances"] = JsonNode.Parse("""{"return_1d":1,"ma_5":0,"ma_20":0,"rsi_14":0,"volume":0,"momentum_10":0,"volatility_20":0}""");
        report["random_state"] = 42;
        report["hyperparameters"] = JsonNode.Parse("""{"random_state":42}""");
        report["baseline_model_name"] = "logistic_regression_baseline";
        report["baseline_accuracy"] = .875;
        report["accuracy_delta"] = 0;
        models.Add(forest);
        var result = await Models(registry.ToJsonString()).GetAsync();
        Assert.Equal(3, result.Count);
        Assert.Equal(2, result.Count(item => item.Status == "Active"));
        Assert.Contains(result, item => item.ModelType == "random_forest");
    }

    [Theory]
    [InlineData("version")]
    [InlineData("metric")]
    [InlineData("timestamp")]
    [InlineData("timestamp_newline")]
    [InlineData("period")]
    [InlineData("duplicate")]
    [InlineData("schema")]
    [InlineData("active")]
    public async Task Invalid_registry_is_rejected_without_leaking_paths(string kind)
    {
        var root = JsonNode.Parse(Fixture("registry"))!;
        var model = root["models"]![0]!;
        switch (kind)
        {
            case "version": model["model_version"] = "latest"; break;
            case "metric": model["metrics"]!["accuracy"] = .5; break;
            case "timestamp": model["trained_at_utc"] = "2026-10-14T14:00:00"; break;
            case "timestamp_newline": model["trained_at_utc"] = "2026-10-14T14:00:00+00:00\n"; break;
            case "period": model["training_report"]!["split"]!["train_end"] = "2024-05-16"; break;
            case "duplicate": root["models"]!.AsArray().Add(model.DeepClone()); break;
            case "schema": root["schema_version"] = 2; break;
            case "active":
                model["status"] = "Active";
                var duplicate = model.DeepClone();
                duplicate["version_number"] = 2;
                duplicate["model_version"] = "v000002";
                duplicate["training_run_id"] = "10000000-0000-4000-8000-000000000003";
                root["models"]!.AsArray().Add(duplicate);
                break;
        }
        var error = await Assert.ThrowsAsync<CatalogUnavailableException>(() => Models(root.ToJsonString()).GetAsync());
        Assert.True(error.IsInvalid);
        Assert.DoesNotContain(directory, error.Message);
    }

    [Fact]
    public async Task Available_empty_sources_are_empty_and_missing_sources_are_unavailable()
    {
        Assert.Empty(await Models("""{"schema_version":1,"models":[]}""").GetAsync());
        Assert.Empty(await Backtests("[]").GetAsync());
        var emptyOptions = Options.Create(new AiTraderMetadataOptions());
        Assert.False((await Assert.ThrowsAsync<CatalogUnavailableException>(() => new JsonAiModelCatalogReadService(emptyOptions).GetAsync())).IsInvalid);
        Assert.False((await Assert.ThrowsAsync<CatalogUnavailableException>(() => new JsonAiBacktestCatalogReadService(emptyOptions).GetAsync())).IsInvalid);
        var missing = Options.Create(new AiTraderMetadataOptions { RegistryPath = Path.Combine(directory, "missing.json") });
        Assert.False((await Assert.ThrowsAsync<CatalogUnavailableException>(() => new JsonAiModelCatalogReadService(missing).GetAsync())).IsInvalid);
    }

    [Theory]
    [InlineData("{\"schema_version\":1,\"schema_version\":1,\"models\":[]}")]
    [InlineData("{\"schema_version\":1,\"models\":[],\"extra\":1e999}")]
    [InlineData("{\"schema_version\":1,\"models\":[NaN]}")]
    public async Task Ambiguous_or_nonfinite_json_is_invalid(string json) =>
        Assert.True((await Assert.ThrowsAsync<CatalogUnavailableException>(() => Models(json).GetAsync())).IsInvalid);

    [Fact]
    public async Task Invalid_utf8_and_oversized_sources_are_invalid()
    {
        var path = Write("bad.json", "");
        await File.WriteAllBytesAsync(path, [0xff, 0xfe, 0x7b, 0x7d]);
        var options = Options.Create(new AiTraderMetadataOptions { RegistryPath = path });
        Assert.True((await Assert.ThrowsAsync<CatalogUnavailableException>(() => new JsonAiModelCatalogReadService(options).GetAsync())).IsInvalid);
        await File.WriteAllBytesAsync(path, new byte[8 * 1024 * 1024 + 1]);
        Assert.True((await Assert.ThrowsAsync<CatalogUnavailableException>(() => new JsonAiModelCatalogReadService(options).GetAsync())).IsInvalid);
    }

    [Fact]
    public async Task Legacy_backtest_has_exact_persisted_results_without_invented_version()
    {
        var item = Assert.Single(await Backtests(Fixture("backtest")).GetAsync());
        Assert.Equal("synthetic_example", item.ModelName);
        Assert.Null(item.ModelVersion);
        Assert.Equal(new DateOnly(2024, 1, 5), item.StartDate);
        Assert.Equal(new DateOnly(2024, 1, 10), item.EndDate);
        Assert.Equal(100000m, item.InitialCapital);
        Assert.Equal(102000m, item.FinalValue);
        Assert.Equal(.020000000000000018m, item.TotalReturn);
        Assert.Equal(1m, item.WinRate);
        Assert.Equal(0m, item.MaxDrawdown);
        Assert.Equal(2, item.ExecutedTrades);
        Assert.Null(item.BenchmarkReturn);
        Assert.Null(item.SourceTimestampUtc);
        Assert.Equal("backtesting", item.EvaluationSource);
    }

    [Fact]
    public async Task Benchmark_comparison_preserves_real_benchmark_metrics_and_missing_trade_statistics()
    {
        var options = Options.Create(new AiTraderMetadataOptions { BenchmarkComparisonPaths = [Write("comparison.json", Fixture("benchmark"))] });
        var item = Assert.Single(await new JsonAiBacktestCatalogReadService(options).GetAsync());
        Assert.Equal("synthetic_example", item.ModelName);
        Assert.Null(item.ModelVersion);
        Assert.Equal(101000m, item.FinalValue);
        Assert.Equal(.050000000000000044m, item.BenchmarkReturn);
        Assert.Equal(.13636363636363635m, item.BenchmarkMaxDrawdown);
        Assert.Equal(-.040000000000000036m, item.ReturnDelta);
        Assert.Null(item.WinRate);
        Assert.Null(item.ExecutedTrades);
        Assert.Equal("benchmark_comparison", item.EvaluationSource);
    }

    [Theory]
    [InlineData("capital")]
    [InlineData("return")]
    [InlineData("date")]
    [InlineData("counts")]
    [InlineData("win")]
    [InlineData("schema")]
    [InlineData("identity")]
    [InlineData("positions")]
    [InlineData("no_trades")]
    [InlineData("sell_without_buy")]
    public async Task Inconsistent_backtests_are_rejected(string kind)
    {
        var report = JsonNode.Parse(Fixture("backtest"))!;
        switch (kind)
        {
            case "capital": report["initial_capital"] = 0; break;
            case "return": report["total_return"] = .2; break;
            case "date": report["end_date"] = "2023-01-01"; break;
            case "counts": report["executed_trades"] = 99; break;
            case "win": report["win_rate"] = .5; break;
            case "schema": report["model_version"] = "latest"; break;
            case "identity": report["model_name"] = "synthetic_example\n"; break;
            case "positions": report["final_positions_value"] = 1000; report["final_cash"] = 101000; break;
            case "no_trades":
                foreach (var field in new[] { "buy_decisions", "sell_decisions", "executed_trades", "executed_buys", "executed_sells", "winning_closed_trades", "win_rate" }) report[field] = 0;
                report["hold_decisions"] = 3;
                break;
            case "sell_without_buy":
                report["buy_decisions"] = 0;
                report["sell_decisions"] = 2;
                report["executed_buys"] = 0;
                report["executed_sells"] = 2;
                report["winning_closed_trades"] = 2;
                break;
        }
        Assert.True((await Assert.ThrowsAsync<CatalogUnavailableException>(() => Backtests(report.ToJsonString()).GetAsync())).IsInvalid);
    }

    [Fact]
    public async Task Inconsistent_benchmark_delta_is_rejected()
    {
        var report = JsonNode.Parse(Fixture("benchmark"))!;
        report["comparison"]!["return_delta"] = 1;
        var options = Options.Create(new AiTraderMetadataOptions { BenchmarkComparisonPaths = [Write("benchmark.json", report.ToJsonString())] });
        Assert.True((await Assert.ThrowsAsync<CatalogUnavailableException>(() => new JsonAiBacktestCatalogReadService(options).GetAsync())).IsInvalid);
    }

    [Fact]
    public async Task Python_promotion_evidence_proves_exact_version_without_artifact_access()
    {
        var options = Options.Create(new AiTraderMetadataOptions { PromotionEvaluationPaths = [Write("evaluation.json", Fixture("promotion"))] });
        var item = Assert.Single(await new JsonAiBacktestCatalogReadService(options).GetAsync());
        Assert.Equal("logistic_regression_baseline", item.ModelName);
        Assert.Equal("v000002", item.ModelVersion);
        Assert.Equal("model_promotion", item.EvaluationSource);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), item.SourceTimestampUtc);
        Assert.Equal(new DateOnly(2024, 7, 1), item.StartDate);
        Assert.Equal(new DateOnly(2024, 10, 31), item.EndDate);
        Assert.NotNull(item.ExecutedTrades);
        Assert.NotNull(item.BenchmarkReturn);
        Assert.DoesNotContain("registry_path", JsonSerializer.Serialize(item));
    }

    [Fact]
    public async Task Promotion_preserves_both_exact_versions_from_comparable_evidence()
    {
        var options = Options.Create(new AiTraderMetadataOptions { PromotionEvaluationPaths = [Write("evaluation.json", Fixture("comparative-promotion"))] });
        var items = await new JsonAiBacktestCatalogReadService(options).GetAsync();
        Assert.Equal(2, items.Count);
        Assert.Contains(items, item => item.ModelVersion == "v000001");
        Assert.Contains(items, item => item.ModelVersion == "v000002");
        Assert.All(items, item => Assert.Equal("model_promotion", item.EvaluationSource));
    }

    [Fact]
    public async Task Already_active_blocked_promotion_is_valid_without_backtest_evidence()
    {
        var options = Options.Create(new AiTraderMetadataOptions { PromotionEvaluationPaths = [Write("evaluation.json", Fixture("blocked-promotion"))] });
        Assert.Empty(await new JsonAiBacktestCatalogReadService(options).GetAsync());
    }

    [Fact]
    public async Task Checksummed_malformed_blocked_evidence_is_invalid()
    {
        var report = JsonNode.Parse(Fixture("blocked-promotion"))!;
        report["payload"]!["evidence"] = JsonNode.Parse("""{"candidate":"corrupt"}""");
        using var payload = JsonDocument.Parse(report["payload"]!.ToJsonString());
        report["sha256"] = MetadataJson.Digest(payload.RootElement);
        var options = Options.Create(new AiTraderMetadataOptions { PromotionEvaluationPaths = [Write("evaluation.json", report.ToJsonString())] });
        Assert.True((await Assert.ThrowsAsync<CatalogUnavailableException>(() => new JsonAiBacktestCatalogReadService(options).GetAsync())).IsInvalid);
    }

    [Fact]
    public async Task Tampered_promotion_evidence_is_invalid()
    {
        var report = JsonNode.Parse(Fixture("promotion"))!;
        report["payload"]!["candidate_model_version"] = "v000003";
        var options = Options.Create(new AiTraderMetadataOptions { PromotionEvaluationPaths = [Write("evaluation.json", report.ToJsonString())] });
        Assert.True((await Assert.ThrowsAsync<CatalogUnavailableException>(() => new JsonAiBacktestCatalogReadService(options).GetAsync())).IsInvalid);
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Models(Fixture("registry")).GetAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Backtests(Fixture("backtest")).GetAsync(cancellation.Token));
    }

    private JsonAiModelCatalogReadService Models(string json) => new(Options.Create(new AiTraderMetadataOptions { RegistryPath = Write("registry.json", json) }));
    private JsonAiBacktestCatalogReadService Backtests(string json) => new(Options.Create(new AiTraderMetadataOptions { BacktestReportPaths = [Write("report.json", json)] }));
    private string Write(string name, string json)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }

    private static string Fixture(string name, [CallerFilePath] string source = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "Fixtures", "test-only-" + name + ".json"));

    public void Dispose() => Directory.Delete(directory, true);
}
