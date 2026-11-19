using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using StockLab.Infrastructure.Trading;

namespace StockLab.Api.Operations;

/// <summary>Runs before host/configuration construction; no service provider or persistable batch exists.</summary>
public static class BoostedDiagnosticCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var flags = new HashSet<string>(StringComparer.Ordinal);
            var required = new[] { "--python-executable", "--ml-directory", "--registry-path", "--registry-sha256",
                "--artifacts-dir", "--model-version", "--bundle-dir", "--manifest-sha256", "--as-of-utc" };
            for (var index = 0; index < args.Length; index++)
            {
                var key = args[index];
                if (key is "--diagnose-boosted" or "--trusted-artifacts")
                {
                    if (!flags.Add(key)) throw new ArgumentException();
                }
                else
                {
                    if ((!required.Contains(key, StringComparer.Ordinal) && key != "--timeout-seconds")
                        || index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal)
                        || !values.TryAdd(key, args[++index])) throw new ArgumentException();
                }
            }
            if (flags.Count != 2 || required.Any(key => !values.ContainsKey(key))) throw new ArgumentException();
            var text = values["--as-of-utc"];
            if (!Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)\z", RegexOptions.CultureInvariant)
                || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf)
                || asOf.Offset != TimeSpan.Zero || asOf.Ticks % 10 != 0) throw new ArgumentException();
            var timeout = values.TryGetValue("--timeout-seconds", out var timeoutText)
                ? int.Parse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture) : 60;
            var options = new BoostedDiagnosticOptions(values["--python-executable"], values["--ml-directory"],
                values["--registry-path"], values["--registry-sha256"], values["--artifacts-dir"], values["--model-version"],
                values["--bundle-dir"], values["--manifest-sha256"], asOf, true, timeout);
            var batch = await new PythonBoostedDiagnosticRunner().RunAsync(options, cancellationToken);
            await output.WriteLineAsync(JsonSerializer.Serialize(batch, new JsonSerializerOptions
                { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { await error.WriteLineAsync("boosted_diagnostic_cancelled"); return 1; }
        catch (Exception)
        { await error.WriteLineAsync("boosted_diagnostic_failed"); return 1; }
    }
}
