using System.Text.Json.Serialization;

namespace UsageBeacon.Models.Insights;

/// <summary>Service that produced a usage record.</summary>
public enum UsageService
{
    Claude,
    Codex,
}

/// <summary>
/// One billed usage event extracted from a local session log.
/// Token fields are normalized so that a single cost formula applies to both
/// vendors: <see cref="InputTokens"/> never includes <see cref="CachedInputTokens"/>,
/// and <see cref="OutputTokens"/> already contains any reasoning tokens.
/// </summary>
public sealed record TokenUsageEntry(
    [property: JsonPropertyName("id")] long IdHash,
    [property: JsonPropertyName("ts")] DateTime TimestampUtc,
    [property: JsonPropertyName("svc")] UsageService Service,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("in")] long InputTokens,
    [property: JsonPropertyName("cin")] long CachedInputTokens,
    [property: JsonPropertyName("w5m")] long CacheWrite5mTokens,
    [property: JsonPropertyName("w1h")] long CacheWrite1hTokens,
    [property: JsonPropertyName("out")] long OutputTokens);
