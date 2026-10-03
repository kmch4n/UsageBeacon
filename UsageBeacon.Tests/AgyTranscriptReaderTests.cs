using UsageBeacon.Models.Insights;
using UsageBeacon.Services.Insights;

namespace UsageBeacon.Tests;

public sealed class AgyTranscriptReaderTests
{
    [Fact]
    public void ParseFile_ReadsPlannerResponses_WithSeparateCachedInput()
    {
        using var directory = new TempDirectory();
        var path = WriteTranscript(directory.Path, "conv-a",
            Notice(0, "None", "Gemini 3.1 Pro (High)"),
            Response(1, "2026-10-03T03:04:52Z", input: 5277, cached: 8093, output: 534),
            """{"step_index":2,"source":"MODEL","type":"GENERIC","created_at":"2026-10-03T03:04:53Z","content":"tool output"}""");

        var entry = Assert.Single(AgyTranscriptReader.ParseFile(path, "Claude Opus 5.5 (High)"));

        Assert.Equal(UsageService.Agy, entry.Service);
        Assert.Equal("gemini-3.1-pro", entry.Model);
        Assert.Equal(new DateTime(2026, 10, 3, 3, 4, 52, DateTimeKind.Utc), entry.TimestampUtc);
        Assert.Equal(5277, entry.InputTokens);
        Assert.Equal(8093, entry.CachedInputTokens);
        Assert.Equal(534, entry.OutputTokens);
        Assert.Equal(0, entry.CacheWrite5mTokens + entry.CacheWrite1hTokens);
    }

    [Fact]
    public void ParseFile_FollowsModelSwitches_AndAttributesEarlierStepsToTheOriginalModel()
    {
        using var directory = new TempDirectory();
        var path = WriteTranscript(directory.Path, "conv-b",
            Response(1, "2026-10-03T03:00:00Z", input: 10, cached: 0, output: 1),
            Notice(2, "Gemini 3.1 Pro (High)", "Claude Opus 5.5 (High)"),
            Response(3, "2026-10-03T03:01:00Z", input: 20, cached: 0, output: 2));

        var entries = AgyTranscriptReader.ParseFile(path, defaultModel: null);

        Assert.Equal(["gemini-3.1-pro", "claude-opus-5-5"], entries.Select(entry => entry.Model));
    }

    [Fact]
    public void ParseFile_UsesConfiguredModel_WhenTheConversationHasNoNotice()
    {
        using var directory = new TempDirectory();
        var path = WriteTranscript(directory.Path, "conv-c",
            Response(1, "2026-10-03T03:00:00Z", input: 10, cached: 0, output: 1));

        Assert.Equal("gemini-3.8-flash",
            Assert.Single(AgyTranscriptReader.ParseFile(path, "Gemini 3.8 Flash (Medium)")).Model);
        Assert.Equal("unknown",
            Assert.Single(AgyTranscriptReader.ParseFile(path, defaultModel: null)).Model);
    }

    [Fact]
    public void ParseFile_IgnoresModelNoticesQuotedInToolOutput()
    {
        using var directory = new TempDirectory();
        var quoted = """{"step_index":2,"source":"MODEL","type":"GENERIC","created_at":"2026-10-03T03:00:30Z","content":"The user changed setting `Model Selection` from None to Claude Opus 5.5 (High). and USER_INPUT"}""";
        var path = WriteTranscript(directory.Path, "conv-d",
            Notice(0, "None", "Gemini 3.1 Pro (High)"),
            quoted,
            Response(3, "2026-10-03T03:01:00Z", input: 10, cached: 0, output: 1));

        Assert.Equal("gemini-3.1-pro", Assert.Single(AgyTranscriptReader.ParseFile(path, null)).Model);
    }

    [Fact]
    public void ParseFile_GivesStableIdsPerConversationStep()
    {
        using var directory = new TempDirectory();
        var first = WriteTranscript(directory.Path, "conv-e",
            Response(1, "2026-10-03T03:00:00Z", input: 10, cached: 0, output: 1));
        var other = WriteTranscript(directory.Path, "conv-f",
            Response(1, "2026-10-03T03:00:00Z", input: 10, cached: 0, output: 1));

        var a = Assert.Single(AgyTranscriptReader.ParseFile(first, null)).IdHash;
        var again = Assert.Single(AgyTranscriptReader.ParseFile(first, null)).IdHash;
        var b = Assert.Single(AgyTranscriptReader.ParseFile(other, null)).IdHash;

        Assert.Equal(a, again);
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("Gemini 3.1 Pro (High)", "gemini-3.1-pro")]
    [InlineData("Gemini 3.8 Flash (Low)", "gemini-3.8-flash")]
    [InlineData("Claude Opus 5.5 (High)", "claude-opus-5-5")]
    [InlineData("Claude Sonnet 5.5 (Medium)", "claude-sonnet-5-5")]
    [InlineData("GPT-OSS 120B (Medium)", "gpt-oss-120b")]
    [InlineData("gemini-3.1-pro-high", "gemini-3.1-pro")]
    [InlineData("None", null)]
    [InlineData("  ", null)]
    public void NormalizeModel_MapsDisplayNamesToPricingKeys(string displayName, string? expected)
        => Assert.Equal(expected, AgyTranscriptReader.NormalizeModel(displayName));

    private static string Notice(int step, string from, string to)
        => $$"""{"step_index":{{step}},"source":"USER_EXPLICIT","type":"USER_INPUT","created_at":"2026-10-03T02:59:00Z","content":"<USER_SETTINGS_CHANGE>\nThe user changed setting `Model Selection` from {{from}} to {{to}}. No need to comment on this change if the user doesn't ask about it.\n</USER_SETTINGS_CHANGE>\nhello"}""";

    private static string Response(int step, string timestamp, long input, long cached, long output)
        => $$"""{"step_index":{{step}},"source":"MODEL","type":"PLANNER_RESPONSE","status":"DONE","created_at":"{{timestamp}}","input_tokens":{{input}},"cache_read_tokens":{{cached}},"output_tokens":{{output}},"content":"ok"}""";

    private static string WriteTranscript(string root, string conversation, params string[] lines)
    {
        var directory = Directory.CreateDirectory(Path.Combine(
            root, conversation, ".system_generated", "logs"));
        var path = Path.Combine(directory.FullName, AgyTranscriptReader.FileName);
        File.WriteAllLines(path, lines);
        return path;
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory =
            Directory.CreateTempSubdirectory("UsageBeaconTests-");

        public string Path => _directory.FullName;

        public void Dispose()
        {
            try { _directory.Delete(recursive: true); } catch { }
        }
    }
}
