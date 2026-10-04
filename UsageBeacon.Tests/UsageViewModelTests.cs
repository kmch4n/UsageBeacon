using UsageBeacon.Models;
using UsageBeacon.Providers;
using UsageBeacon.Services;
using UsageBeacon.Utilities;
using UsageBeacon.ViewModels;

namespace UsageBeacon.Tests;

// Shares a collection with ThemeServiceTests because the view model mutates
// the process-global ThemeService state.
[Collection("ThemeServiceState")]
public sealed class UsageViewModelTests
{
    [Fact]
    public async Task RefreshAsync_PreservesCallerCancellation_WithoutPublishingNetworkError()
    {
        using var directory = new TempDirectory();
        var claude = new CancelledUsageProvider();
        await using var vm = CreateViewModel(directory.Path, claude);
        using var cts = new CancellationTokenSource();

        var refresh = vm.RefreshAsync(cts.Token);
        await claude.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Equal(DateTime.MinValue, vm.Snapshot.FetchedAt);
        Assert.Null(vm.Snapshot.ClaudeError);
        Assert.False(vm.IsLoading);
    }

    private sealed class CancelledUsageProvider : IUsageProvider
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ServiceUsage> FetchAsync(CancellationToken ct = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    [Fact]
    public async Task RefreshAsync_ReleasesGateAfterBodyTimeout_AndPublishesNextResult()
    {
        using var directory = new TempDirectory();
        var requests = 0;
        using var http = ClaudeHttpResponseTests.CreateHttp(() => ++requests == 1
            ? new ClaudeHttpResponseTests.StalledContent()
            : new StringContent("""{"five_hour":{"utilization":25}}"""));
        await using var vm = CreateViewModel(directory.Path, new HttpUsageProvider(http));
        using var cleanup = new CancellationTokenSource();
        try
        {
            await vm.RefreshAsync(cleanup.Token).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(DomainErrorKind.Timeout, vm.Snapshot.ClaudeError?.Kind);
            Assert.NotNull(vm.Snapshot.CodexUsage);
            Assert.False(vm.IsLoading);

            await vm.RefreshAsync(cleanup.Token).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Null(vm.Snapshot.ClaudeError);
            Assert.Equal(0.25, vm.Snapshot.ClaudeUsage?.FiveHour?.Utilization);
        }
        finally { cleanup.Cancel(); }
    }

    private sealed class HttpUsageProvider(HttpClient http) : IUsageProvider
    {
        public async Task<ServiceUsage> FetchAsync(CancellationToken ct = default)
        {
            var cl = new AnthropicUsageApiClient(http);
            var usage = await cl.FetchAsync("test-access", ct);
            return new ServiceUsage(usage.FiveHour?.ToRateLimit(), null, null);
        }
    }

    [Fact]
    public async Task RunPollingLoopAsync_KeepsRunning_WhenSnapshotSubscriberThrows()
    {
        using var directory = new TempDirectory();
        var claude = new StubUsageProvider();
        var vm = CreateViewModel(directory.Path, claude);
        vm.SnapshotChanged += () => throw new InvalidOperationException("subscriber failure");
        using var cts = new CancellationTokenSource();

        var loop = vm.RunPollingLoopAsync(cts.Token);
        await WaitUntilAsync(() => claude.CallCount >= 1);
        cts.Cancel();

        // The subscriber exception is rethrown into the loop; it must neither
        // fault the polling task nor prevent the snapshot from being published.
        await loop;
        Assert.True(vm.Snapshot.FetchedAt > DateTime.MinValue);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task RefreshAsync_FetchesClaude_WhenCooldownActiveButNoCachedUsage()
    {
        using var directory = new TempDirectory();
        WritePollingState(directory.Path, DateTime.UtcNow.AddMinutes(20), wasRateLimited: false);
        var claude = new StubUsageProvider();
        var vm = CreateViewModel(directory.Path, claude);

        await vm.RefreshAsync();

        Assert.Equal(1, claude.CallCount);
        Assert.NotNull(vm.Snapshot.ClaudeUsage);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task RefreshAsync_HonorsRateLimitCooldown_WhenNoCachedUsage()
    {
        using var directory = new TempDirectory();
        WritePollingState(directory.Path, DateTime.UtcNow.AddMinutes(20), wasRateLimited: true);
        var claude = new StubUsageProvider();
        var vm = CreateViewModel(directory.Path, claude);

        await vm.RefreshAsync();

        Assert.Equal(0, claude.CallCount);
        // The user sees an explicit waiting state instead of endless loading.
        Assert.Equal(DomainErrorKind.AnthropicRateLimited, vm.Snapshot.ClaudeError?.Kind);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task RefreshAsync_SkipsClaude_WhenCooldownActiveAndCacheExists()
    {
        using var directory = new TempDirectory();
        WritePollingState(directory.Path, DateTime.UtcNow.AddMinutes(20), wasRateLimited: false);
        WriteClaudeUsageCache(directory.Path, utilization: 0.42);
        var claude = new StubUsageProvider();
        var vm = CreateViewModel(directory.Path, claude);

        await vm.RefreshAsync();

        Assert.Equal(0, claude.CallCount);
        Assert.Equal(0.42, vm.Snapshot.ClaudeUsage?.FiveHour?.Utilization);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task DashboardCurrency_Set_PersistsAcrossOtherSettingsChanges()
    {
        using var directory = new TempDirectory();
        var vm = CreateViewModel(directory.Path, new StubUsageProvider());
        try
        {
            vm.DashboardCurrency = "EUR";
            vm.PollingInterval = PollingInterval.Min10;

            var saved = new AppSettingsStore(
                Path.Combine(directory.Path, "settings.json")).Load();
            Assert.Equal("EUR", saved.DashboardCurrency);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [Fact]
    public async Task ShowWeeklyInWidget_PersistsAndRollsBackWhenSavingFails()
    {
        using var directory = new TempDirectory();
        var store = new StubSettingsStore();
        await using var vm = new UsageViewModel(
            new StubUsageProvider(), new StubUsageProvider(), store,
            new FakeStartupManager(), directory.Path);

        Assert.False(vm.ShowWeeklyInWidget);
        vm.ShowWeeklyInWidget = true;
        Assert.True(vm.ShowWeeklyInWidget);
        Assert.True(store.Saved?.ShowWeeklyInWidget);

        store.ThrowOnSave = true;
        vm.ShowWeeklyInWidget = false;
        Assert.True(vm.ShowWeeklyInWidget);
        Assert.Equal("SettingsSaveFailed", vm.SettingsErrorKey);
    }

    [Fact]
    public async Task AppTheme_Set_PersistsToSettingsAndAppliesTheme()
    {
        using var directory = new TempDirectory();
        ThemeService.SystemDarkOverride = () => false;
        var vm = CreateViewModel(directory.Path, new StubUsageProvider());

        try
        {
            vm.AppTheme = AppTheme.Dark;

            Assert.True(ThemeService.IsDark);
            var saved = new AppSettingsStore(
                Path.Combine(directory.Path, "settings.json")).Load();
            Assert.Equal("Dark", saved.AppTheme);
        }
        finally
        {
            ThemeService.SystemDarkOverride = null;
            ThemeService.SetTheme(AppTheme.System);
            await vm.DisposeAsync();
        }
    }

    [Fact]
    public async Task Constructor_LoadsPersistedAppTheme()
    {
        using var directory = new TempDirectory();
        ThemeService.SystemDarkOverride = () => false;
        File.WriteAllText(
            Path.Combine(directory.Path, "settings.json"),
            """{ "appTheme": "Dark" }""");
        var vm = CreateViewModel(directory.Path, new StubUsageProvider());

        try
        {
            Assert.Equal(AppTheme.Dark, vm.AppTheme);
            Assert.True(ThemeService.IsDark);
        }
        finally
        {
            ThemeService.SystemDarkOverride = null;
            ThemeService.SetTheme(AppTheme.System);
            await vm.DisposeAsync();
        }
    }

    [Fact]
    public async Task SettingChange_RollsBackAndReportsFailure_WhenSaveFails()
    {
        using var directory = new TempDirectory();
        var store = new StubSettingsStore { ThrowOnSave = true };
        var vm = new UsageViewModel(
            new StubUsageProvider(),
            new StubUsageProvider(),
            store,
            new FakeStartupManager(),
            directory.Path);

        vm.PollingInterval = PollingInterval.Min10;

        Assert.Equal(PollingInterval.Min5, vm.PollingInterval);
        Assert.Equal("SettingsSaveFailed", vm.SettingsErrorKey);

        store.ThrowOnSave = false;
        vm.PollingInterval = PollingInterval.Min10;

        Assert.Equal(PollingInterval.Min10, vm.PollingInterval);
        Assert.Null(vm.SettingsErrorKey);
        Assert.Equal(600, store.Saved?.PollingInterval);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task StartupChange_RollsBackAndReportsFailure_WhenRegistryWriteFails()
    {
        using var directory = new TempDirectory();
        var startup = new FakeStartupManager { ThrowOnSet = true };
        var vm = new UsageViewModel(
            new StubUsageProvider(),
            new StubUsageProvider(),
            new StubSettingsStore(),
            startup,
            directory.Path);

        vm.StartupEnabled = true;

        Assert.False(vm.StartupEnabled);
        Assert.Equal("SettingsStartupFailed", vm.SettingsErrorKey);

        startup.ThrowOnSet = false;
        vm.StartupEnabled = true;

        Assert.True(vm.StartupEnabled);
        Assert.True(startup.IsEnabled);
        Assert.Null(vm.SettingsErrorKey);
        await vm.DisposeAsync();
    }

    private static void WritePollingState(
        string directory,
        DateTime nextRequestUtc,
        bool wasRateLimited)
        => File.WriteAllText(
            Path.Combine(directory, "claude-polling-state.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                NextRequestUtc = nextRequestUtc,
                WasRateLimited = wasRateLimited,
            }));

    private static void WriteClaudeUsageCache(string directory, double utilization)
        => File.WriteAllText(
            Path.Combine(directory, "claude-usage-cache.json"),
            System.Text.Json.JsonSerializer.Serialize(new UsageCacheEntry(
                new ServiceUsage(
                    new RateLimit(utilization, DateTime.Now.AddHours(1)),
                    null,
                    null),
                DateTime.UtcNow.AddMinutes(-10),
                UsageDataSource.OAuthApi)));

    [Fact]
    public async Task RefreshAsync_DoesNotRunAgy_WhenAntigravityIsHidden()
    {
        using var directory = new TempDirectory();
        var agy = new SequenceUsageProvider();
        await using var vm = CreateViewModel(directory.Path, new StubUsageProvider(), agy: agy);

        await vm.RefreshAsync(force: true);

        Assert.Equal(0, agy.CallCount);
        Assert.Null(vm.Snapshot.AgyUsage);
        Assert.Null(vm.Snapshot.AgyError);
    }

    [Fact]
    public async Task ShowAgyUsage_PublishesGeminiQuota_AndPersistsSetting()
    {
        using var directory = new TempDirectory();
        var agy = new SequenceUsageProvider();
        await using var vm = CreateViewModel(directory.Path, new StubUsageProvider(), agy: agy);

        vm.ShowAgyUsage = true;

        await WaitUntilAsync(() => vm.Snapshot.AgyUsage != null);
        Assert.Equal(0.4, vm.Snapshot.AgyUsage?.FiveHour?.Utilization);
        Assert.True(new AppSettingsStore(Path.Combine(directory.Path, "settings.json")).Load().ShowAgyUsage);

        vm.ShowAgyUsage = false;

        Assert.Null(vm.Snapshot.AgyUsage);
    }

    [Fact]
    public async Task UsageAlertsEnabled_DefaultsOn_AndPersistsWhenTurnedOff()
    {
        using var directory = new TempDirectory();
        await using var vm = CreateViewModel(directory.Path, new StubUsageProvider());
        var store = new AppSettingsStore(Path.Combine(directory.Path, "settings.json"));

        Assert.True(vm.UsageAlertsEnabled);
        Assert.True(store.Load().UsageAlertsEnabled);

        vm.UsageAlertsEnabled = false;

        Assert.False(store.Load().UsageAlertsEnabled);
    }

    [Fact]
    public async Task RefreshAsync_KeepsLastAgyUsage_WhenReportTimesOut()
    {
        using var directory = new TempDirectory();
        var agy = new SequenceUsageProvider(null, DomainError.Timeout());
        await using var vm = CreateViewModel(directory.Path, new StubUsageProvider(), agy: agy);
        vm.ShowAgyUsage = true;
        await WaitUntilAsync(() => vm.Snapshot.AgyUsage != null && !vm.IsLoading);

        await vm.RefreshAsync(force: true);

        Assert.Equal(2, agy.CallCount);
        Assert.Equal(0.4, vm.Snapshot.AgyUsage?.FiveHour?.Utilization);
        Assert.Equal(DomainErrorKind.Timeout, vm.Snapshot.AgyError?.Kind);
    }

    [Fact]
    public async Task RefreshAsync_HidesLastAgyUsage_WhenCliIsUnsupported()
    {
        using var directory = new TempDirectory();
        var agy = new SequenceUsageProvider(null, DomainError.AgyUnsupportedVersion("1.1.10"));
        await using var vm = CreateViewModel(directory.Path, new StubUsageProvider(), agy: agy);
        vm.ShowAgyUsage = true;
        await WaitUntilAsync(() => vm.Snapshot.AgyUsage != null && !vm.IsLoading);

        await vm.RefreshAsync(force: true);

        Assert.Null(vm.Snapshot.AgyUsage);
        Assert.Equal(DomainErrorKind.AgyUnsupportedVersion, vm.Snapshot.AgyError?.Kind);
    }

    [Fact]
    public async Task RefreshAsync_SkipsAgy_WithinMinimumIntervalUnlessForced()
    {
        using var directory = new TempDirectory();
        var agy = new SequenceUsageProvider();
        await using var vm = CreateViewModel(directory.Path, new StubUsageProvider(), agy: agy);
        vm.ShowAgyUsage = true;
        await WaitUntilAsync(() => vm.Snapshot.AgyUsage != null && !vm.IsLoading);

        await vm.RefreshAsync();

        Assert.Equal(1, agy.CallCount);
        Assert.NotNull(vm.Snapshot.AgyUsage);
    }

    [Fact]
    public async Task Constructor_RestoresCachedAgyUsage_WhenAntigravityIsShown()
    {
        using var directory = new TempDirectory();
        var agy = new SequenceUsageProvider();
        await using (var first = CreateViewModel(directory.Path, new StubUsageProvider(), agy: agy))
        {
            first.ShowAgyUsage = true;
            await WaitUntilAsync(() => first.Snapshot.AgyUsage != null && !first.IsLoading);
        }

        await using var second = CreateViewModel(directory.Path, new StubUsageProvider(), agy: agy);

        Assert.Equal(0.4, second.Snapshot.AgyUsage?.FiveHour?.Utilization);
    }

    private static UsageViewModel CreateViewModel(
        string directory,
        IUsageProvider claude,
        IUsageProvider? codex = null,
        IUsageProvider? agy = null) => new(
        claude: claude,
        codex: codex ?? new StubUsageProvider(),
        settingsStore: new AppSettingsStore(Path.Combine(directory, "settings.json")),
        startupManager: new FakeStartupManager(),
        dataDirectory: directory,
        agy: agy ?? new SequenceUsageProvider());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class StubUsageProvider(DomainError? error = null) : IUsageProvider
    {
        public int CallCount { get; private set; }

        public Task<ServiceUsage> FetchAsync(CancellationToken ct = default)
        {
            CallCount++;
            if (error != null) throw error;
            return Task.FromResult(new ServiceUsage(
                FiveHour: new RateLimit(0.5, DateTime.Now.AddHours(2)),
                Weekly: null,
                WeeklySonnet: null));
        }
    }

    // Returns a Gemini quota for null entries and throws the given errors in order;
    // the last entry repeats once the sequence is exhausted.
    private sealed class SequenceUsageProvider(params DomainError?[] results) : IUsageProvider
    {
        public int CallCount { get; private set; }

        public Task<ServiceUsage> FetchAsync(CancellationToken ct = default)
        {
            var error = results.Length == 0 ? null : results[Math.Min(CallCount, results.Length - 1)];
            CallCount++;
            if (error != null) throw error;
            return Task.FromResult(new ServiceUsage(
                FiveHour: new RateLimit(0.4, DateTime.UtcNow.AddHours(2)),
                Weekly: new RateLimit(0.1, DateTime.UtcNow.AddDays(3)),
                WeeklySonnet: null));
        }
    }

    private sealed class StubSettingsStore : IAppSettingsStore
    {
        public bool ThrowOnSave { get; set; }

        public AppSettings? Saved { get; private set; }

        public AppSettings Load() => new();

        public void Save(AppSettings settings)
        {
            if (ThrowOnSave) throw new IOException("save failed");
            Saved = settings;
        }
    }

    private sealed class FakeStartupManager : IStartupManager
    {
        private bool _isEnabled;

        public bool ThrowOnSet { get; set; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (ThrowOnSet) throw new UnauthorizedAccessException();
                _isEnabled = value;
            }
        }

        public void MigrateLegacyRegistration()
        {
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory =
            Directory.CreateTempSubdirectory("UsageBeaconTests-");

        public string Path => _directory.FullName;

        public void Dispose()
        {
            try { _directory.Delete(recursive: true); } catch { }
        }
    }
}
