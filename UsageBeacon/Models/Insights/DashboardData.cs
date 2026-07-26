namespace UsageBeacon.Models.Insights;

/// <summary>Aggregated token and cost totals for one period.</summary>
public sealed record UsagePeriodSummary(
    long TotalInputTokens,
    long TotalOutputTokens,
    decimal CostUsd,
    decimal ClaudeCostUsd,
    decimal CodexCostUsd,
    bool HasUnknownModels);

/// <summary>Estimated cost for one local calendar day, split by service.</summary>
public sealed record DailyUsagePoint(
    DateOnly Day,
    decimal ClaudeCostUsd,
    decimal CodexCostUsd)
{
    public decimal TotalCostUsd => ClaudeCostUsd + CodexCostUsd;
}

/// <summary>Token and cost totals for one model over the report window.</summary>
public sealed record ModelUsageBreakdown(
    string Model,
    UsageService Service,
    long InputTokens,
    long CachedInputTokens,
    long OutputTokens,
    decimal? CostUsd);

/// <summary>Everything the dashboard window renders.</summary>
public sealed record DashboardData(
    UsagePeriodSummary Today,
    UsagePeriodSummary Last7Days,
    UsagePeriodSummary Last30Days,
    IReadOnlyList<DailyUsagePoint> Days,
    IReadOnlyList<ModelUsageBreakdown> Models,
    IReadOnlyList<string> UnknownModels);
