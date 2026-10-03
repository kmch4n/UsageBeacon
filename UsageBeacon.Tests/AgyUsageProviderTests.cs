using UsageBeacon.Models;
using UsageBeacon.Providers;

namespace UsageBeacon.Tests;

public sealed class AgyUsageProviderTests
{
    // Shape captured from `agy -p /usage --output-format json` (agy 1.2.16).
    private const string UsageReport = """
        {"conversation_id":"","status":"SUCCESS","response":"Gemini Models\tWeekly Limit Remaining\t83%\n","duration_seconds":0,"num_turns":0,
         "usage":{"input_tokens":0,"output_tokens":0,"thinking_tokens":0,"cache_read_tokens":0,"total_tokens":0},
         "command":{"name":"usage","data":{"description":"Within each group, models share a weekly limit and a 5-hour limit.",
          "groups":[
           {"name":"Gemini Models","description":"Models within this group: Gemini Flash, Gemini Pro","buckets":[
             {"id":"gemini-weekly","name":"Weekly Limit Remaining","window":"weekly","remaining_fraction":0.75,"reset_time":"2026-10-10T03:03:43Z"},
             {"id":"gemini-5h","name":"Five Hour Limit Remaining","window":"5h","remaining_fraction":0,"reset_time":"2026-10-03T08:03:43Z"}]},
           {"name":"Claude and GPT models","description":"Models within this group: Claude Opus, Claude Sonnet, GPT-OSS","buckets":[
             {"id":"3p-weekly","name":"Weekly Limit Remaining","window":"weekly","remaining_fraction":0.5,"reset_time":"2026-10-10T06:42:27Z"},
             {"id":"3p-5h","name":"Five Hour Limit Remaining","window":"5h","remaining_fraction":0.5,"reset_time":"2026-10-03T11:42:27Z"}]}]}}}
        """;

    [Fact]
    public void Parse_ReturnsGeminiGroupQuota_AndIgnoresThirdPartyModels()
    {
        var usage = AgyUsageParser.Parse(UsageReport);

        Assert.Equal(1.0, usage.FiveHour?.Utilization);
        Assert.Equal(new DateTime(2026, 10, 3, 8, 3, 43, DateTimeKind.Utc), usage.FiveHour?.ResetsAt);
        Assert.Equal(DateTimeKind.Utc, usage.FiveHour?.ResetsAt.Kind);
        Assert.Equal(0.25, usage.Weekly?.Utilization);
        Assert.Null(usage.WeeklySonnet);
    }

    [Fact]
    public void Parse_IdentifiesGeminiGroupByBucketId_WhenGroupNameChanges()
    {
        const string report = """
            {"status":"SUCCESS","command":{"data":{"groups":[
              {"name":"Third-party","buckets":[{"id":"3p-5h","window":"5h","remaining_fraction":0.1,"reset_time":"2026-10-03T08:00:00Z"}]},
              {"name":"Google","buckets":[{"id":"gemini-5h","window":"5h","remaining_fraction":0.6,"reset_time":"2026-10-03T08:00:00Z"}]}]}}}
            """;

        var usage = AgyUsageParser.Parse(report);

        Assert.Equal(0.4, usage.FiveHour!.Utilization, precision: 6);
        Assert.Null(usage.Weekly);
    }

    [Fact]
    public void Parse_SkipsDisabledBuckets()
    {
        const string report = """
            {"status":"SUCCESS","command":{"data":{"groups":[
              {"name":"Gemini Models","buckets":[
                {"id":"gemini-weekly","window":"weekly","remaining_fraction":0.9,"reset_time":"2026-10-10T00:00:00Z"},
                {"id":"gemini-5h","window":"5h","reset_time":"2026-10-03T08:00:00Z"}]}]}}}
            """;

        var usage = AgyUsageParser.Parse(report);

        Assert.Null(usage.FiveHour);
        Assert.Equal(0.1, usage.Weekly!.Utilization, precision: 6);
    }

    [Fact]
    public void Parse_ToleratesDiagnosticLinesBeforeJson()
    {
        var output = "Checking for updates...\n" + UsageReport.ReplaceLineEndings("");

        Assert.Equal(1.0, AgyUsageParser.Parse(output).FiveHour?.Utilization);
    }

    [Fact]
    public void Parse_ThrowsCommandFailed_WhenStatusIsNotSuccess()
    {
        var error = Assert.Throws<DomainError>(() =>
            AgyUsageParser.Parse("""{"status":"ERROR","response":"Not signed in"}"""));

        Assert.Equal(DomainErrorKind.AgyCommandFailed, error.Kind);
    }

    [Fact]
    public void Parse_ThrowsDecoding_WhenGeminiGroupIsMissing()
    {
        const string report = """
            {"status":"SUCCESS","command":{"data":{"groups":[
              {"name":"Claude and GPT models","buckets":[{"id":"3p-5h","window":"5h","remaining_fraction":0.5}]}]}}}
            """;

        var error = Assert.Throws<DomainError>(() => AgyUsageParser.Parse(report));

        Assert.Equal(DomainErrorKind.Decoding, error.Kind);
    }

    [Fact]
    public void Parse_ThrowsDecoding_WhenReplyIsAModelAnswer()
    {
        // agy releases without the non-interactive report answer with a model response instead.
        const string reply = """{"conversation_id":"abc","status":"SUCCESS","response":"There is no /usage command.","num_turns":1}""";

        var error = Assert.Throws<DomainError>(() => AgyUsageParser.Parse(reply));

        Assert.Equal(DomainErrorKind.Decoding, error.Kind);
    }

    [Theory]
    [InlineData("1.2.16", "1.2.16")]
    [InlineData("agy version 1.1.11\n", "1.1.11")]
    [InlineData("not a version", null)]
    public void ParseVersion_ExtractsDottedVersion(string output, string? expected)
        => Assert.Equal(expected, AgyUsageProvider.ParseVersion(output)?.ToString());

    [Fact]
    public async Task FetchAsync_ThrowsNotFound_WhenExecutableIsMissing()
    {
        var runner = new FakeRunner("1.2.16", UsageReport);
        var provider = new AgyUsageProvider(() => null, runner.RunAsync);

        var error = await Assert.ThrowsAsync<DomainError>(() => provider.FetchAsync());

        Assert.Equal(DomainErrorKind.AgyNotFound, error.Kind);
        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData("1.1.10")]
    [InlineData("unknown")]
    public async Task FetchAsync_NeverRunsUsagePrompt_WhenVersionCannotAnswerItWithoutAModel(string version)
    {
        var runner = new FakeRunner(version, UsageReport);
        var provider = new AgyUsageProvider(() => "agy.exe", runner.RunAsync);

        var error = await Assert.ThrowsAsync<DomainError>(() => provider.FetchAsync());

        Assert.Equal(DomainErrorKind.AgyUnsupportedVersion, error.Kind);
        Assert.Equal(["--version"], runner.Calls.Select(call => call[0]));
    }

    [Fact]
    public async Task FetchAsync_ReturnsGeminiQuota_AndChecksVersionOnlyOnce()
    {
        var runner = new FakeRunner("1.1.11", UsageReport);
        var provider = new AgyUsageProvider(() => "agy.exe", runner.RunAsync);

        var first = await provider.FetchAsync();
        await provider.FetchAsync();

        Assert.Equal(1.0, first.FiveHour?.Utilization);
        Assert.Equal(
            ["--version", "-p /usage --output-format json", "-p /usage --output-format json"],
            runner.Calls.Select(call => string.Join(' ', call)));
    }

    [Fact]
    public async Task FetchAsync_ThrowsCommandFailed_WhenUsageReportExitsWithError()
    {
        var runner = new FakeRunner("1.2.16", "", usageExitCode: 1);
        var provider = new AgyUsageProvider(() => "agy.exe", runner.RunAsync);

        var error = await Assert.ThrowsAsync<DomainError>(() => provider.FetchAsync());

        Assert.Equal(DomainErrorKind.AgyCommandFailed, error.Kind);
    }

    private sealed class FakeRunner(string version, string usage, int usageExitCode = 0)
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<AgyCommandResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken ct)
        {
            Calls.Add(arguments);
            return Task.FromResult(arguments[0] == "--version"
                ? new AgyCommandResult(0, version)
                : new AgyCommandResult(usageExitCode, usage));
        }
    }
}
