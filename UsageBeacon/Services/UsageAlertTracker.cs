using System.IO;
using System.Text.Json;
using UsageBeacon.Localization;
using UsageBeacon.Models;

namespace UsageBeacon.Services;

public enum UsageAlertService { Claude, Codex, Agy }

public enum UsageAlertPeriod { FiveHour, Weekly }

public sealed record UsageAlert(
    UsageAlertService Service,
    UsageAlertPeriod Period,
    int Threshold,
    RateLimit Limit);

/// <summary>
/// Decides when a utilization threshold is first crossed in a limit window.
/// Each service and period fires at most once per threshold per window: the
/// window is identified by its reset time, so a later reset time re-arms the
/// thresholds. The state is persisted so restarting the app inside a window
/// does not repeat an alert.
/// </summary>
public sealed class UsageAlertTracker
{
    public static readonly IReadOnlyList<int> Thresholds = [80, 95];

    // Providers recompute reset times on every fetch; small drifts are the same window.
    private static readonly TimeSpan SameWindowTolerance = TimeSpan.FromMinutes(10);

    private readonly string? _statePath;
    private readonly Dictionary<string, WindowState> _windows;

    public UsageAlertTracker(string? statePath)
    {
        _statePath = statePath;
        _windows = Load(statePath);
    }

    /// <summary>
    /// Records the snapshot and returns the alerts it newly triggers, at most
    /// one (the highest threshold reached) per service and period.
    /// </summary>
    public IReadOnlyList<UsageAlert> Evaluate(UsageSnapshot snapshot, DateTime nowUtc)
    {
        var alerts = new List<UsageAlert>();
        var changed = false;
        Check(UsageAlertService.Claude, snapshot.ClaudeUsage);
        Check(UsageAlertService.Codex, snapshot.CodexUsage);
        Check(UsageAlertService.Agy, snapshot.AgyUsage);
        if (changed) Save();
        return alerts;

        void Check(UsageAlertService service, ServiceUsage? usage)
        {
            CheckLimit(service, UsageAlertPeriod.FiveHour, usage?.FiveHour);
            CheckLimit(service, UsageAlertPeriod.Weekly, usage?.Weekly);
        }

        void CheckLimit(UsageAlertService service, UsageAlertPeriod period, RateLimit? limit)
        {
            // A missing or past reset time means the value no longer describes a live window.
            if (limit is null || limit.ResetsAt == DateTime.MinValue) return;
            var resetsAtUtc = LocalizedText.ToUtc(limit.ResetsAt);
            if (resetsAtUtc <= nowUtc) return;

            var key = $"{service}.{period}";
            var level = _windows.TryGetValue(key, out var known) &&
                        (resetsAtUtc - known.ResetsAtUtc).Duration() <= SameWindowTolerance
                ? known.Level
                : 0;
            var reached = Thresholds.Where(threshold => limit.Percent >= threshold)
                .DefaultIfEmpty(0)
                .Max();
            if (reached > level)
            {
                alerts.Add(new UsageAlert(service, period, reached, limit));
                level = reached;
            }

            if (known is null || known.Level != level || known.ResetsAtUtc != resetsAtUtc)
            {
                _windows[key] = new WindowState { ResetsAtUtc = resetsAtUtc, Level = level };
                changed = true;
            }
        }
    }

    private static Dictionary<string, WindowState> Load(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path) &&
                JsonSerializer.Deserialize<Dictionary<string, WindowState>>(
                    File.ReadAllText(path)) is { } state)
                return state;
        }
        catch
        {
            // A corrupt state file only risks repeating an alert once.
        }
        return new Dictionary<string, WindowState>(StringComparer.Ordinal);
    }

    private void Save()
    {
        if (_statePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            var tmp = _statePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_windows));
            File.Move(tmp, _statePath, overwrite: true);
        }
        catch
        {
        }
    }

    private sealed class WindowState
    {
        public DateTime ResetsAtUtc { get; init; }
        public int Level { get; init; }
    }
}
