using UsageBeacon.Models.Insights;

namespace UsageBeacon.Services.Insights;

/// <summary>
/// Turns raw usage entries into the dashboard report: today / last 7 days /
/// last 30 days summaries, a 30-day daily series, and a per-model breakdown.
/// Days are local calendar days derived from the entry's UTC timestamp.
/// </summary>
public static class UsageAggregator
{
    public static DashboardData Aggregate(
        IEnumerable<KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>> files,
        ModelPricingCatalog pricing,
        DateOnly today,
        TimeZoneInfo timeZone)
    {
        // Deterministic order so cross-file duplicate ids always resolve the
        // same way regardless of directory enumeration order.
        var seen = new HashSet<long>();
        var entries = new List<TokenUsageEntry>();
        foreach (var pair in files.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var entry in pair.Value)
            {
                if (seen.Add(entry.IdHash)) entries.Add(entry);
            }
        }

        var last30Start = today.AddDays(-29);
        var last7Start = today.AddDays(-6);

        var todayTotals = new PeriodAccumulator();
        var weekTotals = new PeriodAccumulator();
        var monthTotals = new PeriodAccumulator();
        var dayCosts = new Dictionary<DateOnly, (decimal Claude, decimal Codex)>();
        var models = new Dictionary<(string Model, UsageService Service), ModelAccumulator>();
        var unknownModels = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var day = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(entry.TimestampUtc, timeZone));
            if (day < last30Start || day > today) continue;

            var cost = pricing.TryGetCost(entry);
            if (cost is null) unknownModels.Add(entry.Model);

            monthTotals.Add(entry, cost);
            if (day >= last7Start) weekTotals.Add(entry, cost);
            if (day == today) todayTotals.Add(entry, cost);

            var costs = dayCosts.TryGetValue(day, out var existing) ? existing : (0m, 0m);
            if (entry.Service == UsageService.Claude) costs.Item1 += cost ?? 0m;
            else costs.Item2 += cost ?? 0m;
            dayCosts[day] = costs;

            var key = (entry.Model, entry.Service);
            if (!models.TryGetValue(key, out var model))
                models[key] = model = new ModelAccumulator();
            model.Add(entry, cost);
        }

        var days = new List<DailyUsagePoint>(30);
        for (var day = last30Start; day <= today; day = day.AddDays(1))
        {
            var costs = dayCosts.TryGetValue(day, out var found) ? found : (0m, 0m);
            days.Add(new DailyUsagePoint(day, costs.Item1, costs.Item2));
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
            todayTotals.ToSummary(),
            weekTotals.ToSummary(),
            monthTotals.ToSummary(),
            days,
            breakdown,
            unknownModels.ToList());
    }

    private sealed class PeriodAccumulator
    {
        private long _input;
        private long _output;
        private decimal _claudeCost;
        private decimal _codexCost;
        private bool _hasUnknown;

        public void Add(TokenUsageEntry entry, decimal? cost)
        {
            _input += entry.InputTokens + entry.CachedInputTokens +
                      entry.CacheWrite5mTokens + entry.CacheWrite1hTokens;
            _output += entry.OutputTokens;
            if (cost is null) _hasUnknown = true;
            else if (entry.Service == UsageService.Claude) _claudeCost += cost.Value;
            else _codexCost += cost.Value;
        }

        public UsagePeriodSummary ToSummary() => new(
            _input, _output, _claudeCost + _codexCost, _claudeCost, _codexCost, _hasUnknown);
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
