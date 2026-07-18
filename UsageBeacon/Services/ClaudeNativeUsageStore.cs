using System.IO;
using System.Text.Json;
using UsageBeacon.Models;

namespace UsageBeacon.Services;

public static class ClaudeNativeUsageStore
{
    public static string FilePath { get; } = Path.Combine(
        AppDataPaths.DirectoryPath,
        "claude-native-usage.json");

    public static UsageCacheEntry? Load(string? path = null)
    {
        try
        {
            var targetPath = path ?? FilePath;
            if (!File.Exists(targetPath)) return null;
            var entry = JsonSerializer.Deserialize<UsageCacheEntry>(
                File.ReadAllText(targetPath));
            if (entry?.Usage == null || entry.Source != UsageDataSource.ClaudeCodeStatusLine)
                return null;
            var nowUtc = DateTime.UtcNow;
            if (entry.FetchedAtUtc <= DateTime.MinValue ||
                entry.FetchedAtUtc > nowUtc.AddMinutes(5) ||
                !IsValid(entry.Usage))
                return null;
            return entry;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValid(ServiceUsage usage) =>
        IsValid(usage.FiveHour) &&
        IsValid(usage.Weekly) &&
        IsValid(usage.WeeklySonnet) &&
        (usage.FiveHour != null || usage.Weekly != null || usage.WeeklySonnet != null);

    private static bool IsValid(RateLimit? limit) =>
        limit == null ||
        (!double.IsNaN(limit.Utilization) &&
         limit.Utilization is >= 0 and <= 1);
}
