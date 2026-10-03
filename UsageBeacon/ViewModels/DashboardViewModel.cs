using System.IO;
using UsageBeacon.Models.Insights;
using UsageBeacon.Services.Insights;
using UsageBeacon.Utilities;

namespace UsageBeacon.ViewModels;

/// <summary>
/// Orchestrates the dashboard scan: enumerates the Claude, Codex, and
/// Antigravity log directories,
/// resolves entries through the incremental cache, and aggregates the
/// result. The scan runs on a background thread; only the returned data is
/// touched by the UI. Directory and cache paths are injectable for tests.
/// </summary>
public sealed class DashboardViewModel
{
    private readonly string _claudeProjectsDirectory;
    private readonly string _codexSessionsDirectory;
    private readonly string _agyBrainDirectory;
    private readonly string _agySettingsPath;
    private readonly string _cachePath;
    private readonly ModelPricingCatalog _pricing;
    private readonly TimeZoneInfo _timeZone;

    public DashboardViewModel(
        ModelPricingCatalog pricing,
        string? claudeProjectsDirectory = null,
        string? codexSessionsDirectory = null,
        string? cachePath = null,
        TimeZoneInfo? timeZone = null,
        string? agyBrainDirectory = null,
        string? agySettingsPath = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _claudeProjectsDirectory = claudeProjectsDirectory
            ?? Path.Combine(home, ".claude", "projects");
        _codexSessionsDirectory = codexSessionsDirectory
            ?? Path.Combine(home, ".codex", "sessions");
        _agyBrainDirectory = agyBrainDirectory
            ?? Path.Combine(home, ".gemini", "antigravity-cli", "brain");
        _agySettingsPath = agySettingsPath
            ?? Path.Combine(home, ".gemini", "antigravity-cli", "settings.json");
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsageBeacon",
            "insights-cache.json");
        _pricing = pricing;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public string PricesAsOf => _pricing.AsOf;

    /// <summary>True when no supported log directory exists on this machine.</summary>
    public bool HasAnyLogDirectory
        => Directory.Exists(_claudeProjectsDirectory) ||
           Directory.Exists(_codexSessionsDirectory) ||
           Directory.Exists(_agyBrainDirectory);

    public Task<DashboardData> LoadAsync(CancellationToken cancellationToken)
        => Task.Run(() => Scan(cancellationToken), cancellationToken);

    private DashboardData Scan(CancellationToken cancellationToken)
    {
        var cache = UsageLogCache.Load(_cachePath);

        ScanDirectory(
            cache,
            _claudeProjectsDirectory,
            ClaudeTranscriptReader.ParseFile,
            cancellationToken,
            ClaudeTranscriptReader.ParserRevision);
        ScanDirectory(
            cache,
            _codexSessionsDirectory,
            CodexSessionReader.ParseFile,
            cancellationToken,
            CodexSessionReader.ParserRevision);
        var agyDefaultModel = ReadAgyDefaultModel(_agySettingsPath);
        ScanDirectory(
            cache,
            _agyBrainDirectory,
            path => AgyTranscriptReader.ParseFile(path, agyDefaultModel),
            cancellationToken,
            AgyTranscriptReader.ParserRevision,
            AgyTranscriptReader.FileName);

        // Archive older details after scanning so lifetime costs remain
        // repricable without retaining their original file paths.
        cache.ArchiveBefore(DateTime.UtcNow.AddDays(-UsageLogCache.RetentionDays));
        cache.Save();

        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));
        return UsageAggregator.Aggregate(
            cache.AllEntries(),
            _pricing,
            today,
            _timeZone,
            cache.ArchivedUsage);
    }

    private static void ScanDirectory(
        UsageLogCache cache,
        string directory,
        Func<string, IReadOnlyList<TokenUsageEntry>> parser,
        CancellationToken cancellationToken,
        int parserRevision = 0,
        string searchPattern = "*.jsonl")
    {
        foreach (var path in EnumerateLogs(directory, searchPattern))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                cache.GetEntries(
                    path,
                    info.Length,
                    info.LastWriteTimeUtc,
                    parser,
                    parserRevision);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file locked by a running CLI or deleted mid-scan must not
                // discard this scan's other results; its cached entries (if
                // any) still contribute through AllEntries().
            }
        }
    }

    // The SearchOption overload of EnumerateFiles uses EnumerationOptions.Compatible,
    // which sets IgnoreInaccessible to false; an unreadable directory then aborts the
    // whole traversal from MoveNext instead of being skipped.
    private static readonly EnumerationOptions LogEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible    = true,
    };

    private static IEnumerable<string> EnumerateLogs(string directory, string searchPattern)
    {
        if (!Directory.Exists(directory)) return Array.Empty<string>();

        // IgnoreInaccessible covers access failures; the guard covers the rest
        // (a disconnected share, a path that grew too long) so one unreadable
        // corner cannot discard the entries already collected in this scan.
        return ResilientFileEnumeration.IgnoringFileSystemErrors(
            () => Directory.EnumerateFiles(directory, searchPattern, LogEnumerationOptions));
    }

    // Conversations without a model-selection notice ran on the configured
    // model. The current setting is the best available approximation.
    internal static string? ReadAgyDefaultModel(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath)) return null;
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath));
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("model", out var model) &&
                   model.ValueKind == System.Text.Json.JsonValueKind.String
                ? model.GetString()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
