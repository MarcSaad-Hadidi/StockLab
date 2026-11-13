using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StockLab.Application.DTOs.AiTrader;
using static StockLab.Infrastructure.AiTraderMetadata.MetadataJson;

namespace StockLab.Infrastructure.Trading;

public sealed record BoostedDiagnosticOptions(string PythonExecutable, string MlDirectory, string RegistryPath,
    string RegistrySha256, string ArtifactsDirectory, string ModelVersion, string BundleDirectory,
    string ManifestSha256, DateTimeOffset AsOfUtc, bool TrustedArtifacts = false, int TimeoutSeconds = 60);

/// <summary>Explicit read-only child process; never implements the live inference/persistence interface.</summary>
public sealed class PythonBoostedDiagnosticRunner
{
    private const int StreamLimit = 65536;
    private static readonly string[] Symbols = ["AAPL", "JNJ", "JPM", "MSFT", "XOM"];

    public async Task<BoostedDiagnosticBatch> RunAsync(BoostedDiagnosticOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process();
        var started = false;
        Task<string>? stdout = null, stderr = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            Require(options.TrustedArtifacts && options.TimeoutSeconds is >= 1 and <= 300
                && options.AsOfUtc.Offset == TimeSpan.Zero && options.AsOfUtc <= DateTimeOffset.UtcNow
                && options.AsOfUtc.Ticks % 10 == 0 && DigestText(options.RegistrySha256) && DigestText(options.ManifestSha256)
                && Regex.IsMatch(options.ModelVersion, @"^v[0-9]{6,}\z", RegexOptions.CultureInvariant));
            foreach (var path in new[] { options.PythonExecutable, options.MlDirectory, options.RegistryPath, options.ArtifactsDirectory, options.BundleDirectory })
                Require(!string.IsNullOrWhiteSpace(path) && path.Length <= 4096 && Path.IsPathFullyQualified(path)
                    && !path.Split(['/', '\\']).Contains("..", StringComparer.Ordinal));
            Require(File.Exists(options.PythonExecutable) && Directory.Exists(options.MlDirectory)
                && Directory.Exists(options.ArtifactsDirectory) && Directory.Exists(options.BundleDirectory));
            VerifyRegistry(options);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            var start = new ProcessStartInfo(options.PythonExecutable)
            {
                WorkingDirectory = options.MlDirectory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true)
            };
            start.Environment["PYTHONPATH"] = Path.Combine(options.MlDirectory, "src");
            start.Environment["PYTHONIOENCODING"] = "utf-8";
            start.Environment["OMP_NUM_THREADS"] = "1";
            foreach (var argument in new[] { "-B", "-m", "stocklab_ml.boosted_diagnostic", "--registry-path", options.RegistryPath,
                "--registry-sha256", options.RegistrySha256, "--artifacts-dir", options.ArtifactsDirectory,
                "--model-version", options.ModelVersion, "--bundle-dir", options.BundleDirectory,
                "--manifest-sha256", options.ManifestSha256, "--as-of-utc", options.AsOfUtc.ToString("O", CultureInfo.InvariantCulture),
                "--trusted-artifacts" }) start.ArgumentList.Add(argument);
            process.StartInfo = start;
            started = process.Start();
            Require(started);
            stdout = ReadBoundedAsync(process.StandardOutput.BaseStream, timeout.Token);
            stderr = ReadBoundedAsync(process.StandardError.BaseStream, timeout.Token);
            var pending = new List<Task> { stdout, stderr, process.WaitForExitAsync(timeout.Token) };
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                await completed;
                pending.Remove(completed);
            }
            Require(process.ExitCode == 0);
            var batch = ParseBatch(await stdout, options, DateTimeOffset.UtcNow);
            VerifyRegistry(options);
            return batch;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { throw new InvalidOperationException("boosted_diagnostic_failed"); }
        finally
        {
            timeout.Cancel();
            if (started)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            if (stdout is not null && stderr is not null)
            {
                try { await Task.WhenAll(stdout, stderr); }
                catch (Exception) { }
            }
        }
    }

    private static void VerifyRegistry(BoostedDiagnosticOptions options)
    {
        var info = new FileInfo(options.RegistryPath);
        Require(info.Exists && info.Length <= 8 * 1024 * 1024 && !info.Attributes.HasFlag(FileAttributes.ReparsePoint));
        using var file = info.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(Convert.ToHexStringLower(SHA256.HashData(file)) == options.RegistrySha256);
    }

    private static async Task<string> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            Require(bytes.Length + count <= StreamLimit);
            bytes.Write(buffer, 0, count);
        }
        return new UTF8Encoding(false, true).GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
    }

    private static bool DigestText(string text) => Regex.IsMatch(text, @"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant);

    private static double Finite(JsonElement row, string name)
    {
        var value = row.GetProperty(name);
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number));
        return value.GetDouble();
    }

    private static DateTimeOffset DiagnosticUtc(JsonElement root, string name)
    {
        var text = Text(root, name);
        Require(Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,6})?(?:Z|\+00:00)\z", RegexOptions.CultureInvariant)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)
            && timestamp.Offset == TimeSpan.Zero);
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }

    internal static BoostedDiagnosticBatch ParseBatch(string json, BoostedDiagnosticOptions expected, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4, AllowDuplicateProperties = false });
        var root = document.RootElement;
        Keys(root, "schema_version mode execution_eligible model_name model_version model_status target_name target_definition horizon_sessions policy "
            + "feature_date as_of_utc prepared_at_utc inferred_at_utc trained_at_utc fit_boundary model_created_after_as_of prospective_status "
            + "input_manifest_sha256 feature_frame_sha256 policy_context_sha256 artifact_sha256 registry_sha256 decisions");
        Require(Count(root, "schema_version") == 1 && Text(root, "mode") == "diagnostic_only" && !Boolean(root, "execution_eligible")
            && Text(root, "model_name") == "hgb_swing20_market28" && Version(root, "model_version") == expected.ModelVersion
            && Text(root, "model_status") == "Archived" && Text(root, "target_name") == "target_20"
            && Text(root, "target_definition") == "open[t+21]/open[t+1]-1 > .002" && Count(root, "horizon_sessions") == 20
            && Text(root, "policy") == "trend200-immediate-v1" && Text(root, "prospective_status") == "PENDING"
            && Hash(root, "registry_sha256") == expected.RegistrySha256 && Hash(root, "input_manifest_sha256") == expected.ManifestSha256);
        var feature = Day(root, "feature_date");
        var boundary = Day(root, "fit_boundary");
        var asOf = DiagnosticUtc(root, "as_of_utc");
        var prepared = DiagnosticUtc(root, "prepared_at_utc");
        var inferred = DiagnosticUtc(root, "inferred_at_utc");
        var trained = DiagnosticUtc(root, "trained_at_utc");
        Require(asOf == expected.AsOfUtc && asOf <= prepared && prepared <= inferred && inferred <= now
            && trained <= inferred && boundary <= feature && feature < DateOnly.FromDateTime(asOf.UtcDateTime)
            && Boolean(root, "model_created_after_as_of") == (trained > asOf));
        var items = root.GetProperty("decisions");
        Require(items.ValueKind == JsonValueKind.Array && items.GetArrayLength() == Symbols.Length);
        var rows = new List<BoostedDiagnosticDecision>();
        foreach (var row in items.EnumerateArray())
        {
            Keys(row, "symbol probability_up action reason close_ma50 close_ma200 momentum_20");
            var symbol = Text(row, "symbol");
            Require(symbol == Symbols[rows.Count]);
            var p = Finite(row, "probability_up");
            var ma50 = Finite(row, "close_ma50");
            var ma200 = Finite(row, "close_ma200");
            var momentum = Finite(row, "momentum_20");
            Require(p is >= 0 and <= 1);
            var action = p >= .65 && ma50 > 0 ? "BUY" : p <= .35 && ma200 < 0 && momentum < 0 ? "SELL" : "HOLD";
            var reason = action != "HOLD" ? "up_model" : p >= .65 ? "buy_trend_failed" : p <= .35 ? "sell_trend_failed" : "threshold_hold";
            Require(Text(row, "action") == action && Text(row, "reason") == reason);
            rows.Add(new(symbol, p, action, reason, ma50, ma200, momentum));
        }
        return new(expected.ModelVersion, feature, asOf, prepared, inferred, trained, boundary, trained > asOf,
            expected.ManifestSha256, Hash(root, "feature_frame_sha256"), Hash(root, "policy_context_sha256"),
            Hash(root, "artifact_sha256"), expected.RegistrySha256, rows.ToArray());
    }
}
