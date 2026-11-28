using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class PythonBoostedDiagnosticRunnerTests
{
    internal static readonly DateTimeOffset AsOf = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
    internal static string Valid => """
        {"schema_version":1,"mode":"diagnostic_only","execution_eligible":false,
        "model_name":"hgb_swing20_market28","model_version":"v000001","model_status":"Archived",
        "target_name":"target_20","target_definition":"open[t+21]/open[t+1]-1 > .002","horizon_sessions":20,
        "policy":"trend200-immediate-v1","feature_date":"2026-10-01","as_of_utc":"2026-10-02T00:00:00+00:00",
        "prepared_at_utc":"2026-10-20T15:30:00+00:00","inferred_at_utc":"2026-10-20T15:30:01+00:00",
        "trained_at_utc":"2026-10-14T14:00:00+00:00","fit_boundary":"2026-01-02","model_created_after_as_of":true,
        "prospective_status":"PENDING","input_manifest_sha256":"MANIFEST","feature_frame_sha256":"FRAME",
        "policy_context_sha256":"CONTEXT","artifact_sha256":"ARTIFACT","registry_sha256":"REGISTRY",
        "decisions":[
        {"symbol":"AAPL","probability_up":0.99,"action":"HOLD","reason":"buy_trend_failed","close_ma50":-0.01,"close_ma200":-0.01,"momentum_20":-0.01},
        {"symbol":"JNJ","probability_up":0.65,"action":"BUY","reason":"up_model","close_ma50":0.01,"close_ma200":-0.01,"momentum_20":-0.01},
        {"symbol":"JPM","probability_up":0.35,"action":"SELL","reason":"up_model","close_ma50":-0.01,"close_ma200":-0.01,"momentum_20":-0.01},
        {"symbol":"MSFT","probability_up":0.1,"action":"HOLD","reason":"sell_trend_failed","close_ma50":-0.01,"close_ma200":0,"momentum_20":-0.01},
        {"symbol":"XOM","probability_up":0.5,"action":"HOLD","reason":"threshold_hold","close_ma50":0.01,"close_ma200":-0.01,"momentum_20":-0.01}]}
        """.Replace("MANIFEST", new string('a', 64)).Replace("FRAME", new string('b', 64))
        .Replace("CONTEXT", new string('c', 64)).Replace("ARTIFACT", new string('d', 64)).Replace("REGISTRY", new string('e', 64));

    [Fact]
    public void Typed_diagnostic_preserves_raw_hold_and_inclusive_boundaries_without_confidence_or_live_ids()
    {
        var result = PythonBoostedDiagnosticRunner.ParseBatch(Valid, Expected(), DateTimeOffset.UtcNow);
        Assert.False(result.ExecutionEligible);
        Assert.Equal(.99, result.Decisions[0].ProbabilityUp);
        Assert.Equal("HOLD", result.Decisions[0].Action);
        Assert.Equal("BUY", result.Decisions[1].Action);
        Assert.Equal("SELL", result.Decisions[2].Action);
        Assert.True(result.ModelCreatedAfterAsOf);
        Assert.DoesNotContain(result.GetType().GetProperties(), property => property.Name.Contains("Confidence") || property.Name == "DecisionId");
    }

    [Theory]
    [InlineData("eligible")]
    [InlineData("confidence")]
    [InlineData("active")]
    [InlineData("version")]
    [InlineData("registry")]
    [InlineData("manifest")]
    [InlineData("target")]
    [InlineData("horizon")]
    [InlineData("policy")]
    [InlineData("backward")]
    [InlineData("asof")]
    [InlineData("preparation")]
    [InlineData("future")]
    [InlineData("flag")]
    [InlineData("probability")]
    [InlineData("action")]
    [InlineData("reason")]
    [InlineData("count")]
    [InlineData("symbol_order")]
    [InlineData("row_extra")]
    [InlineData("pending")]
    public void Closed_payload_rejects_forged_semantics_and_provenance(string kind)
    {
        var node = JsonNode.Parse(Valid)!;
        switch (kind)
        {
            case "eligible": node["execution_eligible"] = true; break;
            case "confidence": node["confidence"] = null; break;
            case "active": node["model_status"] = "Active"; break;
            case "version": node["model_version"] = "v000002"; break;
            case "registry": node["registry_sha256"] = new string('f', 64); break;
            case "manifest": node["input_manifest_sha256"] = new string('f', 64); break;
            case "target": node["target_name"] = "target_up_1d"; break;
            case "horizon": node["horizon_sessions"] = 63; break;
            case "policy": node["policy"] = "thresholds"; break;
            case "backward": node["feature_date"] = "2025-12-31"; break;
            case "asof": node["as_of_utc"] = "2026-10-03T00:00:00+00:00"; break;
            case "preparation": node["prepared_at_utc"] = "2025-01-01T00:00:00+00:00"; break;
            case "future": node["inferred_at_utc"] = "2099-01-01T00:00:00+00:00"; break;
            case "flag": node["model_created_after_as_of"] = false; break;
            case "probability": node["decisions"]![0]!["probability_up"] = 1.1; break;
            case "action": node["decisions"]![0]!["action"] = "BUY"; break;
            case "reason": node["decisions"]![0]!["reason"] = "threshold_hold"; break;
            case "count": node["decisions"]!.AsArray().RemoveAt(4); break;
            case "symbol_order": node["decisions"]![0]!["symbol"] = "XOM"; break;
            case "row_extra": node["decisions"]![0]!["confidence"] = null; break;
            case "pending": node["prospective_status"] = "AVAILABLE"; break;
        }
        Assert.ThrowsAny<Exception>(() => PythonBoostedDiagnosticRunner.ParseBatch(node.ToJsonString(), Expected(), DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("nonfinite")]
    [InlineData("truncated")]
    public void Ambiguous_or_nonfinite_json_is_refused(string kind)
    {
        var json = kind switch { "duplicate" => Valid.Replace("\"schema_version\":1", "\"schema_version\":1,\"schema_version\":1"),
            "nonfinite" => Valid.Replace("0.99", "NaN"), _ => Valid[..^10] };
        Assert.ThrowsAny<Exception>(() => PythonBoostedDiagnosticRunner.ParseBatch(json, Expected(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Real_process_uses_only_explicit_arguments_and_returns_typed_diagnostic()
    {
        using var fixture = new ProcessFixture("import base64,sys\nsys.stdout.write(base64.b64decode('" +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Valid)) + "').decode('utf-8'))", verifyArguments: true);
        var result = await new PythonBoostedDiagnosticRunner().RunAsync(fixture.Options);
        Assert.Equal(5, result.Decisions.Count);
        Assert.False(result.ExecutionEligible);
    }

    [Fact]
    public async Task Submicrosecond_asof_is_refused_before_launch_without_rounding()
    {
        using var fixture = new ProcessFixture("import pathlib\npathlib.Path('started').touch()");
        var options = fixture.Options with { AsOfUtc = fixture.Options.AsOfUtc.AddTicks(1) };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new PythonBoostedDiagnosticRunner().RunAsync(options));
        Assert.Equal("boosted_diagnostic_failed", error.Message);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "started")));
    }

    [Theory]
    [InlineData("import sys;sys.stderr.write('PRIVATE-PATH');sys.exit(1)")]
    [InlineData("import sys;sys.stdout.write('x'*70000)")]
    [InlineData("import sys;sys.stderr.write('x'*70000)")]
    public async Task Child_failures_and_stream_limits_are_safe(string script)
    {
        using var fixture = new ProcessFixture(script);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new PythonBoostedDiagnosticRunner().RunAsync(fixture.Options));
        Assert.Equal("boosted_diagnostic_failed", error.Message);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_and_cancellation_clean_up_the_child(bool cancel)
    {
        using var fixture = new ProcessFixture("import pathlib,os,time\npathlib.Path('pid').write_text(str(os.getpid()))\ntime.sleep(30)");
        using var cancellation = new CancellationTokenSource();
        if (cancel) cancellation.CancelAfter(TimeSpan.FromSeconds(1));
        var options = fixture.Options with { TimeoutSeconds = cancel ? 10 : 1 };
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PythonBoostedDiagnosticRunner().RunAsync(options, cancellation.Token));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => new PythonBoostedDiagnosticRunner().RunAsync(options));
        var pidPath = Path.Combine(fixture.Root, "pid");
        if (File.Exists(pidPath))
        {
            try { using var process = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(pidPath))); Assert.True(process.HasExited); }
            catch (ArgumentException) { }
        }
    }

    internal static BoostedDiagnosticOptions Expected() => new("C:/python.exe", "C:/ml", "C:/registry.json", new string('e', 64),
        "C:/artifacts", "v000001", "C:/bundle", new string('a', 64), AsOf, true);

    internal sealed class ProcessFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "stocklab boosted diagnostic " + Guid.NewGuid().ToString("N"));
        public BoostedDiagnosticOptions Options { get; }
        public ProcessFixture(string script, bool verifyArguments = false, string? validPayload = null)
        {
            var module = Path.Combine(Root, "src", "stocklab_ml");
            Directory.CreateDirectory(module);
            Directory.CreateDirectory(Path.Combine(Root, "artifacts"));
            Directory.CreateDirectory(Path.Combine(Root, "bundle"));
            File.WriteAllText(Path.Combine(module, "__init__.py"), "");
            var registry = Path.Combine(Root, "registry.json");
            File.WriteAllText(registry, "{}\n");
            var registryHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(registry)));
            script = script.Replace(new string('e', 64), registryHash);
            // The valid payload is base64 encoded, so replace its encoded form as well.
            var payload = validPayload ?? Valid;
            script = script.Replace(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)),
                Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.Replace(new string('e', 64), registryHash))));
            var python = Environment.GetEnvironmentVariable("STOCKLAB_TEST_PYTHON")
                ?? (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                    .Select(path => Path.Combine(path, OperatingSystem.IsWindows() ? "python.exe" : "python3")).First(File.Exists);
            Options = Expected() with { PythonExecutable = python, MlDirectory = Root, RegistryPath = registry,
                RegistrySha256 = registryHash, ArtifactsDirectory = Path.Combine(Root, "artifacts"), BundleDirectory = Path.Combine(Root, "bundle"), TimeoutSeconds = 10 };
            if (verifyArguments)
            {
                var expectedArguments = new[] { "--registry-path", registry, "--registry-sha256", registryHash,
                    "--artifacts-dir", Options.ArtifactsDirectory, "--model-version", Options.ModelVersion,
                    "--bundle-dir", Options.BundleDirectory, "--manifest-sha256", Options.ManifestSha256,
                    "--as-of-utc", Options.AsOfUtc.ToString("O"), "--trusted-artifacts" };
                script = "import sys,os\nassert sys.argv[1:]==" + System.Text.Json.JsonSerializer.Serialize(expectedArguments)
                    + "\nassert os.environ['OMP_NUM_THREADS']=='1'\nassert os.environ['PYTHONPATH']=="
                    + System.Text.Json.JsonSerializer.Serialize(Path.Combine(Root, "src")) + "\n" + script;
            }
            File.WriteAllText(Path.Combine(module, "boosted_diagnostic.py"), script);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
