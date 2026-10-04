using UsageBeacon.Models;
using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class UsageAlertTrackerTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowEnd = Now.AddHours(2);

    [Fact]
    public void Evaluate_FiresEachThresholdOncePerWindow()
    {
        var tracker = new UsageAlertTracker(statePath: null);

        Assert.Empty(tracker.Evaluate(Claude(0.79, WindowEnd), Now));

        var first = Assert.Single(tracker.Evaluate(Claude(0.81, WindowEnd), Now));
        Assert.Equal((UsageAlertService.Claude, UsageAlertPeriod.FiveHour, 80),
            (first.Service, first.Period, first.Threshold));
        Assert.Empty(tracker.Evaluate(Claude(0.90, WindowEnd), Now));

        Assert.Equal(95, Assert.Single(tracker.Evaluate(Claude(0.96, WindowEnd), Now)).Threshold);
        Assert.Empty(tracker.Evaluate(Claude(1.0, WindowEnd), Now));
    }

    [Fact]
    public void Evaluate_ReportsOnlyTheHighestThreshold_WhenSeveralAreCrossedAtOnce()
    {
        var tracker = new UsageAlertTracker(statePath: null);

        Assert.Equal(95, Assert.Single(tracker.Evaluate(Claude(0.97, WindowEnd), Now)).Threshold);
    }

    [Fact]
    public void Evaluate_RearmsWhenTheWindowResets_ButToleratesResetTimeDrift()
    {
        var tracker = new UsageAlertTracker(statePath: null);
        Assert.Single(tracker.Evaluate(Claude(0.85, WindowEnd), Now));

        Assert.Empty(tracker.Evaluate(Claude(0.86, WindowEnd.AddMinutes(3)), Now));

        var nextWindow = WindowEnd.AddHours(5);
        Assert.Empty(tracker.Evaluate(Claude(0.10, nextWindow), Now.AddHours(3)));
        Assert.Single(tracker.Evaluate(Claude(0.82, nextWindow), Now.AddHours(4)));
    }

    [Fact]
    public void Evaluate_IgnoresLimitsWithoutAFutureResetTime()
    {
        var tracker = new UsageAlertTracker(statePath: null);

        Assert.Empty(tracker.Evaluate(Claude(0.99, DateTime.MinValue), Now));
        Assert.Empty(tracker.Evaluate(Claude(0.99, Now.AddMinutes(-1)), Now));
    }

    [Fact]
    public void Evaluate_TracksServicesAndPeriodsIndependently()
    {
        var tracker = new UsageAlertTracker(statePath: null);
        var limit = new RateLimit(0.9, WindowEnd);
        var snapshot = new UsageSnapshot
        {
            ClaudeUsage = new ServiceUsage(limit, new RateLimit(0.5, Now.AddDays(3)), null),
            CodexUsage = new ServiceUsage(null, new RateLimit(0.8, Now.AddDays(3)), null),
            AgyUsage = new ServiceUsage(limit, null, null),
        };

        var alerts = tracker.Evaluate(snapshot, Now);

        Assert.Equal(
            [
                (UsageAlertService.Claude, UsageAlertPeriod.FiveHour),
                (UsageAlertService.Codex, UsageAlertPeriod.Weekly),
                (UsageAlertService.Agy, UsageAlertPeriod.FiveHour),
            ],
            alerts.Select(alert => (alert.Service, alert.Period)));
    }

    [Fact]
    public void Evaluate_DoesNotRepeatAlertsAfterRestart()
    {
        var directory = Directory.CreateTempSubdirectory("UsageBeaconAlertTests-");
        try
        {
            var path = Path.Combine(directory.FullName, "usage-alert-state.json");
            Assert.Single(new UsageAlertTracker(path).Evaluate(Claude(0.85, WindowEnd), Now));

            var restarted = new UsageAlertTracker(path);
            Assert.Empty(restarted.Evaluate(Claude(0.85, WindowEnd), Now));
            Assert.Single(restarted.Evaluate(Claude(0.95, WindowEnd), Now));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Constructor_StartsFresh_WhenTheStateFileIsCorrupt()
    {
        var directory = Directory.CreateTempSubdirectory("UsageBeaconAlertTests-");
        try
        {
            var path = Path.Combine(directory.FullName, "usage-alert-state.json");
            File.WriteAllText(path, "[not json");

            Assert.Single(new UsageAlertTracker(path).Evaluate(Claude(0.85, WindowEnd), Now));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static UsageSnapshot Claude(double utilization, DateTime resetsAt)
        => new() { ClaudeUsage = new ServiceUsage(new RateLimit(utilization, resetsAt), null, null) };
}
