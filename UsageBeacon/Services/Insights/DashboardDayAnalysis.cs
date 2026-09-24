using System.Globalization;
using UsageBeacon.Models.Insights;

namespace UsageBeacon.Services.Insights;

/// <summary>Scale and peak values shared by the cost and token charts.</summary>
public sealed record DashboardDayAnalysis(
    decimal MaxCostUsd,
    long MaxTokens,
    DailyUsagePoint? HighestCostDay,
    DailyUsagePoint? HighestTokenDay,
    bool HasIncompleteCost)
{
    public static DashboardDayAnalysis Build(IReadOnlyList<DailyUsagePoint> days)
    {
        var highestCost = days.OrderByDescending(day => day.TotalCostUsd).FirstOrDefault();
        var highestTokens = days.OrderByDescending(day => day.InputTokens + day.OutputTokens)
            .FirstOrDefault();
        return new DashboardDayAnalysis(
            highestCost?.TotalCostUsd ?? 0m,
            highestTokens is { } tokenDay ? tokenDay.InputTokens + tokenDay.OutputTokens : 0L,
            highestCost is { TotalCostUsd: > 0m } ? highestCost : null,
            highestTokens is { InputTokens: > 0 } or { OutputTokens: > 0 }
                ? highestTokens : null,
            days.Any(day => day.HasUnknownModels));
    }

    public static string FormatCompactTokens(long tokens)
    {
        if (tokens >= 1_000_000_000)
            return (tokens / 1_000_000_000m).ToString("0.##", CultureInfo.InvariantCulture) + "B";
        if (tokens >= 1_000_000)
            return (tokens / 1_000_000m).ToString("0.##", CultureInfo.InvariantCulture) + "M";
        if (tokens >= 1_000)
            return (tokens / 1_000m).ToString("0.##", CultureInfo.InvariantCulture) + "K";
        return tokens.ToString("N0", CultureInfo.InvariantCulture);
    }
}
