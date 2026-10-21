using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StockLab.Application.Exceptions;

namespace StockLab.Infrastructure.AiTraderMetadata;

internal static class MetadataJson
{
    internal const int MaxFileBytes = 8 * 1024 * 1024;

    internal static async Task<JsonDocument> ReadAsync(string? path, string catalog, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new CatalogUnavailableException(catalog);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            Require(stream.Length <= MaxFileBytes);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, token)) > 0)
            {
                Require(bytes.Length + count <= MaxFileBytes);
                bytes.Write(buffer, 0, count);
            }
            var json = new UTF8Encoding(false, true).GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
            var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            try { ValidateTree(document.RootElement); }
            catch { document.Dispose(); throw; }
            return document;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { throw new CatalogUnavailableException(catalog); }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or FormatException or ArgumentException)
        { throw new CatalogUnavailableException(catalog, true); }
    }

    private static void ValidateTree(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                Require(keys.Add(property.Name));
                ValidateTree(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateTree(item);
        else if (value.ValueKind == JsonValueKind.Number)
            Require(value.TryGetDouble(out var number) && double.IsFinite(number));
    }

    internal static void Require(bool valid)
    {
        if (!valid) throw new FormatException("Metadata does not match its source contract.");
    }

    internal static void Keys(JsonElement value, string fields)
    {
        Require(value.ValueKind == JsonValueKind.Object);
        var expected = fields.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Require(value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expected));
    }

    internal static string Text(JsonElement value) { Require(value.ValueKind == JsonValueKind.String); return value.GetString()!; }
    internal static string Text(JsonElement value, string key) => Text(value.GetProperty(key));
    internal static int Count(JsonElement value, string key, bool positive = false)
    {
        var item = value.GetProperty(key);
        Require(item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out _));
        var count = item.GetInt32();
        Require(count >= (positive ? 1 : 0));
        return count;
    }

    internal static decimal Number(JsonElement value, string key)
    {
        var item = value.GetProperty(key);
        Require(item.ValueKind == JsonValueKind.Number && item.TryGetDecimal(out _));
        return item.GetDecimal();
    }

    internal static double Real(JsonElement value, string key) => (double)Number(value, key);
    internal static decimal Rate(JsonElement value, string key) { var number = Number(value, key); Require(number is >= 0 and <= 1); return number; }
    internal static bool Boolean(JsonElement value, string key)
    {
        var item = value.GetProperty(key);
        Require(item.ValueKind is JsonValueKind.True or JsonValueKind.False);
        return item.GetBoolean();
    }

    internal static DateOnly Day(JsonElement value, string key) => Day(Text(value, key));
    internal static DateOnly Day(string text)
    {
        Require(DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result));
        return result;
    }

    internal static DateTimeOffset Timestamp(JsonElement value, string key)
    {
        var text = Text(value, key);
        Require(Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{6})?\+00:00\z", RegexOptions.CultureInvariant));
        Require(DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) && timestamp.Offset == TimeSpan.Zero);
        return timestamp;
    }

    internal static Guid Uuid(JsonElement value, string key)
    {
        var text = Text(value, key);
        Require(Guid.TryParseExact(text, "D", out var result) && result != Guid.Empty && result.ToString("D") == text);
        return result;
    }

    internal static string Name(JsonElement value, string key = "model_name")
    {
        var text = Text(value, key);
        Require(Regex.IsMatch(text, @"^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\z", RegexOptions.CultureInvariant));
        return text;
    }

    internal static string Version(JsonElement value, string key)
    {
        var text = Text(value, key);
        Require(Regex.IsMatch(text, @"^v[0-9]{6,}\z", RegexOptions.CultureInvariant)
            && int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number > 0 && "v" + number.ToString("D6", CultureInfo.InvariantCulture) == text);
        return text;
    }

    internal static string Hash(JsonElement value, string key)
    {
        var text = Text(value, key);
        Require(Regex.IsMatch(text, @"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant));
        return text;
    }

    internal static string[] Symbols(JsonElement value, string key = "symbols", bool allowEmpty = false)
    {
        var array = value.GetProperty(key);
        Require(array.ValueKind == JsonValueKind.Array);
        var items = array.EnumerateArray().Select(Text).ToArray();
        Require((allowEmpty || items.Length > 0) && items.SequenceEqual(items.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            && items.All(item => Regex.IsMatch(item, @"^[A-Z0-9][A-Z0-9.\^/-]{0,31}(?::[A-Z0-9][A-Z0-9._-]{0,15})?\z", RegexOptions.CultureInvariant)));
        return items;
    }

    internal static void Close(decimal actual, decimal expected) => Require(Math.Abs(actual - expected) <= Math.Max(.000000000001m, Math.Abs(expected) * .000000001m));

    internal static string Digest(JsonElement value, string? excludedProperty = null)
    {
        var builder = new StringBuilder();
        Canonical(value, builder, excludedProperty);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    // shortcut: hashes preserve Python numeric lexemes; re-export audits if another serializer rewrites numbers.
    private static void Canonical(JsonElement value, StringBuilder builder, string? excludedProperty = null)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var first = true;
                foreach (var property in value.EnumerateObject().Where(item => item.Name != excludedProperty).OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    if (!first) builder.Append(", ");
                    first = false;
                    Quote(property.Name, builder);
                    builder.Append(": ");
                    Canonical(property.Value, builder);
                }
                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                for (var index = 0; index < value.GetArrayLength(); index++)
                {
                    if (index > 0) builder.Append(", ");
                    Canonical(value[index], builder);
                }
                builder.Append(']');
                break;
            case JsonValueKind.String: Quote(Text(value), builder); break;
            default: builder.Append(value.GetRawText()); break;
        }
    }

    private static void Quote(string text, StringBuilder builder)
    {
        builder.Append('"');
        foreach (var character in text)
            builder.Append(character switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                < ' ' => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture), _ => character.ToString()
            });
        builder.Append('"');
    }
}
