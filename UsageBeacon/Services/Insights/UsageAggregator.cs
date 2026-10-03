using UsageBeacon.Models.Insights;

namespace UsageBeacon.Services.Insights;

/// <summary>
/// Turns raw usage entries into the dashboard report: today / last 7 days /
/// last 30 days summaries, locally retained lifetime costs, a 30-day
/// daily series, and a per-model breakdown. Days are local calendar days
/// derived from the entry's UTC timestamp.
/// </summary>
public static class UsageAggregator
{
    public static DashboardData Aggregate(
        IEnumerable<KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>> files,
        ModelPricingCatalog pricing,
        DateOnly today,
        TimeZoneInfo timeZone,
        ArchivedUsageSnapshot? archivedUsage = null)
    {
        // Deterministic order so cross-file duplicate ids always resolve the
        // same way regardless of directory enumeration order.
        var seen = new HashSet<long>();
        var last30Start = today.AddDays(-29);
        var last7Start = today.AddDays(-6);

        var lifetime = new LifetimeAccumulator();
        var hasUnpricedLegacyUsage = archivedUsage?.HasUnpricedLegacyUsage ?? false;
        var firstUsageUtc = ValidFirstUsageUtc(
            archivedUsage?.UnpricedLegacyFirstUsageUtc,
            today,
            timeZone);
        var todayTotals = new PeriodAccumulator();
        var weekTotals = new PeriodAccumulator();
        var monthTotals = new PeriodAccumulator();
        var dayCosts = new Dictionary<DateOnly, PeriodAccumulator>();
        var models = new Dictionary<(string Model, UsageService Service), ModelAccumulator>();
        var unknownModels = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archivedUsage?.Entries ?? [])
        {
            if (!seen.Add(entry.IdHash))
                continue;

            var day = LocalDay(entry.TimestampUtc, timeZone);
            if (day > today)
                continue;

            var cost = pricing.TryGetCost(
                entry.Model,
                entry.TimestampUtc,
                entry.InputTokens,
                entry.CachedInputTokens,
                entry.CacheWrite5mTokens,
                entry.CacheWrite1hTokens,
                entry.OutputTokens);
            lifetime.Add(entry.Service, entry.Model, cost, unknownModels);
            if (firstUsageUtc is null || entry.TimestampUtc < firstUsageUtc)
                firstUsageUtc = entry.TimestampUtc;
        }

        foreach (var pair in files.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var entry in pair.Value)
            {
                if (!seen.Add(entry.IdHash)) continue;

                var day = LocalDay(entry.TimestampUtc, timeZone);
                if (day > today) continue;

                var cost = pricing.TryGetCost(entry);
                lifetime.Add(entry.Service, entry.Model, cost, unknownModels);
                if (firstUsageUtc is null || entry.TimestampUtc < firstUsageUtc)
                    firstUsageUtc = entry.TimestampUtc;

                if (day < last30Start) continue;

                monthTotals.Add(entry, cost);
                if (day >= last7Start) weekTotals.Add(entry, cost);
                if (day == today) todayTotals.Add(entry, cost);

                if (!dayCosts.TryGetValue(day, out var daily))
                    dayCosts[day] = daily = new PeriodAccumulator();
                daily.Add(entry, cost);

                var key = (entry.Model, entry.Service);
                if (!models.TryGetValue(key, out var model))
                    models[key] = model = new ModelAccumulator();
                model.Add(entry, cost);
            }
        }

        var days = new List<DailyUsagePoint>(30);
        for (var day = last30Start; day <= today; day = day.AddDays(1))
        {
            var daily = dayCosts.TryGetValue(day, out var found)
                ? found.ToSummary()
                : new UsagePeriodSummary(0, 0, 0, 0, 0, false);
            days.Add(new DailyUsagePoint(day, daily.ClaudeCostUsd, daily.CodexCostUsd,
                daily.TotalInputTokens, daily.TotalOutputTokens, daily.HasUnknownModels,
                daily.AgyCostUsd));
        }

        var breakdown = models
            .Select(pair => new ModelUsageBreakdown(
                pair.Key.Model,
                pair.Key.Service,
                pair.Value.Input,
                pair.Value.Cached,
                pair.Value.Output,
                pair.Value.HasUnknownCost ? null : pair.Value.Cost))
            .OrderByDescending(model => model.CostUsd ?? 0m)
            .ThenByDescending(model => model.InputTokens + model.CachedInputTokens + model.OutputTokens)
            .ToList();

        return new DashboardData(
            new LifetimeCostSummary(
                lifetime.ClaudeCost,
                lifetime.CodexCost,
                lifetime.ClaudeHasUnknown,
                lifetime.CodexHasUnknown,
                hasUnpricedLegacyUsage,
                firstUsageUtc is { } first
                    ? LocalDay(first, timeZone)
                    : null,
                lifetime.AgyCost,
                lifetime.AgyHasUnknown),
            todayTotals.ToSummary(),
            weekTotals.ToSummary(),
            monthTotals.ToSummary(),
            days,
            breakdown,
            unknownModels.ToList());
    }

    private static DateOnly LocalDay(DateTime timestampUtc, TimeZoneInfo timeZone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(timestampUtc, timeZone));

    private static DateTime? ValidFirstUsageUtc(
        DateTime? timestampUtc,
        DateOnly today,
        TimeZoneInfo timeZone)
        => timestampUtc is { } timestamp && LocalDay(timestamp, timeZone) <= today
            ? timestamp
            : null;

    private sealed class LifetimeAccumulator
    {
        public decimal ClaudeCost { get; private set; }
        public decimal CodexCost { get; private set; }
        public decimal AgyCost { get; private set; }
        public bool ClaudeHasUnknown { get; private set; }
        public bool CodexHasUnknown { get; private set; }
        public bool AgyHasUnknown { get; private set; }

        public void Add(UsageService service, string model, decimal? cost, ISet<string> unknownModels)
        {
            if (cost is null)
            {
                switch (service)
                {
                    case UsageService.Claude: ClaudeHasUnknown = true; break;
                    case UsageService.Agy: AgyHasUnknown = true; break;
                    default: CodexHasUnknown = true; break;
                }
                unknownModels.Add(model);
                return;
            }

            switch (service)
            {
                case UsageService.Claude: ClaudeCost += cost.Value; break;
                case UsageService.Agy: AgyCost += cost.Value; break;
                default: CodexCost += cost.Value; break;
            }
        }
    }

    private sealed class PeriodAccumulator
    {
        private long _input;
        private long _output;
        private decimal _claudeCost;
        private decimal _codexCost;
        private decimal _agyCost;
        private bool _hasUnknown;

        public void Add(TokenUsageEntry entry, decimal? cost)
        {
            _input += entry.InputTokens + entry.CachedInputTokens +
                      entry.CacheWrite5mTokens + entry.CacheWrite1hTokens;
            _output += entry.OutputTokens;
            if (cost is null) _hasUnknown = true;
            else if (entry.Service == UsageService.Claude) _claudeCost += cost.Value;
            else if (entry.Service == UsageService.Agy) _agyCost += cost.Value;
            else _codexCost += cost.Value;
        }

        public UsagePeriodSummary ToSummary() => new(
            _input, _output, _claudeCost + _codexCost + _agyCost, _claudeCost, _codexCost,
            _hasUnknown, _agyCost);
    }

    private sealed class ModelAccumulator
    {
        public long Input { get; private set; }
        public long Cached { get; private set; }
        public long Output { get; private set; }
        public decimal Cost { get; private set; }
        public bool HasUnknownCost { get; private set; }

        public void Add(TokenUsageEntry entry, decimal? cost)
        {
            Input += entry.InputTokens + entry.CacheWrite5mTokens + entry.CacheWrite1hTokens;
            Cached += entry.CachedInputTokens;
            Output += entry.OutputTokens;
            if (cost is null) HasUnknownCost = true;
            else Cost += cost.Value;
        }
    }
}
