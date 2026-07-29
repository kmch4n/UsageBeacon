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
    public const int ParserRevision = 2;

    public static IReadOnlyList<TokenUsageEntry> ParseFile(string path)
    {
        var entries = new List<TokenUsageEntry>();
        var leadingEntries = new List<TokenUsageEntry>();
        string? model = null;
        long prevInput = 0, prevCached = 0, prevOutput = 0;
        var fileKey = Path.GetFileNameWithoutExtension(path);
        var lineIndex = 0;

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
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
                    type.ValueKind == JsonValueKind.String &&
                    type.GetString() == "turn_context")
                {
                    var turnModel = payload.TryGetProperty("model", out var modelProp) &&
                                    modelProp.ValueKind == JsonValueKind.String
                        ? modelProp.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(turnModel))
                    {
                        model = turnModel;
                        if (leadingEntries.Count > 0)
                        {
                            // Codex can emit cumulative usage before its first
                            // turn_context. The first observed model is the
                            // best available attribution for those deltas.
                            entries.AddRange(leadingEntries.Select(
                                entry => entry with { Model = model }));
                            leadingEntries.Clear();
                        }
                    }
                    continue;
                }

                if (!payload.TryGetProperty("type", out var payloadType) ||
                    payloadType.ValueKind != JsonValueKind.String ||
                    payloadType.GetString() != "token_count")
                    continue;
                if (!payload.TryGetProperty("info", out var info) ||
                    info.ValueKind != JsonValueKind.Object ||
                    !info.TryGetProperty("total_token_usage", out var total) ||
                    total.ValueKind != JsonValueKind.Object)
                    continue;
                if (!root.TryGetProperty("timestamp", out var tsProp) ||
                    tsProp.ValueKind != JsonValueKind.String ||
                    !ClaudeTranscriptReader.TryParseUtc(tsProp.GetString(), out var timestampUtc))
                    continue;

                if (!ClaudeTranscriptReader.TryGetNonNegativeLong(total, "input_tokens", out var input) ||
                    !ClaudeTranscriptReader.TryGetNonNegativeLong(total, "cached_input_tokens", out var cached) ||
                    !ClaudeTranscriptReader.TryGetNonNegativeLong(total, "output_tokens", out var output) ||
                    cached > input)
                    continue;

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
                if (dCached > dInput) continue;

                prevInput = input;
                prevCached = cached;
                prevOutput = output;

                if (dInput == 0 && dOutput == 0) continue;

                var entry = new TokenUsageEntry(
                    IdHash: UsageLogHashing.Hash($"{fileKey}:{lineIndex}:{input}:{output}"),
                    TimestampUtc: timestampUtc,
                    Service: UsageService.Codex,
                    Model: model ?? "unknown",
                    InputTokens: Math.Max(0, dInput - dCached),
                    CachedInputTokens: dCached,
                    CacheWrite5mTokens: 0,
                    CacheWrite1hTokens: 0,
                    OutputTokens: dOutput);
                if (model == null)
                    leadingEntries.Add(entry);
                else
                    entries.Add(entry);
            }
            catch (JsonException)
            {
                // Malformed line: skip.
            }
        }

        // Preserve the existing fallback when a session never identifies a model.
        entries.AddRange(leadingEntries);
        return entries;
    }
}
