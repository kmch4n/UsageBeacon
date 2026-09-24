namespace UsageBeacon.Services.Insights;

/// <summary>Rounded chart ceiling and descending tick values.</summary>
public sealed record DashboardChartScale(
    decimal Ceiling,
    decimal Step,
    IReadOnlyList<decimal> Ticks)
{
    public static DashboardChartScale Create(decimal maximum, decimal minimumStep = 0m)
    {
        if (maximum <= 0m)
            return new DashboardChartScale(0m, 0m, [0m]);

        var desired = maximum / 3m;
        var exponent = (int)Math.Floor(Math.Log10((double)desired));
        var factor = 1m;
        for (var i = 0; i < Math.Abs(exponent); i++)
            factor = exponent >= 0 ? factor * 10m : factor / 10m;

        var candidates = new[] { 1m, 2m, 2.5m, 5m, 10m };
        var step = candidates
            .Select(value => value * factor)
            .MinBy(value => Math.Abs(value - desired));
        step = Math.Max(step, minimumStep);
        var ceiling = decimal.Ceiling(maximum / step) * step;
        var ticks = new List<decimal>();
        for (var value = ceiling; value >= 0m; value -= step)
            ticks.Add(value);
        return new DashboardChartScale(ceiling, step, ticks);
    }
}
