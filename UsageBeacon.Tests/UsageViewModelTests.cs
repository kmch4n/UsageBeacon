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

    private static UsageViewModel CreateViewModel(
        string directory,
        IUsageProvider claude,
        IUsageProvider? codex = null) => new(
        claude: claude,
        codex: codex ?? new StubUsageProvider(),
        settingsStore: new AppSettingsStore(Path.Combine(directory, "settings.json")),
        startupManager: new FakeStartupManager(),
        dataDirectory: directory);

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
