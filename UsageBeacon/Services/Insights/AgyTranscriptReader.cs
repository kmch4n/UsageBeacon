using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using UsageBeacon.Models.Insights;

namespace UsageBeacon.Services.Insights;

/// <summary>
/// Extracts token usage from Antigravity CLI transcripts
/// (<c>~/.gemini/antigravity-cli/brain/&lt;conversation&gt;/.system_generated/logs/transcript.jsonl</c>).
///
/// Each <c>PLANNER_RESPONSE</c> line carries <c>input_tokens</c>,
/// <c>cache_read_tokens</c>, and <c>output_tokens</c>; the input count excludes
/// the cached count, matching the normalized entry shape. Only
/// <c>transcript.jsonl</c> is read: <c>transcript_full.jsonl</c> repeats the
/// same steps and would double count them.
///
/// Lines do not name their model. The model is taken from the
/// "changed setting `Model Selection` from X to Y" notice that agy places in
/// <c>USER_INPUT</c> content; notices quoted inside tool output are ignored.
/// Steps before the first notice use its "from" model, or the caller's
/// default (the current agy setting) when that is "None". The result is an
/// estimate, as everywhere else in the dashboard.
/// </summary>
public static partial class AgyTranscriptReader
{
    // Revision 1 was written by an unreleased build that stored display names
    // ("Gemini 3.1 Pro (High)") as models; local caches may still hold it.
    public const int ParserRevision = 2;
    public const string FileName = "transcript.jsonl";

    public static IReadOnlyList<TokenUsageEntry> ParseFile(string path)
        => ParseFile(path, defaultModel: null);

    public static IReadOnlyList<TokenUsageEntry> ParseFile(string path, string? defaultModel)
    {
        var entries = new List<TokenUsageEntry>();
        var fallback = NormalizeModel(defaultModel) ?? "unknown";
        var model = fallback;
        var sawModelNotice = false;
        var conversation = ConversationKey(path);

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var isResponse = line.Contains("\"PLANNER_RESPONSE\"", StringComparison.Ordinal);
            var isModelNotice = !isResponse &&
                line.Contains("\"USER_INPUT\"", StringComparison.Ordinal) &&
                line.Contains("Model Selection", StringComparison.Ordinal);
            if (!isResponse && !isModelNotice) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("type", out var type) ||
                    type.ValueKind != JsonValueKind.String)
                    continue;

                if (type.GetString() == "USER_INPUT")
                {
                    if (TryReadModelChange(root, out var from, out var to))
                    {
                        if (!sawModelNotice && from is not null)
                        {
                            // Earlier steps ran on the model this notice switched away from.
                            for (var i = 0; i < entries.Count; i++)
                                entries[i] = entries[i] with { Model = from };
                        }
                        sawModelNotice = true;
                        model = to;
                    }
                    continue;
                }

                if (type.GetString() != "PLANNER_RESPONSE" ||
                    !root.TryGetProperty("created_at", out var tsProp) ||
                    tsProp.ValueKind != JsonValueKind.String ||
                    !ClaudeTranscriptReader.TryParseUtc(tsProp.GetString(), out var timestampUtc) ||
                    !ClaudeTranscriptReader.TryGetNonNegativeLong(root, "input_tokens", out var input) ||
                    !ClaudeTranscriptReader.TryGetNonNegativeLong(root, "output_tokens", out var output))
                    continue;
                ClaudeTranscriptReader.TryGetNonNegativeLong(root, "cache_read_tokens", out var cached);
                if (input == 0 && cached == 0 && output == 0) continue;

                var step = root.TryGetProperty("step_index", out var stepProp) &&
                           stepProp.ValueKind == JsonValueKind.Number &&
                           stepProp.TryGetInt64(out var stepValue)
                    ? stepValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : tsProp.GetString();

                entries.Add(new TokenUsageEntry(
                    IdHash: UsageLogHashing.Hash($"agy:{conversation}:{step}"),
                    TimestampUtc: timestampUtc,
                    Service: UsageService.Agy,
                    Model: model,
                    InputTokens: input,
                    CachedInputTokens: cached,
                    CacheWrite5mTokens: 0,
                    CacheWrite1hTokens: 0,
                    OutputTokens: output));
            }
            catch (JsonException)
            {
                // Malformed line: skip.
            }
        }

        return entries;
    }

    /// <summary>
    /// Converts an agy display name such as "Gemini 3.1 Pro (High)" or
    /// "Claude Opus 5.5 (High)" into a pricing key ("gemini-3.1-pro",
    /// "claude-opus-5-5"). Claude ids use hyphens for version dots.
    /// </summary>
    public static string? NormalizeModel(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        var name = EffortSuffix().Replace(displayName.Trim(), "");
        if (name.Length == 0 || name.Equals("None", StringComparison.OrdinalIgnoreCase)) return null;

        var key = Whitespace().Replace(name.ToLowerInvariant(), "-");
        key = IdEffortSuffix().Replace(key, "");
        return key.StartsWith("claude-", StringComparison.Ordinal) ? key.Replace('.', '-') : key;
    }

    private static bool TryReadModelChange(JsonElement root, out string? from, out string to)
    {
        from = null;
        to = "";
        if (!root.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String)
            return false;

        var match = ModelChange().Match(content.GetString() ?? "");
        if (!match.Success || NormalizeModel(match.Groups["to"].Value) is not { } target)
            return false;

        from = NormalizeModel(match.Groups["from"].Value);
        to = target;
        return true;
    }

    // brain/<conversation>/.system_generated/logs/transcript.jsonl
    private static string ConversationKey(string path)
        => Directory.GetParent(path)?.Parent?.Parent?.Name is { Length: > 0 } name
            ? name
            : path;

    [GeneratedRegex(@"changed setting `Model Selection` from (?<from>[^\r\n<]+?) to (?<to>[^\r\n<]+?)\.(?:\s|$)")]
    private static partial Regex ModelChange();

    [GeneratedRegex(@"\s*\((?:low|medium|high|xhigh|max|thinking)\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EffortSuffix();

    [GeneratedRegex(@"-(?:low|medium|high|xhigh|max)$")]
    private static partial Regex IdEffortSuffix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
