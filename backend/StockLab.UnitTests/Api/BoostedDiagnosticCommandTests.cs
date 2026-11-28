using System.Text.Json;
using StockLab.Api.Operations;
using StockLab.UnitTests.Trading;

namespace StockLab.UnitTests.Api;

public sealed class BoostedDiagnosticCommandTests
{
    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("mixed")]
    [InlineData("untrusted")]
    public async Task Invalid_operator_arguments_fail_without_host_or_child(string kind)
    {
        var arguments = new List<string> { "--diagnose-boosted" };
        if (kind == "unknown") arguments.Add("--PRIVATE-UNKNOWN");
        if (kind == "duplicate") arguments.AddRange(["--model-version", "v000001", "--model-version", "v000001"]);
        if (kind == "mixed") arguments.Add("--run-ai-trader-cycle");
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.NotEqual(0, await BoostedDiagnosticCommand.RunAsync(arguments.ToArray(), output, error));
        Assert.Equal("", output.ToString());
        Assert.DoesNotContain("PRIVATE", error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Isolated_command_prints_only_validated_raw_diagnostic_json(bool trailingMicrosecondZeros)
    {
        var payload = PythonBoostedDiagnosticRunnerTests.Valid;
        if (trailingMicrosecondZeros) payload = payload.Replace("15:30:00+00:00", "15:30:00.123450+00:00")
            .Replace("15:30:01+00:00", "15:30:01.234560+00:00");
        using var fixture = new PythonBoostedDiagnosticRunnerTests.ProcessFixture("import base64,sys\nsys.stdout.write(base64.b64decode('" +
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload)) + "').decode('utf-8'))", validPayload: payload);
        var options = fixture.Options;
        var arguments = new[] { "--diagnose-boosted", "--python-executable", options.PythonExecutable,
            "--ml-directory", options.MlDirectory, "--registry-path", options.RegistryPath, "--registry-sha256", options.RegistrySha256,
            "--artifacts-dir", options.ArtifactsDirectory, "--model-version", options.ModelVersion, "--bundle-dir", options.BundleDirectory,
            "--manifest-sha256", options.ManifestSha256, "--as-of-utc", options.AsOfUtc.ToString("O"), "--trusted-artifacts" };
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, await BoostedDiagnosticCommand.RunAsync(arguments, output, error));
        Assert.Equal("", error.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("execution_eligible").GetBoolean());
        Assert.False(output.ToString().Contains("confidence", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(5, json.RootElement.GetProperty("decisions").GetArrayLength());
        Assert.True(json.RootElement.GetProperty("decisions")[0].TryGetProperty("momentum_20", out _));
        var reparsed = StockLab.Infrastructure.Trading.PythonBoostedDiagnosticRunner.ParseBatch(output.ToString(), options, DateTimeOffset.UtcNow);
        Assert.Equal(5, reparsed.Decisions.Count);
    }
}
