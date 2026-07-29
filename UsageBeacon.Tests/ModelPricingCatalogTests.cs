using UsageBeacon.Models.Insights;
using UsageBeacon.Services.Insights;

namespace UsageBeacon.Tests;

public sealed class ModelPricingCatalogTests
{
    private static ModelPricingCatalog CreateCatalog() => new("2026-07-20",
        new Dictionary<string, ModelPricing>
        {
            ["gpt-5"] = new(1.25m, 0.125m, 0m, 0m, 10m),
            ["gpt-5.5"] = new(5m, 0.5m, 0m, 0m, 30m),
            ["claude-fable-5"] = new(10m, 1m, 12.5m, 20m, 50m),
        });

    [Fact]
    public void Resolve_PrefersExactMatch()
        => Assert.Equal(5m, CreateCatalog().Resolve("gpt-5.5")!.Input);

    [Fact]
    public void Resolve_MatchesPrefixOnlyAtDashBoundary()
    {
        var catalog = CreateCatalog();

        // "gpt-5" may claim "gpt-5-codex" but never the dotted "gpt-5.6" family.
        Assert.Equal(1.25m, catalog.Resolve("gpt-5-codex")!.Input);
        Assert.Null(catalog.Resolve("gpt-5.6-sol"));
        Assert.Equal(10m, catalog.Resolve("claude-fable-5-20260101")!.Input);
    }

    [Fact]
    public void Resolve_ReturnsNullForUnknownModel()
        => Assert.Null(CreateCatalog().Resolve("gemini-3-flash"));

    [Fact]
    public void TryGetCost_AppliesPerMillionRatesToEachBucket()
    {
        var entry = new TokenUsageEntry(
            IdHash: 1,
            TimestampUtc: DateTime.UtcNow,
            Service: UsageService.Claude,
            Model: "claude-fable-5",
            InputTokens: 1_000_000,
            CachedInputTokens: 1_000_000,
            CacheWrite5mTokens: 1_000_000,
            CacheWrite1hTokens: 1_000_000,
            OutputTokens: 1_000_000);

        Assert.Equal(93.5m, CreateCatalog().TryGetCost(entry));
    }

    [Fact]
    public void TryGetCost_ReturnsNullForUnknownModel()
    {
        var entry = new TokenUsageEntry(
            2, DateTime.UtcNow, UsageService.Codex, "mystery-model", 100, 0, 0, 0, 10);

        Assert.Null(CreateCatalog().TryGetCost(entry));
    }

    [Fact]
    public void ParseDocument_UsesTheRateEffectiveAtTheUsageTimestamp()
    {
        var catalog = ModelPricingCatalog.ParseDocument(
            """
            {
                "asOf": "2026-07-29",
                "models": {
                    "scheduled": [
                        { "effectiveFrom": "2026-09-01", "input": 3, "cachedInput": 0.3, "cacheWrite5m": 0, "cacheWrite1h": 0, "output": 15 },
                        { "effectiveFrom": "2026-01-01", "input": 2, "cachedInput": 0.2, "cacheWrite5m": 0, "cacheWrite1h": 0, "output": 10 }
                    ]
                }
            }
            """);

        Assert.NotNull(catalog);
        Assert.Equal(2m, catalog!.Resolve(
            "scheduled",
            new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc))!.Input);
        Assert.Equal(3m, catalog.Resolve(
            "scheduled",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc))!.Input);
        Assert.Null(catalog.Resolve(
            "scheduled",
            new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc)));
    }

    [Fact]
    public void ParseDocument_RejectsDuplicateEffectiveDates()
    {
        var catalog = ModelPricingCatalog.ParseDocument(
            """
            {"models":{"scheduled":[
                {"effectiveFrom":"2026-09-01","input":2,"cachedInput":0,"cacheWrite5m":0,"cacheWrite1h":0,"output":10},
                {"effectiveFrom":"2026-09-01","input":3,"cachedInput":0,"cacheWrite5m":0,"cacheWrite1h":0,"output":15}
            ]}}
            """);

        Assert.Null(catalog);
    }

    [Fact]
    public void EmbeddedPricing_IncludesVerifiedLifetimeModelRates()
    {
        var path = Path.Combine(
            RepositoryRoot(),
            "UsageBeacon",
            "Resources",
            "model-pricing.json");
        var catalog = ModelPricingCatalog.ParseDocument(File.ReadAllText(path));

        Assert.NotNull(catalog);
        Assert.Equal("2026-07-30", catalog!.AsOf);
        Assert.Equal(
            new ModelPricing(5m, 0.5m, 6.25m, 10m, 25m),
            catalog.Resolve("claude-opus-5"));

        var entry = new TokenUsageEntry(
            IdHash: 3,
            TimestampUtc: DateTime.UtcNow,
            Service: UsageService.Claude,
            Model: "claude-opus-5",
            InputTokens: 1_000_000,
            CachedInputTokens: 1_000_000,
            CacheWrite5mTokens: 1_000_000,
            CacheWrite1hTokens: 1_000_000,
            OutputTokens: 1_000_000);
        Assert.Equal(46.75m, catalog.TryGetCost(entry));
        Assert.Equal(
            new ModelPricing(1.75m, 0.175m, 0m, 0m, 14m),
            catalog.Resolve("gpt-5.2-codex"));
    }

    [Fact]
    public void EmbeddedPricing_AppliesTheClaudeSonnet5ScheduledIncrease()
    {
        var path = Path.Combine(
            RepositoryRoot(),
            "UsageBeacon",
            "Resources",
            "model-pricing.json");
        var catalog = ModelPricingCatalog.ParseDocument(File.ReadAllText(path));

        Assert.NotNull(catalog);
        Assert.Equal(2m, catalog!.Resolve(
            "claude-sonnet-5",
            new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc))!.Input);
        Assert.Equal(3m, catalog.Resolve(
            "claude-sonnet-5",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc))!.Input);
    }

    [Fact]
    public void MergeOverride_ReplacesAndAddsModels()
    {
        using var directory = new TempDirectory();
        var overridePath = Path.Combine(directory.Path, "model-pricing.json");
        File.WriteAllText(overridePath,
            """
            {"asOf":"2026-08-01","models":{"gpt-5.5":{"input":4,"cachedInput":0.4,"cacheWrite5m":0,"cacheWrite1h":0,"output":24},"gemini-3-flash":{"input":0.3,"cachedInput":0.03,"cacheWrite5m":0,"cacheWrite1h":0,"output":2.5}}}
            """);

        var merged = ModelPricingCatalog.MergeOverride(CreateCatalog(), overridePath);

        Assert.Equal("2026-08-01", merged.AsOf);
        Assert.Equal(4m, merged.Resolve("gpt-5.5")!.Input);
        Assert.Equal(0.3m, merged.Resolve("gemini-3-flash")!.Input);
        Assert.Equal(10m, merged.Resolve("claude-fable-5")!.Input);
    }

    [Fact]
    public void MergeOverride_IgnoresMissingOrBrokenOverrideFile()
    {
        using var directory = new TempDirectory();
        var missing = Path.Combine(directory.Path, "missing.json");
        var broken = Path.Combine(directory.Path, "broken.json");
        File.WriteAllText(broken, "{not json");

        Assert.Equal(5m, ModelPricingCatalog.MergeOverride(CreateCatalog(), missing).Resolve("gpt-5.5")!.Input);
        Assert.Equal(5m, ModelPricingCatalog.MergeOverride(CreateCatalog(), broken).Resolve("gpt-5.5")!.Input);
    }

    [Fact]
    public void MergeOverride_ScheduleReplacesTheWholeBuiltInModelSchedule()
    {
        using var directory = new TempDirectory();
        var overridePath = Path.Combine(directory.Path, "model-pricing.json");
        File.WriteAllText(overridePath,
            """
            {"models":{"gpt-5.5":[
                {"effectiveFrom":"2026-10-01","input":7,"cachedInput":0.7,"cacheWrite5m":0,"cacheWrite1h":0,"output":42}
            ]}}
            """);

        var merged = ModelPricingCatalog.MergeOverride(CreateCatalog(), overridePath);

        Assert.Null(merged.Resolve(
            "gpt-5.5",
            new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc)));
        Assert.Equal(7m, merged.Resolve(
            "gpt-5.5",
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc))!.Input);
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

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UsageBeacon.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
