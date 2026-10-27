using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockLab.Api.Operations;

internal sealed record AiTradingCycleJournal(int SchemaVersion, string Database, DateOnly Session,
    string ModelName, string ModelVersion, string[] Symbols, string DatasetSha256, Guid[] DecisionIds,
    Guid CaptureId, DateTime StartedAtUtc, string Stage, string? FailureCode = null)
{
    private static readonly JsonSerializerOptions Json = new() {
        AllowDuplicateProperties = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 4
    };

    internal static AiTradingCycleJournal Read(string path)
    {
        if (new FileInfo(path).Length > 65536 || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException();
        var value = JsonSerializer.Deserialize<AiTradingCycleJournal>(File.ReadAllBytes(path), Json)
            ?? throw new InvalidOperationException();
        if (value.SchemaVersion != 1 || value.DecisionIds is null || value.Symbols is null
            || value.Symbols.Length is < 1 or > 10 || value.Symbols.Distinct(StringComparer.Ordinal).Count() != value.Symbols.Length
            || value.DecisionIds.Length > 10 || value.DecisionIds.Contains(Guid.Empty)
            || value.DecisionIds.Distinct().Count() != value.DecisionIds.Length || value.CaptureId == Guid.Empty
            || value.StartedAtUtc == default || value.StartedAtUtc.Kind != DateTimeKind.Utc
            || value.Stage is not ("INFERENCE" or "EXECUTION" or "PERFORMANCE" or "COMPLETED" or "ABANDONED")
            || value.Stage is not ("INFERENCE" or "ABANDONED") && value.DecisionIds.Length != value.Symbols.Length
            || string.IsNullOrWhiteSpace(value.Database) || string.IsNullOrWhiteSpace(value.ModelName)
            || string.IsNullOrWhiteSpace(value.ModelVersion) || value.DatasetSha256 is null
            || !System.Text.RegularExpressions.Regex.IsMatch(value.DatasetSha256, "\\A[a-f0-9]{64}\\z"))
            throw new InvalidOperationException();
        return value;
    }

    internal void Save(string path)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, this, Json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
