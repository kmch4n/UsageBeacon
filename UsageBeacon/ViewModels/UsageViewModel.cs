using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
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

    private UsageSnapshot _snapshot = UsageSnapshot.Empty;
    private bool _isLoading;
    private PollingInterval _pollingInterval;
    private WidgetPlacement _widgetPlacement;
    private PopupTransparency _popupTransparency;
    private string? _monitorDeviceName;
    private bool _startupEnabled;
    private bool _loginPrompted;
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

    /// <summary>初回起動時のログイン自動案内を一度だけ出すためのフラグ。</summary>
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

    // ── Init ─────────────────────────────────────────────────────────────

    public UsageViewModel(IUsageProvider? claude = null, IUsageProvider? codex = null)
    {
        _claude          = claude ?? new ClaudeUsageProvider();
        _codex           = codex  ?? new CodexUsageProvider();
        _pollingInterval = LoadSettings();
        _widgetPlacement = LoadWidgetPlacement();
        _popupTransparency = LoadPopupTransparency();
        _monitorDeviceName = LoadMonitorDeviceName();
        _loginPrompted   = LoadLoginPrompted();
        try { StartupManager.MigrateLegacyRegistration(); } catch { }
        _startupEnabled  = StartupManager.IsEnabled;
        var claudeUsageCache = LoadClaudeUsageCache();
        var nativeUsageCache = ClaudeNativeUsageStore.Load();
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
        await RefreshAsync(ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollingInterval.ToTimeSpan(), ct);
                await RefreshAsync(ct);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default, bool force = false)
    {
        // ポーリングと手動更新が同時に走り _snapshot / _lastClaudeUsage を競合させないよう直列化する。
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
            var nativeUsage = ClaudeNativeUsageStore.Load();
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
            var fetchClaude = !hasFreshNativeUsage && ClaudePollingPolicy.ShouldFetch(
                nowUtc,
                _claudeCooldownUntilUtc,
                _claudeWaitingAfterRateLimit,
                force);
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
            // 一時的なエラー時は直前に取得できた値を表示し続ける（常に数字を出すため）。
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

    // 直前値の表示継続が妥当な「一時的」エラーか。
    // 未ログイン・CLI未検出は古い数字を出すと誤解を招くため対象外。
    // CodexUnauthorized は再ログイン中も直前値を表示し続けるため一時的扱い。
    private static bool IsTransient(DomainErrorKind kind) => kind is not (
        DomainErrorKind.TokenMissing or
        DomainErrorKind.AnthropicUnauthorized or
        DomainErrorKind.CodexNotFound);

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Settings (JSON file in %AppData%\UsageBeacon) ───────────────────

    private static readonly string SettingsPath = Path.Combine(
        AppDataPaths.DirectoryPath, "settings.json");
    private static readonly string ClaudeUsageCachePath = Path.Combine(
        AppDataPaths.DirectoryPath, "claude-usage-cache.json");
    private static readonly string CodexUsageCachePath = Path.Combine(
        AppDataPaths.DirectoryPath, "codex-usage-cache.json");
    private static readonly string ClaudePollingStatePath = Path.Combine(
        AppDataPaths.DirectoryPath, "claude-polling-state.json");

    // 一時ファイルに書いてから置換することで、書き込み途中の電源断による 0 バイト破損を避ける。
    private static void AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    private static PollingInterval LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return PollingIntervalExtensions.Default;
            var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (doc.RootElement.TryGetProperty("pollingInterval", out var el) &&
                el.TryGetInt32(out var raw) &&
                Enum.IsDefined(typeof(PollingInterval), raw))
                return raw < (int)PollingInterval.Min2 ? PollingInterval.Min2 : (PollingInterval)raw;
        }
        catch { }
        return PollingIntervalExtensions.Default;
    }

    private static WidgetPlacement LoadWidgetPlacement()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return WidgetPlacement.Right;
            var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (doc.RootElement.TryGetProperty("widgetPlacement", out var el) &&
                Enum.TryParse<WidgetPlacement>(el.GetString(), out var placement))
                return placement;
        }
        catch { }
        return WidgetPlacement.Right;
    }

    private static PopupTransparency LoadPopupTransparency()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return PopupTransparencyExtensions.Default;
            var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (doc.RootElement.TryGetProperty("popupTransparency", out var el) &&
                Enum.TryParse<PopupTransparency>(el.GetString(), out var transparency))
                return transparency;
        }
        catch { }
        return PopupTransparencyExtensions.Default;
    }

    private static string? LoadMonitorDeviceName()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (doc.RootElement.TryGetProperty("monitorDeviceName", out var el))
                return el.GetString();
        }
        catch { }
        return null;
    }

    private static bool LoadLoginPrompted()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return false;
            var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (doc.RootElement.TryGetProperty("loginPrompted", out var el) &&
                el.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return el.GetBoolean();
        }
        catch { }
        return false;
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            AtomicWrite(SettingsPath,
                JsonSerializer.Serialize(new
                {
                    pollingInterval = (int)_pollingInterval,
                    widgetPlacement = _widgetPlacement.ToString(),
                    popupTransparency = _popupTransparency.ToString(),
                    monitorDeviceName = _monitorDeviceName,
                    loginPrompted = _loginPrompted,
                }));
        }
        catch { }
    }

    private static UsageCacheEntry? LoadClaudeUsageCache()
    {
        try
        {
            if (!File.Exists(ClaudeUsageCachePath)) return null;
            var json = File.ReadAllText(ClaudeUsageCachePath);
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

    private static void SaveClaudeUsageCache(UsageCacheEntry entry)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ClaudeUsageCachePath)!);
            AtomicWrite(ClaudeUsageCachePath, JsonSerializer.Serialize(entry));
        }
        catch { }
    }

    private static ServiceUsage? LoadCodexUsageCache()
    {
        try
        {
            return File.Exists(CodexUsageCachePath)
                ? JsonSerializer.Deserialize<ServiceUsage>(File.ReadAllText(CodexUsageCachePath))
                : null;
        }
        catch { return null; }
    }

    private static void SaveCodexUsageCache(ServiceUsage usage)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CodexUsageCachePath)!);
            AtomicWrite(CodexUsageCachePath, JsonSerializer.Serialize(usage));
        }
        catch { }
    }

    private static ClaudePollingState? LoadClaudePollingState()
    {
        try
        {
            return File.Exists(ClaudePollingStatePath)
                ? JsonSerializer.Deserialize<ClaudePollingState>(File.ReadAllText(ClaudePollingStatePath))
                : null;
        }
        catch { return null; }
    }

    private void SaveClaudePollingState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ClaudePollingStatePath)!);
            AtomicWrite(ClaudePollingStatePath, JsonSerializer.Serialize(
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
