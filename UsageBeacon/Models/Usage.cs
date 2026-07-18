namespace UsageBeacon.Models;

public sealed record RateLimit(double Utilization, DateTime ResetsAt)
{
    public int Percent => (int)Math.Round(Utilization * 100);
}

public sealed record ServiceUsage(
    RateLimit? FiveHour,
    RateLimit? Weekly,
    RateLimit? WeeklySonnet);

public enum UsageDataSource
{
    OAuthApi,
    ClaudeCodeStatusLine,
}

public sealed record UsageCacheEntry(
    ServiceUsage Usage,
    DateTime FetchedAtUtc,
    UsageDataSource Source);

public sealed class UsageSnapshot
{
    public ServiceUsage? ClaudeUsage { get; init; }
    public DomainError?  ClaudeError { get; init; }
    public DateTime?     ClaudeFetchedAtUtc { get; init; }
    public UsageDataSource? ClaudeSource { get; init; }
    public ServiceUsage? CodexUsage  { get; init; }
    public DomainError?  CodexError  { get; init; }
    public DateTime      FetchedAt   { get; init; }

    public static readonly UsageSnapshot Empty = new() { FetchedAt = DateTime.MinValue };
}
