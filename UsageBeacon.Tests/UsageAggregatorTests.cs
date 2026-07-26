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
}
