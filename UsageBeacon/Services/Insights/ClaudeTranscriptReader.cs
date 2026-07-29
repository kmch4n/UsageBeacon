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
                type.ValueKind != JsonValueKind.String ||
                type.GetString() != "assistant")
                return null;
            if (!root.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.Object)
                return null;
            if (!message.TryGetProperty("usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object)
                return null;

            var model = message.TryGetProperty("model", out var modelProp) &&
                        modelProp.ValueKind == JsonValueKind.String
                ? modelProp.GetString()
                : null;
            if (string.IsNullOrEmpty(model) || model == "<synthetic>") return null;

            if (!root.TryGetProperty("timestamp", out var tsProp) ||
                tsProp.ValueKind != JsonValueKind.String ||
                !TryParseUtc(tsProp.GetString(), out var timestampUtc))
                return null;

            var messageId = message.TryGetProperty("id", out var idProp) &&
                            idProp.ValueKind == JsonValueKind.String
                ? idProp.GetString()
                : null;
            if (string.IsNullOrEmpty(messageId)) return null;
            var requestId = "";
            if (root.TryGetProperty("requestId", out var reqProp))
            {
                if (reqProp.ValueKind != JsonValueKind.String) return null;
                requestId = reqProp.GetString() ?? "";
            }

            if (!TryGetNonNegativeLong(usage, "input_tokens", out var input) ||
                !TryGetNonNegativeLong(usage, "cache_read_input_tokens", out var cacheRead) ||
                !TryGetNonNegativeLong(usage, "output_tokens", out var output))
                return null;

            long write5m, write1h;
            if (usage.TryGetProperty("cache_creation", out var creation) &&
                creation.ValueKind == JsonValueKind.Object)
            {
                if (!TryGetNonNegativeLong(creation, "ephemeral_5m_input_tokens", out write5m) ||
                    !TryGetNonNegativeLong(creation, "ephemeral_1h_input_tokens", out write1h))
                    return null;
            }
            else
            {
                if (usage.TryGetProperty("cache_creation", out _)) return null;

                // Older records only expose the combined counter; bill it at
                // the cheaper five-minute write rate.
                if (!TryGetNonNegativeLong(usage, "cache_creation_input_tokens", out write5m))
                    return null;
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

    internal static bool TryGetNonNegativeLong(
        JsonElement element,
        string property,
        out long result)
    {
        result = 0;
        if (!element.TryGetProperty(property, out var value)) return true;
        return value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt64(out result) &&
               result >= 0;
    }
}
