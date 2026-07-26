using System.Globalization;
using System.IO;
using System.Text.Json;
using UsageBeacon.Models.Insights;

namespace UsageBeacon.Services.Insights;

/// <summary>
/// Extracts billed token usage from Claude Code transcript files
/// (<c>~/.claude/projects/**/*.jsonl</c>). Only numeric usage, timestamps,
/// model names, and message identifiers are read; message content is never
/// materialized beyond the transient line buffer.
/// </summary>
public static class ClaudeTranscriptReader
{
    /// <summary>
    /// Parses one transcript file. Repeated emissions of the same message
    /// (the transcript rewrites an assistant record as it streams) are
    /// deduplicated by message id + request id; the last occurrence wins.
    /// Malformed lines are skipped.
    /// </summary>
    public static IReadOnlyList<TokenUsageEntry> ParseFile(string path)
    {
        var byId = new Dictionary<long, TokenUsageEntry>();
        foreach (var line in File.ReadLines(path))
        {
            // Cheap pre-filter before paying for JSON parsing.
            if (!line.Contains("\"assistant\"", StringComparison.Ordinal) ||
                !line.Contains("\"usage\"", StringComparison.Ordinal))
                continue;

            var entry = ParseLine(line);
            if (entry != null) byId[entry.IdHash] = entry;
        }
        return byId.Values.ToList();
    }

    internal static TokenUsageEntry? ParseLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("type", out var type) ||
                type.GetString() != "assistant")
                return null;
            if (!root.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.Object)
                return null;
            if (!message.TryGetProperty("usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object)
                return null;

            var model = message.TryGetProperty("model", out var modelProp)
                ? modelProp.GetString()
                : null;
            if (string.IsNullOrEmpty(model) || model == "<synthetic>") return null;

            if (!root.TryGetProperty("timestamp", out var tsProp) ||
                !TryParseUtc(tsProp.GetString(), out var timestampUtc))
                return null;

            var messageId = message.TryGetProperty("id", out var idProp)
                ? idProp.GetString()
                : null;
            if (string.IsNullOrEmpty(messageId)) return null;
            var requestId = root.TryGetProperty("requestId", out var reqProp)
                ? reqProp.GetString() ?? ""
                : "";

            var input = GetLong(usage, "input_tokens");
            var cacheRead = GetLong(usage, "cache_read_input_tokens");
            var output = GetLong(usage, "output_tokens");
            long write5m, write1h;
            if (usage.TryGetProperty("cache_creation", out var creation) &&
                creation.ValueKind == JsonValueKind.Object)
            {
                write5m = GetLong(creation, "ephemeral_5m_input_tokens");
                write1h = GetLong(creation, "ephemeral_1h_input_tokens");
            }
            else
            {
                // Older records only expose the combined counter; bill it at
                // the cheaper five-minute write rate.
                write5m = GetLong(usage, "cache_creation_input_tokens");
                write1h = 0;
            }

            return new TokenUsageEntry(
                IdHash: UsageLogHashing.Hash($"{messageId}:{requestId}"),
                TimestampUtc: timestampUtc,
                Service: UsageService.Claude,
                Model: model,
                InputTokens: input,
                CachedInputTokens: cacheRead,
                CacheWrite5mTokens: write5m,
                CacheWrite1hTokens: write1h,
                OutputTokens: output);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static bool TryParseUtc(string? value, out DateTime utc)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            utc = parsed.UtcDateTime;
            return true;
        }
        utc = default;
        return false;
    }

    internal static long GetLong(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : 0;
}
