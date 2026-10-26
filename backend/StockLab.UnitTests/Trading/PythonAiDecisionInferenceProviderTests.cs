using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StockLab.Infrastructure.AiTraderMetadata;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class PythonAiDecisionInferenceProviderTests
{
    private const string Valid = """
        {"schema_version":1,"model_name":"random_forest_baseline","model_version":"v000001",
        "decision_date":"2026-10-02","decisions":[{"symbol":"AAPL","signal":"HOLD","confidence":"0.1234567890123456789012345678"}]}
        """;

    [Fact]
    public void Strict_output_preserves_decimal_and_exact_identity()
    {
        var batch = PythonAiDecisionInferenceProvider.ParseBatch(Valid, AiDecisionPipelineTests.Session,
            "random_forest_baseline", ["AAPL"]);
        Assert.Equal(.1234567890123456789012345678m, Assert.Single(batch.Decisions).Confidence);
        Assert.Equal("v000001", batch.ModelVersion);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("nan")]
    [InlineData("number")]
    [InlineData("precision")]
    [InlineData("model")]
    [InlineData("date")]
    [InlineData("symbol")]
    [InlineData("signal")]
    [InlineData("empty")]
    [InlineData("version")]
    public void Malformed_or_unexpected_inference_is_rejected(string invalid)
    {
        var json = invalid switch
        {
            "duplicate" => Valid.Replace("\"schema_version\":1", "\"schema_version\":1,\"schema_version\":1"),
            "unknown" => Valid.Replace("\"schema_version\":1", "\"extra\":1,\"schema_version\":1"),
            "nan" => Valid.Replace("\"0.1234567890123456789012345678\"", "NaN"),
            "number" => Valid.Replace("\"0.1234567890123456789012345678\"", "0.8"),
            "precision" => Valid.Replace("0.1234567890123456789012345678", "0.12345678901234567890123456789"),
            "model" => Valid.Replace("random_forest_baseline", "other_model"),
            "date" => Valid.Replace("2026-10-02", "2026-10-01"),
            "symbol" => Valid.Replace("AAPL", "MSFT"),
            "signal" => Valid.Replace("HOLD", "buy"),
            "version" => Valid.Replace("v000001", " "),
            _ => Valid.Replace("[{\"symbol\":\"AAPL\",\"signal\":\"HOLD\",\"confidence\":\"0.1234567890123456789012345678\"}]", "[]")
        };
        Assert.ThrowsAny<Exception>(() => PythonAiDecisionInferenceProvider.ParseBatch(json,
            AiDecisionPipelineTests.Session, "random_forest_baseline", ["AAPL"]));
    }

    [Fact]
    public async Task Real_process_uses_argument_list_and_validates_stdout()
    {
        using var fixture = new ProcessFixture("import base64,sys\nsys.stdout.write(base64.b64decode('" +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Valid)) + "').decode('utf-8'))");
        var result = await fixture.Provider().InferAsync(AiDecisionPipelineTests.Session);
        Assert.Single(result.Decisions);
        Assert.Equal("AAPL", result.Decisions[0].Symbol);
    }

    [Theory]
    [InlineData("import sys; sys.stderr.write('private-path-and-secret'); sys.exit(1)")]
    [InlineData("import sys; sys.stdout.write('x' * 70000)")]
    [InlineData("import sys; sys.stderr.write('x' * 70000)")]
    public async Task Process_failures_and_oversized_output_are_sanitized(string script)
    {
        using var fixture = new ProcessFixture(script);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Provider().InferAsync(AiDecisionPipelineTests.Session));
        Assert.Equal("decision_inference_failed", error.Message);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_and_caller_cancellation_stop_the_process(bool cancel)
    {
        using var fixture = new ProcessFixture("import os,pathlib,time\npathlib.Path('pid').write_text(str(os.getpid()))\ntime.sleep(30)");
        fixture.Settings.TimeoutSeconds = cancel ? 10 : 1;
        using var cancellation = new CancellationTokenSource();
        if (cancel) cancellation.CancelAfter(TimeSpan.FromSeconds(1));
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Provider().InferAsync(AiDecisionPipelineTests.Session, cancellation.Token));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Provider().InferAsync(AiDecisionPipelineTests.Session));
        var pidPath = Path.Combine(fixture.Root, "pid");
        if (File.Exists(pidPath))
        {
            var pid = int.Parse(await File.ReadAllTextAsync(pidPath), CultureInfo.InvariantCulture);
            try { using var child = Process.GetProcessById(pid); Assert.True(child.HasExited); }
            catch (ArgumentException) { }
        }
    }

    [Fact]
    public async Task Unconfigured_or_untrusted_provider_fails_without_launching_python()
    {
        using var fixture = new ProcessFixture("raise RuntimeError('must not run')");
        fixture.Settings.TrustedArtifacts = false;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Provider().InferAsync(AiDecisionPipelineTests.Session));
        Assert.Equal("decision_inference_failed", error.Message);
    }

    internal sealed class ProcessFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "stocklab inference " + Guid.NewGuid().ToString("N"));
        public AiDecisionPipelineOptions Settings { get; }
        public ProcessFixture(string code)
        {
            var module = Path.Combine(Root, "src", "stocklab_ml");
            Directory.CreateDirectory(module);
            File.WriteAllText(Path.Combine(module, "__init__.py"), "");
            File.WriteAllText(Path.Combine(module, "decision_pipeline.py"), code);
            var python = Environment.GetEnvironmentVariable("STOCKLAB_TEST_PYTHON")
                ?? (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                    .Select(p => Path.Combine(p, OperatingSystem.IsWindows() ? "python.exe" : "python3"))
                    .First(File.Exists);
            Settings = new() { PythonExecutable = python, MlDirectory = Root, DatasetPath = Path.Combine(Root, "prices.csv"),
                ArtifactsDirectory = Path.Combine(Root, "artifacts"), ModelName = "random_forest_baseline",
                Symbols = ["AAPL"], TrustedArtifacts = true, TimeoutSeconds = 10 };
        }
        public PythonAiDecisionInferenceProvider Provider() => new(Options.Create(Settings),
            Options.Create(new AiTraderMetadataOptions { RegistryPath = Path.Combine(Root, "registry.json") }));
        public void Dispose() { Directory.Delete(Root, recursive: true); }
    }
}
