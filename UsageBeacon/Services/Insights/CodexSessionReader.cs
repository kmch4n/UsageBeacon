using System.IO;
using System.Text.Json;
using UsageBeacon.Models.Insights;

namespace UsageBeacon.Services.Insights;

/// <summary>
/// Extracts billed token usage from Codex rollout files
/// (<c>~/.codex/sessions/**/*.jsonl</c>).
///
/// Usage is derived from the deltas of consecutive cumulative
/// <c>total_token_usage</c> values: summing <c>last_token_usage</c> instead
/// over-counts (events exist where <c>last</c> is non-zero while the
/// cumulative total does not advance). A negative delta means the cumulative
/// baseline was reset, in which case the current value is taken as the delta.
///
/// Vendor semantics normalized here: <c>input_tokens</c> includes
/// <c>cached_input_tokens</c>, and <c>output_tokens</c> already includes
/// reasoning tokens, so the entry stores (input - cached, cached, output).
/// </summary>
public static class CodexSessionReader
{
    public static IReadOnlyList<TokenUsageEntry> ParseFile(string path)
    {
        var entries = new List<TokenUsageEntry>();
        var model = "unknown";
        long prevInput = 0, prevCached = 0, prevOutput = 0;
        var fileKey = Path.GetFileNameWithoutExtension(path);
        var lineIndex = 0;

        foreach (var line in File.ReadLines(path))
        {
            lineIndex++;
            var hasTurnContext = line.Contains("\"turn_context\"", StringComparison.Ordinal);
            var hasTokenCount = line.Contains("\"token_count\"", StringComparison.Ordinal);
            if (!hasTurnContext && !hasTokenCount) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("payload", out var payload) ||
                    payload.ValueKind != JsonValueKind.Object)
                    continue;

                if (root.TryGetProperty("type", out var type) &&
                    type.GetString() == "turn_context")
                {
                    var turnModel = payload.TryGetProperty("model", out var modelProp)
                        ? modelProp.GetString()
                        : null;
                    if (!string.IsNullOrEmpty(turnModel)) model = turnModel;
                    continue;
                }

                if (!payload.TryGetProperty("type", out var payloadType) ||
                    payloadType.GetString() != "token_count")
                    continue;
                if (!payload.TryGetProperty("info", out var info) ||
                    info.ValueKind != JsonValueKind.Object ||
                    !info.TryGetProperty("total_token_usage", out var total) ||
                    total.ValueKind != JsonValueKind.Object)
                    continue;
                if (!root.TryGetProperty("timestamp", out var tsProp) ||
                    !ClaudeTranscriptReader.TryParseUtc(tsProp.GetString(), out var timestampUtc))
                    continue;

                var input = ClaudeTranscriptReader.GetLong(total, "input_tokens");
                var cached = ClaudeTranscriptReader.GetLong(total, "cached_input_tokens");
                var output = ClaudeTranscriptReader.GetLong(total, "output_tokens");

                long dInput = input - prevInput;
                long dCached = cached - prevCached;
                long dOutput = output - prevOutput;
                if (dInput < 0 || dCached < 0 || dOutput < 0)
                {
                    // Cumulative baseline reset: the current totals are the delta.
                    dInput = input;
                    dCached = cached;
                    dOutput = output;
                }
                prevInput = input;
                prevCached = cached;
                prevOutput = output;

                if (dInput == 0 && dOutput == 0) continue;

                entries.Add(new TokenUsageEntry(
                    IdHash: UsageLogHashing.Hash($"{fileKey}:{lineIndex}:{input}:{output}"),
                    TimestampUtc: timestampUtc,
                    Service: UsageService.Codex,
                    Model: model,
                    InputTokens: Math.Max(0, dInput - dCached),
                    CachedInputTokens: dCached,
                    CacheWrite5mTokens: 0,
                    CacheWrite1hTokens: 0,
                    OutputTokens: dOutput));
            }
            catch (JsonException)
            {
                // Malformed line: skip.
            }
        }
        return entries;
    }
}
