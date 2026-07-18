using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class ClaudePollingPolicyTests
{
    [Fact]
    public void ShouldFetch_DoesNotBypassServerCooldown_WhenForced()
    {
        var now = new DateTime(2026, 7, 18, 5, 0, 0, DateTimeKind.Utc);

        var result = ClaudePollingPolicy.ShouldFetch(
            now,
            now.AddMinutes(30),
            wasRateLimited: true,
            force: true);

        Assert.False(result);
    }

    [Fact]
    public void ShouldFetch_BypassesOrdinaryMinimumInterval_WhenForced()
    {
        var now = new DateTime(2026, 7, 18, 5, 0, 0, DateTimeKind.Utc);

        var result = ClaudePollingPolicy.ShouldFetch(
            now,
            now.AddMinutes(30),
            wasRateLimited: false,
            force: true);

        Assert.True(result);
    }

    [Fact]
    public void NextRequestAfterRateLimit_PrefersLongerServerDelay()
    {
        var now = new DateTime(2026, 7, 18, 5, 0, 0, DateTimeKind.Utc);

        var result = ClaudePollingPolicy.NextRequestAfterRateLimit(
            now,
            retryAfterSeconds: 3600,
            minimumInterval: TimeSpan.FromMinutes(30));

        Assert.Equal(now.AddHours(1), result);
    }
}
