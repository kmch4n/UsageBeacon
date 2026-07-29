using UsageBeacon.Models.Insights;
using UsageBeacon.Services.Insights;

namespace UsageBeacon.Tests;

public sealed class UsageLogCacheTests
{
    private static readonly DateTime Noon =
        new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    private static TokenUsageEntry Entry(long id) => new(
        id, new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
        UsageService.Claude, "claude-fable-5", 100, 200, 30, 40, 50);

    private static TokenUsageEntry Entry(long id, DateTime timestampUtc) => new(
        id, timestampUtc,
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
    public void GetEntries_ReparsesOnce_WhenParserRevisionChanges()
    {
        using var directory = new TempDirectory();
        var cache = UsageLogCache.Load(Path.Combine(directory.Path, "cache.json"));
        var mtime = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var parses = 0;
        IReadOnlyList<TokenUsageEntry> Parser(string _)
        {
            parses++;
            return new[] { Entry(parses) };
        }

        cache.GetEntries("log.jsonl", 100, mtime, Parser);
        cache.GetEntries("log.jsonl", 100, mtime, Parser, parserRevision: 1);
        var current = cache.GetEntries(
            "log.jsonl",
            100,
            mtime,
            Parser,
            parserRevision: 1);

        Assert.Equal(2, parses);
        Assert.Equal(Entry(2), Assert.Single(current));
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

    [Fact]
    public void Load_AcceptsLegacyCacheWithoutParserRevision()
    {
        using var directory = new TempDirectory();
        var cachePath = Path.Combine(directory.Path, "cache.json");
        var mtime = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var cache = UsageLogCache.Load(cachePath);
        cache.GetEntries("log.jsonl", 100, mtime, _ => new[] { Entry(7) });
        cache.Save();
        var legacyJson = File.ReadAllText(cachePath)
            .Replace(",\"parserRevision\":0", "");
        var archivedStart = legacyJson.LastIndexOf(
            ",\"archived\":",
            StringComparison.Ordinal);
        Assert.True(archivedStart > 0);
        legacyJson = legacyJson[..archivedStart] + "}";
        File.WriteAllText(cachePath, legacyJson);
        var parses = 0;

        var reloaded = UsageLogCache.Load(cachePath);
        var entries = reloaded.GetEntries(
            "log.jsonl",
            100,
            mtime,
            _ =>
            {
                parses++;
                return new[] { Entry(8) };
            });

        Assert.Equal(0, parses);
        Assert.Equal(Entry(7), Assert.Single(entries));
    }

    [Fact]
    public void Load_MigratesV1ArchiveByRecoveringExactEntries()
    {
        using var directory = new TempDirectory();
        var cachePath = Path.Combine(directory.Path, "cache.json");
        var old = Entry(1, Noon.AddDays(-200));
        File.WriteAllText(
            cachePath,
            $$$"""
            {"schemaVersion":1,"files":{"log.jsonl":{"len":100,"mtime":"{{{Noon:o}}}","entries":[],"parserRevision":0}},"archived":{"ids":[1],"in":370,"out":50,"first":"{{{old.TimestampUtc:o}}}"}}
            """);

        var cache = UsageLogCache.Load(cachePath);
        var parses = 0;
        cache.GetEntries(
            "log.jsonl",
            100,
            Noon,
            _ =>
            {
                parses++;
                return new[] { old };
            });
        cache.ArchiveBefore(Noon.AddDays(-180));

        Assert.Equal(1, parses);
        Assert.Equal(old.IdHash, Assert.Single(cache.ArchivedUsage.Entries).IdHash);
        Assert.False(cache.ArchivedUsage.HasUnpricedLegacyUsage);
    }

    [Fact]
    public void Load_PreservesUnrecoverableV1ArchiveAsUnpricedUsage()
    {
        using var directory = new TempDirectory();
        var cachePath = Path.Combine(directory.Path, "cache.json");
        var first = Noon.AddDays(-200);
        File.WriteAllText(
            cachePath,
            $$$"""
            {"schemaVersion":1,"files":{},"archived":{"ids":[1],"in":370,"out":50,"first":"{{{first:o}}}"}}
            """);

        var cache = UsageLogCache.Load(cachePath);
        cache.ArchiveBefore(Noon.AddDays(-180));
        cache.Save();
        var migrated = UsageLogCache.Load(cachePath);

        Assert.Empty(migrated.ArchivedUsage.Entries);
        Assert.True(migrated.ArchivedUsage.HasUnpricedLegacyUsage);
        Assert.Equal(370m, migrated.ArchivedUsage.UnpricedLegacyInputTokens);
        Assert.Equal(50m, migrated.ArchivedUsage.UnpricedLegacyOutputTokens);
        Assert.Equal(first, migrated.ArchivedUsage.UnpricedLegacyFirstUsageUtc);
    }

    [Fact]
    public void Load_PartialV1RecoveryRemainsMarkedUnpriced_WhenTotalsReachZero()
    {
        using var directory = new TempDirectory();
        var cachePath = Path.Combine(directory.Path, "cache.json");
        var old = Entry(1, Noon.AddDays(-200));
        File.WriteAllText(
            cachePath,
            $$$"""
            {"schemaVersion":1,"files":{"log.jsonl":{"len":100,"mtime":"{{{Noon:o}}}","entries":[],"parserRevision":0}},"archived":{"ids":[1,2],"in":100,"out":10,"first":"{{{old.TimestampUtc:o}}}"}}
            """);

        var cache = UsageLogCache.Load(cachePath);
        cache.GetEntries("log.jsonl", 100, Noon, _ => new[] { old });
        cache.ArchiveBefore(Noon.AddDays(-180));

        Assert.Equal(0m, cache.ArchivedUsage.UnpricedLegacyInputTokens);
        Assert.Equal(0m, cache.ArchivedUsage.UnpricedLegacyOutputTokens);
        Assert.Equal(1, cache.ArchivedUsage.UnresolvedLegacyEntryCount);
        Assert.True(cache.ArchivedUsage.HasUnpricedLegacyUsage);
    }

    [Fact]
    public void ArchiveBefore_KeepsEntriesNewerThanTheCutoff()
    {
        using var directory = new TempDirectory();
        var cache = UsageLogCache.Load(Path.Combine(directory.Path, "cache.json"));
        cache.GetEntries("log.jsonl", 1, Noon, _ => new[] { Entry(1, Noon) });

        cache.ArchiveBefore(Noon.AddDays(-180));

        var pair = Assert.Single(cache.AllEntries());
        Assert.Equal(Entry(1, Noon), Assert.Single(pair.Value));
        Assert.Equal(0m, cache.ArchivedUsage.TotalInputTokens);
    }

    [Fact]
    public void ArchiveBefore_CompactsEntriesOlderThanTheCutoff()
    {
        using var directory = new TempDirectory();
        var cache = UsageLogCache.Load(Path.Combine(directory.Path, "cache.json"));
        cache.GetEntries("log.jsonl", 1, Noon, _ => new[] { Entry(1, Noon.AddDays(-200)) });

        cache.ArchiveBefore(Noon.AddDays(-180));

        var pair = Assert.Single(cache.AllEntries());
        Assert.Empty(pair.Value);
        Assert.Equal(370m, cache.ArchivedUsage.TotalInputTokens);
        Assert.Equal(50m, cache.ArchivedUsage.TotalOutputTokens);
        Assert.Equal(Noon.AddDays(-200), cache.ArchivedUsage.FirstUsageUtc);
    }

    [Fact]
    public void ArchiveBefore_CompactsOnlyOldEntries_WhenAFileSpansTheCutoff()
    {
        using var directory = new TempDirectory();
        var cache = UsageLogCache.Load(Path.Combine(directory.Path, "cache.json"));
        var cutoff = Noon.AddDays(-180);
        cache.GetEntries("log.jsonl", 1, Noon, _ => new[]
        {
            Entry(1, cutoff.AddSeconds(-1)),
            Entry(2, cutoff),
            Entry(3, Noon),
        });

        cache.ArchiveBefore(cutoff);

        var pair = Assert.Single(cache.AllEntries());
        Assert.Equal(new long[] { 2, 3 }, pair.Value.Select(entry => entry.IdHash));
        Assert.Equal(370m, cache.ArchivedUsage.TotalInputTokens);
    }

    [Fact]
    public void ArchiveBefore_KeepsFileMetadataWithEmptyEntries_SoUnchangedFilesAreNotReparsed()
    {
        using var directory = new TempDirectory();
        var cache = UsageLogCache.Load(Path.Combine(directory.Path, "cache.json"));
        var parses = 0;
        IReadOnlyList<TokenUsageEntry> Parser(string _)
        {
            parses++;
            return new[] { Entry(1, Noon.AddDays(-200)) };
        }
        cache.GetEntries("log.jsonl", 100, Noon, Parser);

        cache.ArchiveBefore(Noon.AddDays(-180));
        cache.GetEntries("log.jsonl", 100, Noon, Parser);

        // Dropping the key would force a full reparse of a possibly huge file
        // on every scan, only to discard the whole result again.
        Assert.Equal(1, parses);
    }

    [Fact]
    public void ArchiveBefore_IsIdempotent()
    {
        using var directory = new TempDirectory();
        var cache = UsageLogCache.Load(Path.Combine(directory.Path, "cache.json"));
        var cutoff = Noon.AddDays(-180);
        cache.GetEntries("log.jsonl", 1, Noon, _ => new[]
        {
            Entry(1, Noon.AddDays(-200)),
            Entry(2, Noon),
        });

        cache.ArchiveBefore(cutoff);
        var afterFirst = cache.AllEntries().Single().Value.ToList();
        var archivedAfterFirst = cache.ArchivedUsage;
        cache.ArchiveBefore(cutoff);
        var afterSecond = cache.AllEntries().Single().Value.ToList();

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(archivedAfterFirst.Entries, cache.ArchivedUsage.Entries);
        Assert.Equal(
            archivedAfterFirst.UnpricedLegacyInputTokens,
            cache.ArchivedUsage.UnpricedLegacyInputTokens);
        Assert.Equal(
            archivedAfterFirst.UnpricedLegacyOutputTokens,
            cache.ArchivedUsage.UnpricedLegacyOutputTokens);
        Assert.Equal(Entry(2, Noon), Assert.Single(afterSecond));
    }

    [Fact]
    public void ArchiveBefore_DoesNotDoubleCountAnEntryAfterFileReparse()
    {
        using var directory = new TempDirectory();
        var cache = UsageLogCache.Load(Path.Combine(directory.Path, "cache.json"));
        var old = Entry(1, Noon.AddDays(-200));
        var cutoff = Noon.AddDays(-180);
        cache.GetEntries("log.jsonl", 1, Noon, _ => new[] { old });
        cache.ArchiveBefore(cutoff);

        cache.GetEntries("log.jsonl", 2, Noon, _ => new[] { old });
        cache.ArchiveBefore(cutoff);

        Assert.Equal(370m, cache.ArchivedUsage.TotalInputTokens);
        Assert.Equal(50m, cache.ArchivedUsage.TotalOutputTokens);
        Assert.Empty(cache.AllEntries().Single().Value);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsArchivedUsage()
    {
        using var directory = new TempDirectory();
        var cachePath = Path.Combine(directory.Path, "cache.json");
        var cache = UsageLogCache.Load(cachePath);
        cache.GetEntries("log.jsonl", 100, Noon, _ => new[]
        {
            Entry(1, Noon.AddDays(-200)),
            Entry(2, Noon),
        });
        cache.ArchiveBefore(Noon.AddDays(-180));
        cache.Save();

        var reloaded = UsageLogCache.Load(cachePath);

        var pair = Assert.Single(reloaded.AllEntries());
        Assert.Equal("log.jsonl", pair.Key);
        Assert.Equal(Entry(2, Noon), Assert.Single(pair.Value));
        Assert.Equal(370m, reloaded.ArchivedUsage.TotalInputTokens);
        Assert.Equal(50m, reloaded.ArchivedUsage.TotalOutputTokens);
        Assert.Equal(Noon.AddDays(-200), reloaded.ArchivedUsage.FirstUsageUtc);
    }

    [Fact]
    public void ArchiveBefore_DeduplicatesEntriesAcrossFiles()
    {
        using var directory = new TempDirectory();
        var cache = UsageLogCache.Load(Path.Combine(directory.Path, "cache.json"));
        var old = Entry(1, Noon.AddDays(-200));
        var conflicting = old with { InputTokens = 999 };
        cache.GetEntries("b.jsonl", 1, Noon, _ => new[] { conflicting });
        cache.GetEntries("a.jsonl", 1, Noon, _ => new[] { old });

        cache.ArchiveBefore(Noon.AddDays(-180));

        // Deterministic path order means a.jsonl wins regardless of insertion order.
        Assert.Equal(370m, cache.ArchivedUsage.TotalInputTokens);
        Assert.Equal(50m, cache.ArchivedUsage.TotalOutputTokens);
        Assert.All(cache.AllEntries(), pair => Assert.Empty(pair.Value));
    }

    [Fact]
    public void ArchiveBefore_DoesNotOverflowLongTokenBuckets()
    {
        using var directory = new TempDirectory();
        var cache = UsageLogCache.Load(Path.Combine(directory.Path, "cache.json"));
        var old = new TokenUsageEntry(
            1,
            Noon.AddDays(-200),
            UsageService.Claude,
            "claude-fable-5",
            long.MaxValue,
            long.MaxValue,
            long.MaxValue,
            long.MaxValue,
            long.MaxValue);
        cache.GetEntries("log.jsonl", 1, Noon, _ => new[] { old });

        cache.ArchiveBefore(Noon.AddDays(-180));

        Assert.Equal((decimal)long.MaxValue * 4m, cache.ArchivedUsage.TotalInputTokens);
        Assert.Equal((decimal)long.MaxValue, cache.ArchivedUsage.TotalOutputTokens);
    }

    [Fact]
    public void Save_DoesNotRewriteAnUnchangedCache()
    {
        using var directory = new TempDirectory();
        var cachePath = Path.Combine(directory.Path, "cache.json");
        var cache = UsageLogCache.Load(cachePath);
        cache.GetEntries("log.jsonl", 1, Noon, _ => new[] { Entry(1) });
        cache.Save();
        File.SetLastWriteTimeUtc(cachePath, Noon);

        cache.Save();

        Assert.Equal(Noon, File.GetLastWriteTimeUtc(cachePath));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsLargeArchivedHistory()
    {
        using var directory = new TempDirectory();
        var cachePath = Path.Combine(directory.Path, "cache.json");
        var cache = UsageLogCache.Load(cachePath);
        var old = Noon.AddDays(-200);
        var entries = Enumerable.Range(1, 100_000)
            .Select(id => Entry(id, old))
            .ToArray();
        cache.GetEntries("large.jsonl", 1, Noon, _ => entries);

        cache.ArchiveBefore(Noon.AddDays(-180));
        cache.Save();
        var reloaded = UsageLogCache.Load(cachePath);
        var json = File.ReadAllText(cachePath);

        Assert.Equal(37_000_000m, reloaded.ArchivedUsage.TotalInputTokens);
        Assert.Equal(5_000_000m, reloaded.ArchivedUsage.TotalOutputTokens);
        Assert.Contains("\"models\":[\"claude-fable-5\"]", json);
        Assert.Contains("\"rows\":[[", json);
        Assert.DoesNotContain("\"model\":\"claude-fable-5\"", json);
        Assert.InRange(new FileInfo(cachePath).Length, 1_000_000, 10_000_000);
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
