using UsageBeacon.Models.Insights;
using UsageBeacon.Services.Insights;
using UsageBeacon.ViewModels;

namespace UsageBeacon.Tests;

public sealed class DashboardViewModelTests
{
    private static readonly ModelPricingCatalog Pricing = new("2026-07-20",
        new Dictionary<string, ModelPricing>
        {
            ["claude-fable-5"] = new(10m, 1m, 12.5m, 20m, 50m),
            ["gpt-5.6-sol"] = new(5m, 0.5m, 0m, 0m, 30m),
        });

    [Fact]
    public async Task LoadAsync_AggregatesBothLogDirectories()
    {
        using var directory = new TempDirectory();
        var claudeDir = Directory.CreateDirectory(
            Path.Combine(directory.Path, "claude", "project-a")).Parent!.FullName;
        var codexDir = Directory.CreateDirectory(
            Path.Combine(directory.Path, "codex", "2026")).Parent!.FullName;
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        File.WriteAllText(
            Path.Combine(claudeDir, "project-a", "session.jsonl"),
            """
            {"type":"assistant","timestamp":"__TS__","requestId":"req_1","message":{"id":"msg_1","model":"claude-fable-5","usage":{"input_tokens":1000000,"cache_read_input_tokens":0,"output_tokens":0}}}
            """.Replace("__TS__", now) + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(codexDir, "2026", "rollout.jsonl"),
            """
            {"timestamp":"__TS__","type":"turn_context","payload":{"model":"gpt-5.6-sol"}}
            {"timestamp":"__TS__","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":0,"cached_input_tokens":0,"output_tokens":1000000,"total_tokens":1000000}}}}
            """.Replace("__TS__", now) + Environment.NewLine);

        var vm = new DashboardViewModel(
            Pricing,
            claudeProjectsDirectory: claudeDir,
            codexSessionsDirectory: codexDir,
            cachePath: Path.Combine(directory.Path, "cache.json"),
            timeZone: TimeZoneInfo.Utc);

        var data = await vm.LoadAsync(CancellationToken.None);

        Assert.True(vm.HasAnyLogDirectory);
        Assert.Equal(10m, data.Today.ClaudeCostUsd);
        Assert.Equal(30m, data.Today.CodexCostUsd);
        Assert.Equal(2, data.Models.Count);
        Assert.True(File.Exists(Path.Combine(directory.Path, "cache.json")));
    }

    [Fact]
    public async Task LoadAsync_ReparsesLegacyCodexCacheWithUnknownModel()
    {
        using var directory = new TempDirectory();
        var codexDir = Directory.CreateDirectory(Path.Combine(directory.Path, "codex")).FullName;
        var path = Path.Combine(codexDir, "rollout.jsonl");
        var nowUtc = DateTime.UtcNow;
        var now = nowUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        File.WriteAllText(
            path,
            """
            {"timestamp":"__TS__","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":0,"cached_input_tokens":0,"output_tokens":1000000,"total_tokens":1000000}}}}
            {"timestamp":"__TS__","type":"turn_context","payload":{"model":"gpt-5.6-sol"}}
            """.Replace("__TS__", now) + Environment.NewLine);

        var cachePath = Path.Combine(directory.Path, "cache.json");
        var info = new FileInfo(path);
        var legacyCache = UsageLogCache.Load(cachePath);
        legacyCache.GetEntries(
            path,
            info.Length,
            info.LastWriteTimeUtc,
            _ => new[]
            {
                new TokenUsageEntry(
                    1,
                    nowUtc,
                    UsageService.Codex,
                    "unknown",
                    0,
                    0,
                    0,
                    0,
                    1000000),
            });
        legacyCache.Save();

        var vm = new DashboardViewModel(
            Pricing,
            claudeProjectsDirectory: Path.Combine(directory.Path, "no-claude"),
            codexSessionsDirectory: codexDir,
            cachePath: cachePath,
            timeZone: TimeZoneInfo.Utc);

        var data = await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(30m, data.Today.CodexCostUsd);
        Assert.Equal("gpt-5.6-sol", Assert.Single(data.Models).Model);
    }

    [Fact]
    public async Task LoadAsync_SkipsLockedFiles_AndStillSavesTheCache()
    {
        using var directory = new TempDirectory();
        var claudeDir = Directory.CreateDirectory(Path.Combine(directory.Path, "claude")).FullName;
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var readable = Path.Combine(claudeDir, "readable.jsonl");
        File.WriteAllText(readable,
            """
            {"type":"assistant","timestamp":"__TS__","requestId":"req_1","message":{"id":"msg_1","model":"claude-fable-5","usage":{"input_tokens":1000000,"cache_read_input_tokens":0,"output_tokens":0}}}
            """.Replace("__TS__", now) + Environment.NewLine);
        var locked = Path.Combine(claudeDir, "locked.jsonl");
        File.WriteAllText(locked, "placeholder");
        var cachePath = Path.Combine(directory.Path, "cache.json");
        var vm = new DashboardViewModel(
            Pricing,
            claudeProjectsDirectory: claudeDir,
            codexSessionsDirectory: Path.Combine(directory.Path, "no-codex"),
            cachePath: cachePath,
            timeZone: TimeZoneInfo.Utc);

        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var data = await vm.LoadAsync(CancellationToken.None);

            Assert.Equal(10m, data.Today.ClaudeCostUsd);
            Assert.True(File.Exists(cachePath));
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmptyData_WhenDirectoriesAreMissing()
    {
        using var directory = new TempDirectory();
        var vm = new DashboardViewModel(
            Pricing,
            claudeProjectsDirectory: Path.Combine(directory.Path, "no-claude"),
            codexSessionsDirectory: Path.Combine(directory.Path, "no-codex"),
            cachePath: Path.Combine(directory.Path, "cache.json"),
            timeZone: TimeZoneInfo.Utc);

        var data = await vm.LoadAsync(CancellationToken.None);

        Assert.False(vm.HasAnyLogDirectory);
        Assert.Equal(0m, data.Last30Days.CostUsd);
        Assert.Empty(data.Models);
    }

    [Fact]
    public async Task LoadAsync_PrunesCachedEntriesOlderThanTheRetentionWindow()
    {
        using var directory = new TempDirectory();
        var claudeDir = Directory.CreateDirectory(Path.Combine(directory.Path, "claude")).FullName;
        var recent = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var ancient = DateTime.UtcNow.AddDays(-200).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        File.WriteAllText(
            Path.Combine(claudeDir, "recent.jsonl"),
            """
            {"type":"assistant","timestamp":"__TS__","requestId":"req_1","message":{"id":"msg_1","model":"claude-fable-5","usage":{"input_tokens":1000000,"cache_read_input_tokens":0,"output_tokens":0}}}
            """.Replace("__TS__", recent) + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(claudeDir, "ancient.jsonl"),
            """
            {"type":"assistant","timestamp":"__TS__","requestId":"req_2","message":{"id":"msg_2","model":"claude-fable-5","usage":{"input_tokens":1000000,"cache_read_input_tokens":0,"output_tokens":0}}}
            """.Replace("__TS__", ancient) + Environment.NewLine);

        var cachePath = Path.Combine(directory.Path, "cache.json");
        var vm = new DashboardViewModel(
            Pricing,
            claudeProjectsDirectory: claudeDir,
            codexSessionsDirectory: Path.Combine(directory.Path, "no-codex"),
            cachePath: cachePath,
            timeZone: TimeZoneInfo.Utc);

        var data = await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(10m, data.Today.ClaudeCostUsd);

        var cutoff = DateTime.UtcNow.AddDays(-UsageLogCache.RetentionDays);
        var cached = UsageLogCache.Load(cachePath)
            .AllEntries()
            .SelectMany(pair => pair.Value)
            .ToList();

        Assert.Single(cached);
        Assert.All(cached, entry => Assert.True(entry.TimestampUtc >= cutoff));
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
