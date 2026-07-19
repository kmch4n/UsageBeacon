using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using UsageBeacon.Localization;
using UsageBeacon.Models;
using UsageBeacon.Providers;
using UsageBeacon.Services;
using UsageBeacon.Utilities;

namespace UsageBeacon.ViewModels;

public sealed class UsageViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly TimeSpan ClaudeMinimumInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ClaudeNativeUsageFreshness = TimeSpan.FromMinutes(30);

    private readonly IUsageProvider _claude;
    private readonly IUsageProvider _codex;
    private readonly AppSettingsStore _settingsStore;
    private readonly string _claudeUsageCachePath;
    private readonly string _codexUsageCachePath;
    private readonly string _claudePollingStatePath;
    private readonly string _claudeNativeUsagePath;

    private UsageSnapshot _snapshot = UsageSnapshot.Empty;
    private bool _isLoading;
    private PollingInterval _pollingInterval;
    private WidgetPlacement _widgetPlacement;
    private PopupTransparency _popupTransparency;
    private string? _monitorDeviceName;
    private bool _startupEnabled;
    private bool _loginPrompted;
    private string _uiLanguage;
    private DateTime _claudeCooldownUntilUtc;
    private ServiceUsage? _lastClaudeUsage;
    private DateTime? _lastClaudeFetchedAtUtc;
    private UsageDataSource? _lastClaudeSource;
    private ServiceUsage? _lastCodexUsage;
    private bool _claudeWaitingAfterRateLimit;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? SnapshotChanged;

    // ── Properties ───────────────────────────────────────────────────────

    public UsageSnapshot Snapshot
    {
        get => _snapshot;
        private set { _snapshot = value; Notify(); SnapshotChanged?.Invoke(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; Notify(); }
    }

    public PollingInterval PollingInterval
    {
        get => _pollingInterval;
        set { _pollingInterval = value; Notify(); SaveSettings(); }
    }

    public WidgetPlacement WidgetPlacement
    {
        get => _widgetPlacement;
        set
        {
            if (_widgetPlacement == value) return;
            _widgetPlacement = value;
            Notify();
            SaveSettings();
        }
    }

    public PopupTransparency PopupTransparency
    {
        get => _popupTransparency;
        set
        {
            if (_popupTransparency == value) return;
            _popupTransparency = value;
            Notify();
            SaveSettings();
        }
    }

    public string? MonitorDeviceName
    {
        get => _monitorDeviceName;
        set
        {
            if (_monitorDeviceName == value) return;
            _monitorDeviceName = value;
            Notify();
            SaveSettings();
        }
    }

    public bool StartupEnabled
    {
        get => _startupEnabled;
        set
        {
            _startupEnabled = value;
            Notify();
            try { StartupManager.IsEnabled = value; } catch { }
        }
    }

    /// <summary>Tracks whether the first-run sign-in prompt has been shown.</summary>
    public bool LoginPrompted
    {
        get => _loginPrompted;
        set
        {
            if (_loginPrompted == value) return;
            _loginPrompted = value;
            SaveSettings();
        }
    }

    public DateTime? ClaudeNextRetryAt =>
        Snapshot.ClaudeError?.Kind == DomainErrorKind.AnthropicRateLimited &&
        _claudeCooldownUntilUtc > DateTime.UtcNow
            ? _claudeCooldownUntilUtc.ToLocalTime()
            : null;

    public PollingInterval[] AllIntervals { get; } = PollingIntervalExtensions.All;

    public string UiLanguage
    {
        get => _uiLanguage;
        set
        {
            var normalized = LocalizationService.NormalizePreference(value);
            if (_uiLanguage == normalized) return;
            _uiLanguage = normalized;
            LocalizationService.SetLanguage(normalized);
            Notify();
            SaveSettings();
        }
    }

    // ── Init ─────────────────────────────────────────────────────────────

    public UsageViewModel(
        IUsageProvider? claude = null,
        IUsageProvider? codex = null,
        AppSettingsStore? settingsStore = null,
        string? dataDirectory = null)
    {
        var directory = dataDirectory ?? AppDataPaths.DirectoryPath;
        _claudeUsageCachePath = Path.Combine(directory, "claude-usage-cache.json");
        _codexUsageCachePath = Path.Combine(directory, "codex-usage-cache.json");
        _claudePollingStatePath = Path.Combine(directory, "claude-polling-state.json");
        _claudeNativeUsagePath = Path.Combine(directory, "claude-native-usage.json");
        _settingsStore = settingsStore ?? new AppSettingsStore();
        var settings = _settingsStore.Load();
        _claude          = claude ?? new ClaudeUsageProvider();
        _codex           = codex  ?? new CodexUsageProvider();
        _pollingInterval = ParsePollingInterval(settings.PollingInterval);
        _widgetPlacement = ParseWidgetPlacement(settings.WidgetPlacement);
        _popupTransparency = ParsePopupTransparency(settings.PopupTransparency);
        _monitorDeviceName = settings.MonitorDeviceName;
        _loginPrompted = settings.LoginPrompted;
        _uiLanguage = LocalizationService.NormalizePreference(settings.UiLanguage);
        LocalizationService.SetLanguage(_uiLanguage);
        try { StartupManager.MigrateLegacyRegistration(); } catch { }
        _startupEnabled  = StartupManager.IsEnabled;
        var claudeUsageCache = LoadClaudeUsageCache();
        var nativeUsageCache = ClaudeNativeUsageStore.Load(_claudeNativeUsagePath);
        var latestClaudeUsage = claudeUsageCache;
        if (nativeUsageCache != null &&
            (latestClaudeUsage == null ||
             nativeUsageCache.FetchedAtUtc > latestClaudeUsage.FetchedAtUtc))
            latestClaudeUsage = nativeUsageCache;
        _lastClaudeUsage = latestClaudeUsage?.Usage;
        _lastClaudeFetchedAtUtc = latestClaudeUsage?.FetchedAtUtc;
        _lastClaudeSource = latestClaudeUsage?.Source;
        _lastCodexUsage  = LoadCodexUsageCache();
        var claudePollingState = LoadClaudePollingState();
        _claudeCooldownUntilUtc = claudePollingState?.NextRequestUtc ?? DateTime.MinValue;
        _claudeWaitingAfterRateLimit = claudePollingState?.WasRateLimited == true;
        if (!_claudeWaitingAfterRateLimit &&
            _claudeCooldownUntilUtc > DateTime.UtcNow.Add(ClaudeMinimumInterval))
            _claudeCooldownUntilUtc = DateTime.UtcNow.Add(ClaudeMinimumInterval);
        var hasFreshNativeUsage =
            _lastClaudeSource == UsageDataSource.ClaudeCodeStatusLine &&
            _lastClaudeFetchedAtUtc > DateTime.UtcNow.Subtract(ClaudeNativeUsageFreshness);
        var waitingForClaudeRetry = !hasFreshNativeUsage &&
                                    _claudeWaitingAfterRateLimit &&
                                    _claudeCooldownUntilUtc > DateTime.UtcNow;
        if (_lastClaudeUsage != null || _lastCodexUsage != null || waitingForClaudeRetry)
        {
            _snapshot = new UsageSnapshot
            {
                ClaudeUsage = _lastClaudeUsage,
                ClaudeError = waitingForClaudeRetry ? DomainError.AnthropicRateLimited(null) : null,
                ClaudeFetchedAtUtc = _lastClaudeFetchedAtUtc,
                ClaudeSource = _lastClaudeSource,
                CodexUsage  = _lastCodexUsage,
                FetchedAt = DateTime.MinValue,
            };
        }
    }

    // ── Polling ──────────────────────────────────────────────────────────

    public async Task RunPollingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // SnapshotChanged and PropertyChanged subscribers marshal to the
                // UI thread with Dispatcher.Invoke, whose exceptions are
                // rethrown on this thread. A failing subscriber or any other
                // unexpected error must not stop future refreshes.
            }

            try
            {
                await Task.Delay(PollingInterval.ToTimeSpan(), ct);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default, bool force = false)
    {
        // Serialize automatic and manual refreshes so they cannot race shared state.
        await _refreshGate.WaitAsync(ct);
        try
        {
            if (force && _codex is CodexUsageProvider codex)
                codex.ResetBackoff();
            await RefreshCoreAsync(ct, force);
        }
        finally { _refreshGate.Release(); }
    }

    private async Task RefreshCoreAsync(CancellationToken ct, bool force)
    {
        IsLoading = true;
        try
        {
            var nowUtc = DateTime.UtcNow;
            var nativeUsage = ClaudeNativeUsageStore.Load(_claudeNativeUsagePath);
            if (nativeUsage != null &&
                (!_lastClaudeFetchedAtUtc.HasValue ||
                 nativeUsage.FetchedAtUtc > _lastClaudeFetchedAtUtc.Value))
            {
                _lastClaudeUsage = nativeUsage.Usage;
                _lastClaudeFetchedAtUtc = nativeUsage.FetchedAtUtc;
                _lastClaudeSource = nativeUsage.Source;
                SaveClaudeUsageCache(nativeUsage);
            }
            var hasFreshNativeUsage =
                _lastClaudeSource == UsageDataSource.ClaudeCodeStatusLine &&
                _lastClaudeFetchedAtUtc > nowUtc.Subtract(ClaudeNativeUsageFreshness);
            // With no cached usage the cooldown would leave the UI on an
            // indefinite loading state, so treat that case like a manual
            // refresh. Rate-limit cooldowns still win inside ShouldFetch.
            var fetchClaude = !hasFreshNativeUsage && ClaudePollingPolicy.ShouldFetch(
                nowUtc,
                _claudeCooldownUntilUtc,
                _claudeWaitingAfterRateLimit,
                force || _lastClaudeUsage == null);
            var claudeTask = fetchClaude
                ? FetchSafe(_claude, ct)
                : Task.FromResult((
                    _lastClaudeUsage,
                    hasFreshNativeUsage ? null : Snapshot.ClaudeError));
            var codexTask  = FetchSafe(_codex,  ct);
            await Task.WhenAll(claudeTask, codexTask);

            var (cu, ce) = await claudeTask;
            var (xu, xe) = await codexTask;
            if (fetchClaude)
            {
                if (cu != null)
                {
                    _claudeWaitingAfterRateLimit = false;
                    _claudeCooldownUntilUtc = nowUtc.Add(ClaudeMinimumInterval);
                    SaveClaudePollingState();
                }
                else if (ce?.Kind == DomainErrorKind.AnthropicRateLimited)
                {
                    _claudeWaitingAfterRateLimit = true;
                    _claudeCooldownUntilUtc = ClaudePollingPolicy.NextRequestAfterRateLimit(
                        nowUtc,
                        ce.RetryAfterSeconds,
                        ClaudeMinimumInterval);
                    SaveClaudePollingState();
                }
                else
                {
                    _claudeWaitingAfterRateLimit = false;
                    _claudeCooldownUntilUtc = DateTime.MinValue;
                    SaveClaudePollingState();
                }
            }
            // Keep the last successful value visible during transient failures.
            if (fetchClaude && cu != null)
            {
                _lastClaudeUsage = cu;
                _lastClaudeFetchedAtUtc = nowUtc;
                _lastClaudeSource = UsageDataSource.OAuthApi;
                SaveClaudeUsageCache(new UsageCacheEntry(
                    cu,
                    nowUtc,
                    UsageDataSource.OAuthApi));
            }
            else if (ce != null && _lastClaudeUsage != null && IsTransient(ce.Kind))
            {
                cu = _lastClaudeUsage;
            }

            if (xu != null)
            {
                _lastCodexUsage = xu;
                SaveCodexUsageCache(xu);
            }
            else if (xe != null && _lastCodexUsage != null && IsTransient(xe.Kind))
            {
                xu = _lastCodexUsage;
            }

            Snapshot = new UsageSnapshot
            {
                ClaudeUsage = cu, ClaudeError = ce,
                ClaudeFetchedAtUtc = _lastClaudeFetchedAtUtc,
                ClaudeSource = _lastClaudeSource,
                CodexUsage  = xu, CodexError  = xe,
                FetchedAt   = DateTime.Now,
            };
        }
        finally { IsLoading = false; }
    }

    // ── Internals ────────────────────────────────────────────────────────

    private static async Task<(ServiceUsage?, DomainError?)> FetchSafe(
        IUsageProvider provider, CancellationToken ct)
    {
        try   { return (await provider.FetchAsync(ct), null); }
        catch (DomainError e)  { return (null, e); }
        catch (Exception   e)  { return (null, DomainError.Network(e.Message)); }
    }

    // Missing credentials and a missing CLI must not show potentially misleading stale data.
    // An expired Codex sign-in remains transient so the last value stays visible while signing in.
    private static bool IsTransient(DomainErrorKind kind) => kind is not (
        DomainErrorKind.TokenMissing or
        DomainErrorKind.AnthropicUnauthorized or
        DomainErrorKind.CodexNotFound);

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Settings (JSON files in the data directory, %AppData%\UsageBeacon by default) ──

    // Replace through a temporary file to avoid leaving a zero-byte cache after an interrupted write.
    private static void AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    private static PollingInterval ParsePollingInterval(int raw)
        => Enum.IsDefined(typeof(PollingInterval), raw) && raw >= (int)PollingInterval.Min2
            ? (PollingInterval)raw
            : PollingIntervalExtensions.Default;

    private static WidgetPlacement ParseWidgetPlacement(string? value)
        => Enum.TryParse<WidgetPlacement>(value, out var placement)
            ? placement
            : WidgetPlacement.Right;

    private static PopupTransparency ParsePopupTransparency(string? value)
        => Enum.TryParse<PopupTransparency>(value, out var transparency)
            ? transparency
            : PopupTransparencyExtensions.Default;

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(new AppSettings
            {
                PollingInterval = (int)_pollingInterval,
                WidgetPlacement = _widgetPlacement.ToString(),
                PopupTransparency = _popupTransparency.ToString(),
                MonitorDeviceName = _monitorDeviceName,
                LoginPrompted = _loginPrompted,
                UiLanguage = _uiLanguage,
            });
        }
        catch { }
    }

    private UsageCacheEntry? LoadClaudeUsageCache()
    {
        try
        {
            if (!File.Exists(_claudeUsageCachePath)) return null;
            var json = File.ReadAllText(_claudeUsageCachePath);
            try
            {
                var entry = JsonSerializer.Deserialize<UsageCacheEntry>(json);
                if (entry?.Usage != null) return entry;
            }
            catch (JsonException) { }

            var legacyUsage = JsonSerializer.Deserialize<ServiceUsage>(json);
            return legacyUsage == null
                ? null
                : new UsageCacheEntry(
                    legacyUsage,
                    DateTime.MinValue,
                    UsageDataSource.OAuthApi);
        }
        catch { return null; }
    }

    private void SaveClaudeUsageCache(UsageCacheEntry entry)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_claudeUsageCachePath)!);
            AtomicWrite(_claudeUsageCachePath, JsonSerializer.Serialize(entry));
        }
        catch { }
    }

    private ServiceUsage? LoadCodexUsageCache()
    {
        try
        {
            return File.Exists(_codexUsageCachePath)
                ? JsonSerializer.Deserialize<ServiceUsage>(File.ReadAllText(_codexUsageCachePath))
                : null;
        }
        catch { return null; }
    }

    private void SaveCodexUsageCache(ServiceUsage usage)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_codexUsageCachePath)!);
            AtomicWrite(_codexUsageCachePath, JsonSerializer.Serialize(usage));
        }
        catch { }
    }

    private ClaudePollingState? LoadClaudePollingState()
    {
        try
        {
            return File.Exists(_claudePollingStatePath)
                ? JsonSerializer.Deserialize<ClaudePollingState>(File.ReadAllText(_claudePollingStatePath))
                : null;
        }
        catch { return null; }
    }

    private void SaveClaudePollingState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_claudePollingStatePath)!);
            AtomicWrite(_claudePollingStatePath, JsonSerializer.Serialize(
                new ClaudePollingState
                {
                    NextRequestUtc = _claudeCooldownUntilUtc,
                    WasRateLimited = _claudeWaitingAfterRateLimit,
                }));
        }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_codex is IAsyncDisposable d) await d.DisposeAsync();
    }

    private sealed class ClaudePollingState
    {
        public DateTime NextRequestUtc { get; init; }
        public bool WasRateLimited { get; init; }
    }
}
