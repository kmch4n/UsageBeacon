using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using UsageBeacon.Localization;
using UsageBeacon.Models;
using UsageBeacon.Models.Insights;
using UsageBeacon.Providers;
using UsageBeacon.Services;
using UsageBeacon.Services.Insights;
using UsageBeacon.ViewModels;
using UsageBeacon.Views;

namespace UsageBeacon.Tests;

public sealed class DashboardWindowTests
{
    private static readonly ModelPricingCatalog Pricing = new("2026-07-20",
        new Dictionary<string, ModelPricing>
        {
            ["claude-fable-5"] = new(10m, 1m, 12.5m, 20m, 50m),
            ["gpt-5.6-sol"] = new(5m, 0.5m, 0m, 0m, 30m),
        });

    [Fact]
    public void Refresh_ShowsRetainedRecentAndLifetimeUsage_WhenBothLogDirectoriesAreMissing()
    {
        using var directory = new TempDirectory();
        var cachePath = Path.Combine(directory.Path, "cache.json");
        var recent = DateTime.UtcNow;
        var old = recent.AddDays(-200);
        var cache = UsageLogCache.Load(cachePath);
        cache.GetEntries(Path.Combine(directory.Path, "missing-claude", "recent.jsonl"),
            1, recent, _ => [new TokenUsageEntry(1, recent, UsageService.Claude,
                "claude-fable-5", 1_000_000, 0, 0, 0, 0)]);
        cache.GetEntries(Path.Combine(directory.Path, "missing-codex", "old.jsonl"),
            1, old, _ => [new TokenUsageEntry(2, old, UsageService.Codex,
                "gpt-5.6-sol", 1_000_000, 0, 0, 0, 0)]);
        cache.ArchiveBefore(DateTime.UtcNow.AddDays(-UsageLogCache.RetentionDays));
        cache.Save();

        RunOnStaThread(async () =>
        {
            var vm = CreateViewModel(directory.Path, cachePath);
            Assert.False(vm.HasAnyLogDirectory);
            await using var settings = CreateSettings(directory.Path);
            var window = new DashboardWindow(settings, vm);
            try
            {
                Assert.NotNull(window.Icon);
                Assert.NotNull(window.TitleBarIcon.Source);
                await RefreshAsync(window);

                Assert.Equal(Visibility.Visible, window.ContentScroll.Visibility);
                Assert.Equal(Visibility.Collapsed, window.StatusText.Visibility);
                Assert.Equal("$15.00", window.LifetimeTotal.Text);
                Assert.Equal("$10.00", window.TodayCost.Text);
                Assert.Equal("$10.00", window.LifetimeClaudeCost.Text);
                Assert.Equal("$5.00", window.LifetimeCodexCost.Text);
                Assert.Equal("$10.00", window.TodayClaudeCost.Text);
                Assert.Equal("$0.00", window.TodayCodexCost.Text);
                Assert.Equal("Codex $5.00",
                    System.Windows.Automation.AutomationProperties.GetName(window.LifetimeCodexCost));
                Assert.IsAssignableFrom<System.Windows.Media.ImageSource>(window.Resources["ClaudeIcon"]);
                Assert.IsAssignableFrom<System.Windows.Media.ImageSource>(window.Resources["CodexIcon"]);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void CaptionButtons_UseWindowsGlyphs_AndSwapMaximizeForRestore()
    {
        using var directory = new TempDirectory();
        RunOnStaThread(async () =>
        {
            await using var settings = CreateSettings(directory.Path);
            var window = new DashboardWindow(settings, CreateViewModel(directory.Path,
                Path.Combine(directory.Path, "cache.json")));
            try
            {
                Assert.Equal("\uE921", window.MinimizeBtn.Content);
                Assert.Equal("\uE922", window.MaximizeBtn.Content);
                Assert.Equal("\uE8BB", window.CloseBtn.Content);
                Assert.Equal(LocalizationService.Get("DashboardClose"), window.CloseBtn.ToolTip);

                window.Show();
                window.WindowState = WindowState.Maximized;
                Assert.Equal("\uE923", window.MaximizeBtn.Content);
                window.WindowState = WindowState.Normal;
                Assert.Equal("\uE922", window.MaximizeBtn.Content);
                Assert.Equal(new Thickness(0), window.RootGrid.Margin);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void MaximizedWindowInsets_ConvertsOverhangToDips()
    {
        var insets = UsageBeacon.Utilities.MaximizedWindowInsets.Compute(
            new Int32Rect(-10, 50, 3860, 2110), new Int32Rect(0, 60, 3840, 2100), 1.5, 1.5);

        Assert.Equal(new Thickness(10 / 1.5, 10 / 1.5, 10 / 1.5, 0), insets);
    }

    [Fact]
    public void Refresh_ShowsEmptyState_WhenNoLogsOrCacheExist()
    {
        using var directory = new TempDirectory();
        RunOnStaThread(async () =>
        {
            await using var settings = CreateSettings(directory.Path);
            var window = new DashboardWindow(settings, CreateViewModel(directory.Path,
                Path.Combine(directory.Path, "cache.json")));
            try
            {
                await RefreshAsync(window);

                Assert.Equal(Visibility.Collapsed, window.ContentScroll.Visibility);
                Assert.Equal(Visibility.Visible, window.StatusText.Visibility);
                Assert.Equal(LocalizationService.Get("DashboardNoData"), window.StatusText.Text);
            }
            finally { window.Close(); }
        });
    }

    private static DashboardViewModel CreateViewModel(string root, string cachePath) => new(
        Pricing,
        claudeProjectsDirectory: Path.Combine(root, "missing-claude"),
        codexSessionsDirectory: Path.Combine(root, "missing-codex"),
        cachePath: cachePath,
        timeZone: TimeZoneInfo.Utc,
        agyBrainDirectory: Path.Combine(root, "missing-agy"));

    private static UsageViewModel CreateSettings(string root) => new(
        new StubUsageProvider(), new StubUsageProvider(),
        new StubSettingsStore(), new StubStartupManager(), root);

    private static async Task RefreshAsync(DashboardWindow window)
    {
        var method = typeof(DashboardWindow).GetMethod("RefreshDataAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(window, null)!;
    }

    private static void RunOnStaThread(Func<Task> action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(dispatcher));
                var task = action();
                if (!task.IsCompleted)
                {
                    var frame = new DispatcherFrame();
                    task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                }
                task.GetAwaiter().GetResult();
            }
            catch (Exception ex) { error = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class StubUsageProvider : IUsageProvider
    {
        public Task<ServiceUsage> FetchAsync(CancellationToken ct = default) =>
            Task.FromResult(new ServiceUsage(null, null, null));
    }

    private sealed class StubSettingsStore : IAppSettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }

    private sealed class StubStartupManager : IStartupManager
    {
        public bool IsEnabled { get; set; }
        public void MigrateLegacyRegistration() { }
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
