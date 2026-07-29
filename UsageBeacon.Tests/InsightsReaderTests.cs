using System.Text;
using UsageBeacon.Models.Insights;
using UsageBeacon.Services.Insights;

namespace UsageBeacon.Tests;

public sealed class InsightsReaderTests
{
    private const string ClaudeLine =
        """
        {"type":"assistant","timestamp":"2026-07-20T03:00:00.000Z","requestId":"req_1","message":{"id":"msg_1","model":"claude-fable-5","usage":{"input_tokens":10,"cache_creation_input_tokens":100,"cache_read_input_tokens":1000,"cache_creation":{"ephemeral_5m_input_tokens":60,"ephemeral_1h_input_tokens":40},"output_tokens":5}}}
        """;

    [Fact]
    public void ClaudeParseLine_ExtractsNormalizedUsage()
    {
        var entry = ClaudeTranscriptReader.ParseLine(ClaudeLine);

        Assert.NotNull(entry);
        Assert.Equal(UsageService.Claude, entry.Service);
        Assert.Equal("claude-fable-5", entry.Model);
        Assert.Equal(new DateTime(2026, 7, 20, 3, 0, 0, DateTimeKind.Utc), entry.TimestampUtc);
        Assert.Equal(10, entry.InputTokens);
        Assert.Equal(1000, entry.CachedInputTokens);
        Assert.Equal(60, entry.CacheWrite5mTokens);
        Assert.Equal(40, entry.CacheWrite1hTokens);
        Assert.Equal(5, entry.OutputTokens);
    }

    [Fact]
    public void ClaudeParseLine_FallsBackToCombinedCacheCreation_WhenSplitIsMissing()
    {
        var line = ClaudeLine.Replace(
            ""","cache_creation":{"ephemeral_5m_input_tokens":60,"ephemeral_1h_input_tokens":40}""",
            "");

        var entry = ClaudeTranscriptReader.ParseLine(line);

        Assert.NotNull(entry);
        Assert.Equal(100, entry.CacheWrite5mTokens);
        Assert.Equal(0, entry.CacheWrite1hTokens);
    }

    [Theory]
    [InlineData("""{"type":"user","timestamp":"2026-07-20T03:00:00Z"}""")]
    [InlineData("""{"type":"assistant","timestamp":"2026-07-20T03:00:00Z","message":{"id":"m","model":"<synthetic>","usage":{"input_tokens":1}}}""")]
    [InlineData("""{"type":"assistant","timestamp":"not a date","message":{"id":"m","model":"claude-fable-5","usage":{"input_tokens":1}}}""")]
    [InlineData("""{"type":"assistant","timestamp":"2026-07-20T03:00:00Z","message":{"id":1,"model":"claude-fable-5","usage":{"input_tokens":1}}}""")]
    [InlineData("""{"type":"assistant","timestamp":"2026-07-20T03:00:00Z","message":{"id":"m","model":5,"usage":{"input_tokens":1}}}""")]
    [InlineData("""{"type":"assistant","timestamp":"2026-07-20T03:00:00Z","requestId":5,"message":{"id":"m","model":"claude-fable-5","usage":{"input_tokens":1}}}""")]
    [InlineData("""{"type":"assistant","timestamp":"2026-07-20T03:00:00Z","message":{"id":"m","model":"claude-fable-5","usage":{"input_tokens":1.5}}}""")]
    [InlineData("""{"type":"assistant","timestamp":"2026-07-20T03:00:00Z","message":{"id":"m","model":"claude-fable-5","usage":{"input_tokens":-1}}}""")]
    [InlineData("""{"type":"assistant","timestamp":"2026-07-20T03:00:00Z","message":{"id":"m","model":"claude-fable-5","usage":{"input_tokens":9223372036854775808}}}""")]
    [InlineData("not json at all")]
    [InlineData("""["assistant","usage"]""")]
    public void ClaudeParseLine_SkipsNonBillableOrMalformedLines(string line)
        => Assert.Null(ClaudeTranscriptReader.ParseLine(line));

    [Fact]
    public void ClaudeParseFile_DeduplicatesRepeatedMessageEmissions()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "session.jsonl");
        File.WriteAllLines(path, new[] { ClaudeLine, "{\"type\":\"summary\"}", ClaudeLine });

        var entries = ClaudeTranscriptReader.ParseFile(path);

        Assert.Single(entries);
    }

    [Fact]
    public void CodexParseFile_UsesCumulativeDeltas_NotLastTokenUsage()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "rollout.jsonl");
        File.WriteAllLines(path, new[]
        {
            """{"timestamp":"2026-07-20T03:00:00Z","type":"turn_context","payload":{"model":"gpt-5.6-sol"}}""",
            CodexTokenCount("2026-07-20T03:00:05Z", 1000, 400, 50),
            CodexTokenCount("2026-07-20T03:00:10Z", 1500, 600, 80),
        });

        var entries = CodexSessionReader.ParseFile(path);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.Equal(UsageService.Codex, entry.Service);
            Assert.Equal("gpt-5.6-sol", entry.Model);
        });
        // input_tokens includes cached_input_tokens, so the entry stores the split.
        Assert.Equal(600, entries[0].InputTokens);
        Assert.Equal(400, entries[0].CachedInputTokens);
        Assert.Equal(50, entries[0].OutputTokens);
        Assert.Equal(300, entries[1].InputTokens);
        Assert.Equal(200, entries[1].CachedInputTokens);
        Assert.Equal(30, entries[1].OutputTokens);
    }

    [Fact]
    public void CodexParseFile_TreatsNegativeDeltaAsBaselineReset()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "rollout.jsonl");
        File.WriteAllLines(path, new[]
        {
            """{"timestamp":"2026-07-20T03:00:00Z","type":"turn_context","payload":{"model":"gpt-5.6-terra"}}""",
            CodexTokenCount("2026-07-20T03:00:05Z", 1000, 0, 50),
            CodexTokenCount("2026-07-20T03:00:10Z", 200, 0, 10),
        });

        var entries = CodexSessionReader.ParseFile(path);

        Assert.Equal(2, entries.Count);
        Assert.Equal(200, entries[1].InputTokens);
        Assert.Equal(10, entries[1].OutputTokens);
    }

    [Fact]
    public void CodexParseFile_UsesUnknownModel_BeforeAnyTurnContext()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "rollout.jsonl");
        File.WriteAllLines(path, new[] { CodexTokenCount("2026-07-20T03:00:05Z", 100, 0, 5) });

        var entries = CodexSessionReader.ParseFile(path);

        Assert.Single(entries);
        Assert.Equal("unknown", entries[0].Model);
    }

    [Fact]
    public void CodexParseFile_BackfillsLeadingUsageWithFirstObservedModel()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "rollout.jsonl");
        File.WriteAllLines(path, new[]
        {
            CodexTokenCount("2026-07-20T03:00:05Z", 100, 20, 5),
            """{"timestamp":"2026-07-20T03:00:06Z","type":"turn_context","payload":{"model":"   "}}""",
            CodexTokenCount("2026-07-20T03:00:10Z", 150, 30, 8),
            """{"timestamp":"2026-07-20T03:00:11Z","type":"turn_context","payload":{"model":"gpt-5.6-sol"}}""",
            CodexTokenCount("2026-07-20T03:00:15Z", 200, 40, 10),
            """{"timestamp":"2026-07-20T03:00:16Z","type":"turn_context","payload":{"model":"gpt-5.6-terra"}}""",
            CodexTokenCount("2026-07-20T03:00:20Z", 300, 60, 20),
        });

        var entries = CodexSessionReader.ParseFile(path);

        Assert.Equal(4, entries.Count);
        Assert.Equal(
            new[] { "gpt-5.6-sol", "gpt-5.6-sol", "gpt-5.6-sol", "gpt-5.6-terra" },
            entries.Select(entry => entry.Model));
        Assert.Equal(80, entries[0].InputTokens);
        Assert.Equal(40, entries[1].InputTokens);
        Assert.Equal(40, entries[2].InputTokens);
        Assert.Equal(80, entries[3].InputTokens);
    }

    [Fact]
    public void CodexParseFile_RecoversCompletedTrailingLineThroughCache()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "rollout.jsonl");
        var first = CodexTokenCount("2026-07-20T03:00:05Z", 100, 20, 5);
        var second = CodexTokenCount("2026-07-20T03:00:10Z", 200, 40, 10);
        var split = second.Length / 2;
        File.WriteAllText(
            path,
            """
            {"timestamp":"2026-07-20T03:00:00Z","type":"turn_context","payload":{"model":"gpt-5.6-sol"}}
            """
            + Environment.NewLine
            + first
            + Environment.NewLine);

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(0, SeekOrigin.End);
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true);
        writer.Write(second[..split]);
        writer.Flush();

        var cache = UsageLogCache.Load(Path.Combine(directory.Path, "cache.json"));
        var parses = 0;
        IReadOnlyList<TokenUsageEntry> Parser(string filePath)
        {
            parses++;
            return CodexSessionReader.ParseFile(filePath);
        }

        var info = new FileInfo(path);
        var incomplete = cache.GetEntries(
            path,
            info.Length,
            info.LastWriteTimeUtc,
            Parser,
            parserRevision: CodexSessionReader.ParserRevision);

        writer.Write(second[split..]);
        writer.Flush();
        info.Refresh();
        var completed = cache.GetEntries(
            path,
            info.Length,
            info.LastWriteTimeUtc,
            Parser,
            parserRevision: CodexSessionReader.ParserRevision);
        var cached = cache.GetEntries(
            path,
            info.Length,
            info.LastWriteTimeUtc,
            Parser,
            parserRevision: CodexSessionReader.ParserRevision);

        Assert.Single(incomplete);
        Assert.Equal(2, completed.Count);
        Assert.Equal(completed, cached);
        Assert.Equal(2, parses);
    }

    [Fact]
    public void CodexParseFile_SkipsInvalidCountersWithoutAdvancingTheBaseline()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "rollout.jsonl");
        File.WriteAllLines(path, new[]
        {
            CodexTokenCount("2026-07-20T03:00:05Z", 100, 20, 5),
            CodexTokenCountRaw("2026-07-20T03:00:10Z", "150.5", "30", "8"),
            CodexTokenCountRaw("2026-07-20T03:00:15Z", "180", "200", "10"),
            CodexTokenCount("2026-07-20T03:00:20Z", 200, 40, 15),
        });

        var entries = CodexSessionReader.ParseFile(path);

        Assert.Equal(2, entries.Count);
        Assert.Equal(80, entries[0].InputTokens);
        Assert.Equal(80, entries[1].InputTokens);
        Assert.Equal(20, entries[1].CachedInputTokens);
        Assert.Equal(10, entries[1].OutputTokens);
    }

    [Theory]
    [InlineData("-1", "0", "1")]
    [InlineData("1", "-1", "1")]
    [InlineData("1", "0", "1.5")]
    [InlineData("\"100\"", "0", "1")]
    [InlineData("9223372036854775808", "0", "1")]
    public void CodexParseFile_SkipsInvalidNumericValues(
        string input,
        string cached,
        string output)
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "rollout.jsonl");
        File.WriteAllText(path, CodexTokenCountRaw(
            "2026-07-20T03:00:05Z",
            input,
            cached,
            output));

        Assert.Empty(CodexSessionReader.ParseFile(path));
    }

    private static string CodexTokenCount(string timestamp, long input, long cached, long output)
        => """
           {"timestamp":"__TS__","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":__IN__,"cached_input_tokens":__CACHED__,"output_tokens":__OUT__,"reasoning_output_tokens":0,"total_tokens":__TOTAL__}}}}
           """
            .Replace("__TS__", timestamp)
            .Replace("__IN__", input.ToString())
            .Replace("__CACHED__", cached.ToString())
            .Replace("__OUT__", output.ToString())
            .Replace("__TOTAL__", (input + output).ToString());

    private static string CodexTokenCountRaw(
        string timestamp,
        string input,
        string cached,
        string output)
        => """
           {"timestamp":"__TS__","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":__IN__,"cached_input_tokens":__CACHED__,"output_tokens":__OUT__}}}}
           """
            .Replace("__TS__", timestamp)
            .Replace("__IN__", input)
            .Replace("__CACHED__", cached)
            .Replace("__OUT__", output);

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
