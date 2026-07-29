using UsageBeacon.Models.Insights;
using UsageBeacon.Services.Insights;

namespace UsageBeacon.Tests;

public sealed class UsageAggregatorTests
{
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.CreateCustomTimeZone(
        "Test+9", TimeSpan.FromHours(9), "Test+9", "Test+9");

    private static readonly DateOnly Today = new(2026, 7, 20);

    private static readonly ModelPricingCatalog Pricing = new("2026-07-20",
        new Dictionary<string, ModelPricing>
        {
            ["claude-fable-5"] = new(10m, 1m, 12.5m, 20m, 50m),
            ["gpt-5.6-sol"] = new(5m, 0.5m, 0m, 0m, 30m),
        });

    private static TokenUsageEntry Entry(
        long id,
        DateTime timestampUtc,
        UsageService service = UsageService.Claude,
        string model = "claude-fable-5",
        long input = 1_000_000,
        long output = 0)
        => new(id, timestampUtc, service, model, input, 0, 0, 0, output);

    private static DashboardData Aggregate(params TokenUsageEntry[] entries)
        => UsageAggregator.Aggregate(
            new[]
            {
                new KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>("file-a", entries),
            },
            Pricing, Today, Tokyo);

    [Fact]
    public void Aggregate_BucketsByLocalCalendarDay()
    {
        // 2026-07-19T16:00Z is 2026-07-20 01:00 at UTC+9 — that is "today".
        // 2026-07-19T14:59Z is 2026-07-19 23:59 at UTC+9 — that is yesterday.
        var data = Aggregate(
            Entry(1, new DateTime(2026, 7, 19, 16, 0, 0, DateTimeKind.Utc)),
            Entry(2, new DateTime(2026, 7, 19, 14, 59, 0, DateTimeKind.Utc)));

        Assert.Equal(10m, data.Today.CostUsd);
        Assert.Equal(20m, data.Last7Days.CostUsd);
        Assert.Equal(30, data.Days.Count);
        Assert.Equal(10m, data.Days[^1].TotalCostUsd);
        Assert.Equal(10m, data.Days[^2].TotalCostUsd);
    }

    [Fact]
    public void Aggregate_AppliesSevenAndThirtyDayWindows()
    {
        var data = Aggregate(
            Entry(1, new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc)),   // today
            Entry(2, new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc)),   // 7-day window edge
            Entry(3, new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc)),   // 30-day only
            Entry(4, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));   // outside

        Assert.Equal(10m, data.Today.CostUsd);
        Assert.Equal(20m, data.Last7Days.CostUsd);
        Assert.Equal(30m, data.Last30Days.CostUsd);
        Assert.Equal(40m, data.Lifetime.CostUsd);
        Assert.Equal(new DateOnly(2026, 6, 1), data.Lifetime.FirstUsageDay);
    }

    [Fact]
    public void Aggregate_DeduplicatesAcrossFilesDeterministically()
    {
        var timestamp = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var files = new[]
        {
            new KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>(
                "file-b", new[] { Entry(1, timestamp) }),
            new KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>(
                "file-a", new[] { Entry(1, timestamp), Entry(2, timestamp) }),
        };

        var data = UsageAggregator.Aggregate(files, Pricing, Today, Tokyo);

        Assert.Equal(20m, data.Today.CostUsd);
        Assert.Equal(20m, data.Lifetime.CostUsd);
    }

    [Fact]
    public void Aggregate_SplitsCostsByService()
    {
        var timestamp = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var data = Aggregate(
            Entry(1, timestamp),
            Entry(2, timestamp, UsageService.Codex, "gpt-5.6-sol", input: 0, output: 1_000_000));

        Assert.Equal(10m, data.Today.ClaudeCostUsd);
        Assert.Equal(30m, data.Today.CodexCostUsd);
        Assert.Equal(40m, data.Today.CostUsd);
        Assert.Equal(10m, data.Lifetime.ClaudeCostUsd);
        Assert.Equal(30m, data.Lifetime.CodexCostUsd);
        Assert.Equal(10m, data.Days[^1].ClaudeCostUsd);
        Assert.Equal(30m, data.Days[^1].CodexCostUsd);
    }

    [Fact]
    public void Aggregate_FlagsUnknownModels_AndExcludesThemFromCost()
    {
        var timestamp = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var data = Aggregate(
            Entry(1, timestamp),
            Entry(2, timestamp, model: "mystery-model"));

        Assert.Equal(10m, data.Today.CostUsd);
        Assert.True(data.Today.HasUnknownModels);
        Assert.Contains("mystery-model", data.UnknownModels);
        var mystery = Assert.Single(data.Models, model => model.Model == "mystery-model");
        Assert.Null(mystery.CostUsd);
    }

    [Fact]
    public void Aggregate_OrdersModelBreakdownByCost()
    {
        var timestamp = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var data = Aggregate(
            Entry(1, timestamp, UsageService.Codex, "gpt-5.6-sol", input: 1_000_000),
            Entry(2, timestamp));

        Assert.Equal("claude-fable-5", data.Models[0].Model);
        Assert.Equal("gpt-5.6-sol", data.Models[1].Model);
    }

    [Fact]
    public void Aggregate_LifetimePricesAllTokenBucketsAndArchivedUsage()
    {
        var timestamp = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var entry = new TokenUsageEntry(
            1,
            timestamp,
            UsageService.Claude,
            "claude-fable-5",
            1_000_000,
            1_000_000,
            1_000_000,
            1_000_000,
            1_000_000);
        var archivedEntry = Entry(
            2,
            new DateTime(2026, 1, 1, 16, 0, 0, DateTimeKind.Utc),
            UsageService.Codex,
            "gpt-5.6-sol",
            input: 0,
            output: 1_000_000);
        var archived = new ArchivedUsageSnapshot(
            new[] { ArchivedUsageEntry.From(archivedEntry) },
            0m,
            0m,
            null);

        var data = UsageAggregator.Aggregate(
            new[]
            {
                new KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>(
                    "file-a",
                    new[] { entry }),
            },
            Pricing,
            Today,
            Tokyo,
            archived);

        Assert.Equal(93.5m, data.Lifetime.ClaudeCostUsd);
        Assert.Equal(30m, data.Lifetime.CodexCostUsd);
        Assert.Equal(123.5m, data.Lifetime.CostUsd);
        Assert.False(data.Lifetime.HasUnknownCost);
        Assert.Equal(new DateOnly(2026, 1, 2), data.Lifetime.FirstUsageDay);
    }

    [Fact]
    public void Aggregate_LifetimeExcludesFutureUsageAndOldModelsFromThirtyDayViews()
    {
        var data = Aggregate(
            Entry(
                1,
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                model: "old-unknown"),
            Entry(
                2,
                new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(0m, data.Lifetime.CostUsd);
        Assert.True(data.Lifetime.HasUnknownModels);
        Assert.Empty(data.Models);
        Assert.Contains("old-unknown", data.UnknownModels);
        Assert.Equal(0m, data.Last30Days.CostUsd);
    }

    [Fact]
    public void Aggregate_LifetimeFlagsUnpricedLegacyUsage()
    {
        var archived = new ArchivedUsageSnapshot(
            [],
            100m,
            50m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var data = UsageAggregator.Aggregate(
            Array.Empty<KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>>(),
            Pricing,
            Today,
            Tokyo,
            archived);

        Assert.Equal(0m, data.Lifetime.CostUsd);
        Assert.True(data.Lifetime.HasUnpricedLegacyUsage);
        Assert.True(data.Lifetime.HasUnknownCost);
        Assert.Equal(new DateOnly(2026, 1, 1), data.Lifetime.FirstUsageDay);
    }

    [Fact]
    public void Aggregate_LifetimeExcludesFutureArchivedUsage()
    {
        var future = Entry(
            1,
            new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc));
        var archived = new ArchivedUsageSnapshot(
            new[] { ArchivedUsageEntry.From(future) },
            0m,
            0m,
            null);

        var data = UsageAggregator.Aggregate(
            Array.Empty<KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>>(),
            Pricing,
            Today,
            Tokyo,
            archived);

        Assert.Equal(0m, data.Lifetime.CostUsd);
        Assert.Null(data.Lifetime.FirstUsageDay);
    }

    [Fact]
    public void Aggregate_LifetimeRepricesArchivedUnknownModel()
    {
        var entry = Entry(
            1,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            model: "newly-priced");
        var archived = new ArchivedUsageSnapshot(
            new[] { ArchivedUsageEntry.From(entry) },
            0m,
            0m,
            null);
        var updatedPricing = new ModelPricingCatalog(
            "2026-07-21",
            new Dictionary<string, ModelPricing>
            {
                ["newly-priced"] = new(7m, 0m, 0m, 0m, 0m),
            });

        var before = UsageAggregator.Aggregate(
            Array.Empty<KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>>(),
            Pricing,
            Today,
            Tokyo,
            archived);
        var after = UsageAggregator.Aggregate(
            Array.Empty<KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>>(),
            updatedPricing,
            Today,
            Tokyo,
            archived);

        Assert.True(before.Lifetime.HasUnknownModels);
        Assert.Equal(0m, before.Lifetime.CostUsd);
        Assert.False(after.Lifetime.HasUnknownModels);
        Assert.Equal(7m, after.Lifetime.ClaudeCostUsd);
    }

    [Fact]
    public void Aggregate_LifetimeUsesExactArchivedTimestampAtPriceBoundary()
    {
        var pricing = ModelPricingCatalog.ParseDocument(
            """
            {
                "asOf": "2026-01-01",
                "models": {
                    "boundary-model": [
                        {
                            "effectiveFrom": "2026-01-01T00:00:00Z",
                            "input": 10,
                            "cachedInput": 0,
                            "cacheWrite5m": 0,
                            "cacheWrite1h": 0,
                            "output": 0
                        },
                        {
                            "effectiveFrom": "2026-01-01T12:00:00Z",
                            "input": 20,
                            "cachedInput": 0,
                            "cacheWrite5m": 0,
                            "cacheWrite1h": 0,
                            "output": 0
                        }
                    ]
                }
            }
            """)!;
        var archived = new ArchivedUsageSnapshot(
            new[]
            {
                ArchivedUsageEntry.From(Entry(
                    1,
                    new DateTime(2026, 1, 1, 11, 59, 59, DateTimeKind.Utc),
                    model: "boundary-model")),
                ArchivedUsageEntry.From(Entry(
                    2,
                    new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                    model: "boundary-model")),
            },
            0m,
            0m,
            null);

        var data = UsageAggregator.Aggregate(
            Array.Empty<KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>>(),
            pricing,
            Today,
            Tokyo,
            archived);

        Assert.Equal(30m, data.Lifetime.ClaudeCostUsd);
    }

    [Fact]
    public void Aggregate_LifetimeUsesDeterministicDuplicateWinner()
    {
        var timestamp = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var smaller = Entry(1, timestamp, input: 10);
        var larger = Entry(1, timestamp.AddDays(-1), input: 100);
        var forward = new[]
        {
            new KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>("b", new[] { larger }),
            new KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>("a", new[] { smaller }),
        };

        var first = UsageAggregator.Aggregate(forward, Pricing, Today, Tokyo);
        var second = UsageAggregator.Aggregate(forward.Reverse(), Pricing, Today, Tokyo);

        Assert.Equal(0.0001m, first.Lifetime.CostUsd);
        Assert.Equal(first.Lifetime, second.Lifetime);
    }

    [Fact]
    public void Aggregate_LifetimeDoesNotOverflowLongTokenValues()
    {
        var timestamp = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var entry = new TokenUsageEntry(
            1,
            timestamp,
            UsageService.Claude,
            "claude-fable-5",
            long.MaxValue,
            long.MaxValue,
            long.MaxValue,
            long.MaxValue,
            long.MaxValue);

        var data = Aggregate(entry);

        var expected = (decimal)long.MaxValue * 93.5m / 1_000_000m;
        Assert.Equal(expected, data.Lifetime.ClaudeCostUsd);
    }

    [Fact]
    public void Aggregate_HandlesLargeLifetimeHistory()
    {
        var timestamp = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var entries = Enumerable.Range(1, 100_000)
            .Select(id => Entry(id, timestamp, input: 1))
            .ToArray();

        var data = Aggregate(entries);

        Assert.Equal(1m, data.Lifetime.CostUsd);
    }
}
