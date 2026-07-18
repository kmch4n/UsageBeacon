namespace UsageBeacon.Services;

public static class ClaudePollingPolicy
{
    public static bool ShouldFetch(
        DateTime nowUtc,
        DateTime nextRequestUtc,
        bool wasRateLimited,
        bool force)
    {
        if (nowUtc >= nextRequestUtc) return true;
        return force && !wasRateLimited;
    }

    public static DateTime NextRequestAfterRateLimit(
        DateTime nowUtc,
        double? retryAfterSeconds,
        TimeSpan minimumInterval)
    {
        var serverDelay = retryAfterSeconds is > 0
            ? TimeSpan.FromSeconds(retryAfterSeconds.Value)
            : TimeSpan.Zero;
        return nowUtc.Add(serverDelay > minimumInterval ? serverDelay : minimumInterval);
    }
}
