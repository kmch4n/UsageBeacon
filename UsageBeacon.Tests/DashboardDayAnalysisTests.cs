using UsageBeacon.Models.Insights;
using UsageBeacon.Services.Insights;

namespace UsageBeacon.Tests;

public sealed class DashboardDayAnalysisTests
{
    [Fact]
    public void Build_FindsDifferentCostAndTokenPeaks()
    {
        var expensive = new DailyUsagePoint(new DateOnly(2026, 9, 23), 4m, 2m, 100, 50);
        var highVolume = new DailyUsagePoint(new DateOnly(2026, 9, 24), 1m, 1m,
            1_000_000, 250_000, true);

        var result = DashboardDayAnalysis.Build([expensive, highVolume]);

        Assert.Equal(expensive.Day, result.HighestCostDay?.Day);
        Assert.Equal(highVolume.Day, result.HighestTokenDay?.Day);
        Assert.Equal(6m, result.MaxCostUsd);
        Assert.Equal(1_250_000L, result.MaxTokens);
        Assert.True(result.HasIncompleteCost);
    }

    [Fact]
    public void Build_HandlesEmptyAndZeroDays()
    {
        var empty = DashboardDayAnalysis.Build([]);
        var zero = DashboardDayAnalysis.Build([
            new DailyUsagePoint(new DateOnly(2026, 9, 25), 0m, 0m)]);

        Assert.Null(empty.HighestCostDay);
        Assert.Null(empty.HighestTokenDay);
        Assert.Equal(0m, zero.MaxCostUsd);
        Assert.Equal(0L, zero.MaxTokens);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1_200, "1.2K")]
    [InlineData(1_250_000, "1.25M")]
    [InlineData(1_250_000_000, "1.25B")]
    public void FormatCompactTokens_LabelsTheChartScale(long tokens, string expected)
        => Assert.Equal(expected, DashboardDayAnalysis.FormatCompactTokens(tokens));

    [Theory]
    [InlineData("1211.87", "1500", "500", 4)]
    [InlineData("1964130000", "2000000000", "500000000", 5)]
    public void NiceScale_UsesRoundIntervals(
        string maximum, string ceiling, string step, int tickCount)
    {
        var scale = DashboardChartScale.Create(decimal.Parse(maximum));

        Assert.Equal(decimal.Parse(ceiling), scale.Ceiling);
        Assert.Equal(decimal.Parse(step), scale.Step);
        Assert.Equal(tickCount, scale.Ticks.Count);
        Assert.Equal(scale.Ceiling, scale.Ticks[0]);
        Assert.Equal(0m, scale.Ticks[^1]);
    }

    [Fact]
    public void NiceScale_CanUseWholeTokenSteps()
    {
        var scale = DashboardChartScale.Create(2m, 1m);

        Assert.Equal(2m, scale.Ceiling);
        Assert.Equal([2m, 1m, 0m], scale.Ticks);
    }

    [Fact]
    public void NiceScale_RespectsMinimumCostStep()
    {
        var scale = DashboardChartScale.Create(0.001m, 0.01m);

        Assert.Equal([0.01m, 0m], scale.Ticks);
    }
}
