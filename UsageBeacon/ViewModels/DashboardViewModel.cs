using System.IO;
using UsageBeacon.Models.Insights;
using UsageBeacon.Services.Insights;

namespace UsageBeacon.ViewModels;

/// <summary>
/// Orchestrates the dashboard scan: enumerates both log directories,
/// resolves entries through the incremental cache, and aggregates the
/// result. The scan runs on a background thread; only the returned data is
/// touched by the UI. Directory and cache paths are injectable for tests.
/// </summary>
public sealed class DashboardViewModel
{
    private readonly string _claudeProjectsDirectory;
    private readonly string _codexSessionsDirectory;
    private readonly string _cachePath;
    private readonly ModelPricingCatalog _pricing;
    private readonly TimeZoneInfo _timeZone;

    public DashboardViewModel(
        ModelPricingCatalog pricing,
        string? claudeProjectsDirectory = null,
        string? codexSessionsDirectory = null,
        string? cachePath = null,
        TimeZoneInfo? timeZone = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _claudeProjectsDirectory = claudeProjectsDirectory
            ?? Path.Combine(home, ".claude", "projects");
        _codexSessionsDirectory = codexSessionsDirectory
            ?? Path.Combine(home, ".codex", "sessions");
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsageBeacon",
            "insights-cache.json");
        _pricing = pricing;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public string PricesAsOf => _pricing.AsOf;

    /// <summary>True when neither log directory exists on this machine.</summary>
    public bool HasAnyLogDirectory
        => Directory.Exists(_claudeProjectsDirectory) || Directory.Exists(_codexSessionsDirectory);

    public Task<DashboardData> LoadAsync(CancellationToken cancellationToken)
        => Task.Run(() => Scan(cancellationToken), cancellationToken);

    private DashboardData Scan(CancellationToken cancellationToken)
    {
        var cache = UsageLogCache.Load(_cachePath);

        ScanDirectory(cache, _claudeProjectsDirectory, ClaudeTranscriptReader.ParseFile, cancellationToken);
        ScanDirectory(cache, _codexSessionsDirectory, CodexSessionReader.ParseFile, cancellationToken);

        cache.Save();

        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));
        return UsageAggregator.Aggregate(cache.AllEntries(), _pricing, today, _timeZone);
    }

    private static void ScanDirectory(
        UsageLogCache cache,
        string directory,
        Func<string, IReadOnlyList<TokenUsageEntry>> parser,
        CancellationToken cancellationToken)
    {
        foreach (var path in EnumerateLogs(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                cache.GetEntries(path, info.Length, info.LastWriteTimeUtc, parser);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file locked by a running CLI or deleted mid-scan must not
                // discard this scan's other results; its cached entries (if
                // any) still contribute through AllEntries().
            }
        }
    }

    private static IEnumerable<string> EnumerateLogs(string directory)
    {
        if (!Directory.Exists(directory)) yield break;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var file in files) yield return file;
    }
}
