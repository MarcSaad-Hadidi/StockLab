using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static StockLab.UnitTests.Api.AiTraderApiTests;

namespace StockLab.UnitTests.Api;

/// <summary>Only test metadata files are used; these are never application/default financial or ML data.</summary>
public sealed class AiTraderApiCatalogTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "stocklab-ai-http-test-only-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> files = [];

    public AiTraderApiCatalogTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData("models", "model")]
    [InlineData("backtests", "backtest")]
    public async Task Unconfigured_and_missing_sources_are_503_and_invalid_sources_are_502_with_safe_errors(
        string route, string catalog)
    {
        foreach (var scenario in new[] { "unconfigured", "missing", "malformed", "invalid" })
        {
            var path = Path.Combine(directory, "DO_NOT_LEAK_TEST_SOURCE.json");
            if (scenario == "malformed") path = Write("malformed.json", "{ malformed TEST_ONLY_PRIVATE_VALUE");
            if (scenario == "invalid") path = Write("invalid.json", "{\"aws_secret_access_key\":\"TEST_ONLY_PRIVATE_VALUE\"}");
            var config = scenario == "unconfigured" ? null : new Dictionary<string, string?>
            {
                [catalog == "model" ? "AiTraderMetadata:RegistryPath" : "AiTraderMetadata:BacktestReportPaths:0"] = path
            };
            await using var f = await AiTraderApiFixture.CreateAsync(config);
            var before = await f.Database.StateAsync();
            using var response = await f.GetAsync($"/api/ai-trader/{route}", AiTraderApiFixture.Token());
            var invalid = scenario is "malformed" or "invalid";
            Assert.Equal(invalid ? HttpStatusCode.BadGateway : HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal($"{catalog}_catalog_{(invalid ? "invalid" : "unavailable")}", await ErrorAsync(response));
            var json = await response.Content.ReadAsStringAsync();
            foreach (var privateValue in new[] { directory, "DO_NOT_LEAK", "TEST_ONLY_PRIVATE_VALUE", "aws_secret", "StackTrace", "joblib" })
                Assert.DoesNotContain(privateValue, json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, await f.Database.StateAsync());
            Assert.Empty(f.Observer.Commands);
            Assert.Equal(0, f.Observer.SaveCalls);
            Assert.Empty(f.Market.Calls);
        }
    }

    [Fact]
    public async Task Configured_available_empty_catalogs_return_200_arrays()
    {
        await using var f = await AiTraderApiFixture.CreateAsync(new()
        {
            ["AiTraderMetadata:RegistryPath"] = Write("empty-registry.json", "{\"schema_version\":1,\"models\":[]}"),
            ["AiTraderMetadata:BacktestReportPaths:0"] = Write("empty-report.json", "[]")
        });
        foreach (var route in new[] { "models", "backtests" })
            Assert.Empty((await f.ReadAsync($"/api/ai-trader/{route}", AiTraderApiFixture.Token())).EnumerateArray());
        Assert.Empty(f.Observer.Commands);
        Assert.Equal(0, f.Observer.SaveCalls);
    }

    [Fact]
    public async Task Model_HTTP_catalog_preserves_versions_metrics_periods_and_status_for_each_family()
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
        var training = forest["training_report"]!;
        training["model_name"] = "random_forest_baseline";
        training["scaler"] = null;
        training.AsObject().Remove("converged");
        training.AsObject().Remove("iterations");
        training["feature_importances"] = JsonNode.Parse("{\"return_1d\":1,\"ma_5\":0,\"ma_20\":0,\"rsi_14\":0,\"volume\":0,\"momentum_10\":0,\"volatility_20\":0}");
        training["random_state"] = 42;
        training["hyperparameters"] = JsonNode.Parse("{\"random_state\":42}");
        training["baseline_model_name"] = "logistic_regression_baseline";
        training["baseline_accuracy"] = .875;
        training["accuracy_delta"] = 0;
        models.Add(forest);
        await using var f = await AiTraderApiFixture.CreateAsync(new()
        {
            ["AiTraderMetadata:RegistryPath"] = Write("test-only-multi-model-registry.json", registry.ToJsonString())
        });
        var result = (await f.ReadAsync("/api/ai-trader/models", AiTraderApiFixture.Token())).EnumerateArray().ToArray();
        Assert.Equal(3, result.Length);
        Assert.Equal(2, result.Count(model => model.GetProperty("status").GetString() == "Active"));
        var original = Assert.Single(result, model => model.GetProperty("modelName").GetString() == "logistic_regression_baseline"
            && model.GetProperty("modelVersion").GetString() == "v000001");
        Assert.Equal("Archived", original.GetProperty("status").GetString());
        Assert.Equal("logistic_regression", original.GetProperty("modelType").GetString());
        Assert.Equal(Guid.Parse("0574227c-30bd-47ed-8f11-645a4b2360f0"), original.GetProperty("trainingRunId").GetGuid());
        AssertFraction(original, "accuracy", .875m);
        AssertFraction(original, "precision", 1m);
        AssertFraction(original, "recall", .75m);
        AssertFraction(original, "f1", .8571428571428571m);
        AssertUtc(original, "trainedAtUtc");
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 14, 0, 0, TimeSpan.Zero), original.GetProperty("trainedAtUtc").GetDateTimeOffset());
        Assert.Equal("2024-01-29", original.GetProperty("trainingPeriod").GetProperty("startDate").GetString());
        Assert.Equal("2024-05-16", original.GetProperty("evaluationPeriod").GetProperty("endDate").GetString());
        Assert.Contains(result, model => model.GetProperty("modelType").GetString() == "random_forest"
            && model.GetProperty("modelVersion").GetString() == "v000001"
            && model.GetProperty("status").GetString() == "Active");
        var json = JsonSerializer.Serialize(result);
        foreach (var field in new[] { "training_report", "holdout_sha256", "artifact", "joblib", "registryPath", directory })
            Assert.DoesNotContain(field, json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(f.Observer.Commands);
        Assert.Equal(0, f.Observer.SaveCalls);
        Assert.Empty(f.Market.Calls);
    }

    [Fact]
    public async Task Backtest_HTTP_catalog_maps_real_report_and_benchmark_shapes_with_unknown_version_null()
    {
        await using var f = await AiTraderApiFixture.CreateAsync(new()
        {
            ["AiTraderMetadata:BacktestReportPaths:0"] = Write("test-only-backtest.json", Fixture("backtest")),
            ["AiTraderMetadata:BenchmarkComparisonPaths:0"] = Write("test-only-benchmark.json", Fixture("benchmark"))
        });
        var before = await f.Database.StateAsync();
        var items = (await f.ReadAsync("/api/ai-trader/backtests", AiTraderApiFixture.Token())).EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.All(items, item =>
        {
            Assert.Equal("synthetic_example", item.GetProperty("modelName").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("modelVersion").ValueKind);
            Assert.Equal("2024-01-05", item.GetProperty("startDate").GetString());
            Assert.Equal("2024-01-10", item.GetProperty("endDate").GetString());
            AssertMoney(item, "initialCapital", 100000m);
            Assert.False(item.TryGetProperty("sharpeRatio", out _));
            Assert.False(item.TryGetProperty("profitFactor", out _));
        });
        var report = Assert.Single(items, item => item.GetProperty("evaluationSource").GetString() == "backtesting");
        AssertMoney(report, "finalValue", 102000m);
        AssertFraction(report, "totalReturn", .020000000000000018m);
        AssertFraction(report, "winRate", 1m);
        AssertFraction(report, "maxDrawdown", 0m);
        Assert.Equal(2, report.GetProperty("executedTrades").GetInt32());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("benchmarkReturn").ValueKind);
        var comparison = Assert.Single(items, item => item.GetProperty("evaluationSource").GetString() == "benchmark_comparison");
        AssertMoney(comparison, "finalValue", 101000m);
        AssertFraction(comparison, "totalReturn", .010000000000000009m);
        AssertFraction(comparison, "maxDrawdown", .029411764705882353m);
        AssertFraction(comparison, "benchmarkReturn", .050000000000000044m);
        AssertFraction(comparison, "benchmarkMaxDrawdown", .13636363636363635m);
        AssertFraction(comparison, "returnDelta", -.040000000000000036m);
        Assert.Equal(JsonValueKind.Null, comparison.GetProperty("winRate").ValueKind);
        Assert.Equal(JsonValueKind.Null, comparison.GetProperty("executedTrades").ValueKind);
        Assert.Equal(before, await f.Database.StateAsync());
        Assert.Empty(f.Observer.Commands);
        Assert.Equal(0, f.Observer.SaveCalls);
        Assert.Empty(f.Market.Calls);
    }

    [Fact]
    public async Task Validated_promotion_evidence_preserves_proven_historical_model_version()
    {
        await using var f = await AiTraderApiFixture.CreateAsync(new()
        {
            ["AiTraderMetadata:PromotionEvaluationPaths:0"] = Write("test-only-promotion.json", Fixture("promotion"))
        });
        var item = Assert.Single((await f.ReadAsync("/api/ai-trader/backtests", AiTraderApiFixture.Token())).EnumerateArray());
        Assert.Equal("logistic_regression_baseline", item.GetProperty("modelName").GetString());
        Assert.Equal("v000002", item.GetProperty("modelVersion").GetString());
        Assert.Equal("model_promotion", item.GetProperty("evaluationSource").GetString());
        AssertUtc(item, "sourceTimestampUtc");
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), item.GetProperty("sourceTimestampUtc").GetDateTimeOffset());
        Assert.Equal("2024-07-01", item.GetProperty("startDate").GetString());
        Assert.Equal("2024-10-31", item.GetProperty("endDate").GetString());
        Assert.DoesNotContain("registry_path", item.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private string Write(string name, string json)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, json, new UTF8Encoding(false));
        files.Add(path);
        return path;
    }

    private static string Fixture(string name, [CallerFilePath] string source = "") => File.ReadAllText(
        Path.Combine(Path.GetDirectoryName(source)!, "..", "AiTraderMetadata", "Fixtures", "test-only-" + name + ".json"));

    public void Dispose()
    {
        foreach (var file in files) File.Delete(file);
        Directory.Delete(directory);
    }
}
