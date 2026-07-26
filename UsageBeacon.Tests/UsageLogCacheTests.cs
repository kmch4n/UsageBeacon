using UsageBeacon.Models.Insights;
using UsageBeacon.Services.Insights;

namespace UsageBeacon.Tests;

public sealed class UsageLogCacheTests
{
    private static TokenUsageEntry Entry(long id) => new(
        id, new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
        UsageService.Claude, "claude-fable-5", 100, 200, 30, 40, 50);

    [Fact]
    public void GetEntries_ReusesCachedResult_WhenFileIsUnchanged()
    {
        using var directory = new TempDirectory();
        var cache = UsageLogCache.Load(Path.Combine(directory.Path, "cache.json"));
        var mtime = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var parses = 0;
        IReadOnlyList<TokenUsageEntry> Parser(string _) { parses++; return new[] { Entry(1) }; }

        cache.GetEntries("log.jsonl", 100, mtime, Parser);
        var second = cache.GetEntries("log.jsonl", 100, mtime, Parser);

        Assert.Equal(1, parses);
        Assert.Single(second);
    }

    [Fact]
    public void GetEntries_Reparses_WhenLengthOrWriteTimeChanges()
    {
        using var directory = new TempDirectory();
        var cache = UsageLogCache.Load(Path.Combine(directory.Path, "cache.json"));
        var mtime = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var parses = 0;
        IReadOnlyList<TokenUsageEntry> Parser(string _) { parses++; return new[] { Entry(parses) }; }

        cache.GetEntries("log.jsonl", 100, mtime, Parser);
        cache.GetEntries("log.jsonl", 150, mtime, Parser);
        cache.GetEntries("log.jsonl", 150, mtime.AddMinutes(1), Parser);

        Assert.Equal(3, parses);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsEntries_AndRetainsDeletedFiles()
    {
        using var directory = new TempDirectory();
        var cachePath = Path.Combine(directory.Path, "cache.json");
        var cache = UsageLogCache.Load(cachePath);
        var mtime = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        cache.GetEntries("deleted-log.jsonl", 100, mtime, _ => new[] { Entry(7) });
        cache.Save();

        // "deleted-log.jsonl" never existed on disk; a reload must still
        // surface its entries because pruned transcripts are history.
        var reloaded = UsageLogCache.Load(cachePath);
        var all = reloaded.AllEntries().ToList();

        var pair = Assert.Single(all);
        Assert.Equal("deleted-log.jsonl", pair.Key);
        var entry = Assert.Single(pair.Value);
        Assert.Equal(Entry(7), entry);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFile_AndOverwritesPreviousCache()
    {
        using var directory = new TempDirectory();
        var cachePath = Path.Combine(directory.Path, "cache.json");
        var mtime = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);

        var first = UsageLogCache.Load(cachePath);
        first.GetEntries("a.jsonl", 1, mtime, _ => new[] { Entry(1) });
        first.Save();
        var second = UsageLogCache.Load(cachePath);
        second.GetEntries("b.jsonl", 2, mtime, _ => new[] { Entry(2) });
        second.Save();

        Assert.False(File.Exists(cachePath + ".tmp"));
        Assert.Equal(2, UsageLogCache.Load(cachePath).AllEntries().Count());
    }

    [Fact]
    public void Load_DiscardsCorruptCacheFile()
    {
        using var directory = new TempDirectory();
        var cachePath = Path.Combine(directory.Path, "cache.json");
        File.WriteAllText(cachePath, "{broken");

        var cache = UsageLogCache.Load(cachePath);

        Assert.Empty(cache.AllEntries());
    }

    [Fact]
    public void Load_DiscardsCacheWithDifferentSchemaVersion()
    {
        using var directory = new TempDirectory();
        var cachePath = Path.Combine(directory.Path, "cache.json");
        File.WriteAllText(cachePath, """{"schemaVersion":999,"files":{"x":{"len":1,"mtime":"2026-07-20T00:00:00Z","entries":[]}}}""");

        var cache = UsageLogCache.Load(cachePath);

        Assert.Empty(cache.AllEntries());
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
