using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.AiTraderMetadata;

namespace StockLab.Infrastructure.Trading;

public sealed class PythonAiDecisionInferenceProvider(IOptions<AiDecisionPipelineOptions> options,
    IOptions<AiTraderMetadataOptions> metadata) : IAiDecisionInferenceProvider
{
    private const int MaximumOutputCharacters = 65536;

    public async Task<AiInferenceBatch> InferAsync(DateOnly decisionDate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value;
        using var process = new Process();
        var started = false;
        Task<string>? stdout = null, stderr = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            if (!settings.TrustedArtifacts || settings.TimeoutSeconds is < 1 or > 300
                || string.IsNullOrWhiteSpace(settings.ModelName) || settings.ModelName != settings.ModelName.Trim()
                || settings.ModelName.Length > 128 || settings.Symbols is null || settings.Symbols.Length is < 1 or > 100
                || settings.Symbols.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 32 || s != s.ToUpperInvariant()
                    || s.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || s.Contains(','))
                || settings.Symbols.Distinct(StringComparer.Ordinal).Count() != settings.Symbols.Length
                || new[] { settings.PythonExecutable, settings.MlDirectory, settings.DatasetPath,
                    settings.ArtifactsDirectory, metadata.Value.RegistryPath }.Any(p => string.IsNullOrWhiteSpace(p) || !Path.IsPathFullyQualified(p)))
                throw new InvalidOperationException();
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var start = new ProcessStartInfo(settings.PythonExecutable!)
            {
                WorkingDirectory = settings.MlDirectory!, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            start.Environment["PYTHONPATH"] = Path.Combine(settings.MlDirectory!, "src");
            start.Environment["PYTHONIOENCODING"] = "utf-8";
            foreach (var argument in new[] { "-m", "stocklab_ml.decision_pipeline", "--registry-path", metadata.Value.RegistryPath!,
                "--artifacts-dir", settings.ArtifactsDirectory!, "--dataset-path", settings.DatasetPath!,
                "--model-name", settings.ModelName, "--decision-date", decisionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "--trusted-artifacts", "--symbols" }.Concat(settings.Symbols))
                start.ArgumentList.Add(argument);
            process.StartInfo = start;
            started = process.Start();
            if (!started) throw new InvalidOperationException();
            stdout = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            stderr = ReadBoundedAsync(process.StandardError, timeout.Token);
            var pending = new List<Task> { stdout, stderr, process.WaitForExitAsync(timeout.Token) };
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                await completed;
                pending.Remove(completed);
            }
            if (process.ExitCode != 0) throw new InvalidOperationException();
            return ParseBatch(await stdout, decisionDate, settings.ModelName, settings.Symbols);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Never expose child stderr, executable paths, model data or connection details.
            throw new InvalidOperationException("decision_inference_failed");
        }
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

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var content = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            if (content.Length + count > MaximumOutputCharacters) throw new InvalidOperationException();
            content.Append(buffer, 0, count);
        }
        return content.ToString();
    }

    internal static AiInferenceBatch ParseBatch(string json, DateOnly session, string modelName, string[] symbols)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4, AllowDuplicateProperties = false });
        var root = document.RootElement;
        Keys(root, "schema_version", "model_name", "model_version", "decision_date", "decisions");
        var version = root.GetProperty("model_version").GetString();
        if (root.GetProperty("schema_version").GetInt32() != 1
            || root.GetProperty("model_name").GetString() != modelName
            || string.IsNullOrWhiteSpace(version) || version.Length > 128 || version != version.Trim()
            || root.GetProperty("decision_date").GetString() != session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            throw new InvalidOperationException();
        var rows = root.GetProperty("decisions");
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != symbols.Length)
            throw new InvalidOperationException();
        var decisions = new List<AiInferenceSignal>();
        foreach (var row in rows.EnumerateArray())
        {
            Keys(row, "symbol", "signal", "confidence");
            var symbol = row.GetProperty("symbol").GetString();
            var confidence = row.GetProperty("confidence").GetString();
            if (symbol is null || !symbols.Contains(symbol, StringComparer.Ordinal)
                || decisions.Any(d => d.Symbol == symbol) || confidence is null
                || !Regex.IsMatch(confidence, @"\A(?:0(?:\.[0-9]{1,28})?|1(?:\.0{1,28})?)\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException();
            var signal = row.GetProperty("signal").GetString() switch
            {
                "BUY" => AiTradingSignal.Buy, "SELL" => AiTradingSignal.Sell, "HOLD" => AiTradingSignal.Hold,
                _ => throw new InvalidOperationException()
            };
            decisions.Add(new(symbol, signal, decimal.Parse(confidence, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)));
        }
        return new(modelName, version, session, decisions);
    }

    private static void Keys(JsonElement value, params string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != keys.Length
            || keys.Any(key => !value.TryGetProperty(key, out _)))
            throw new InvalidOperationException();
    }
}
