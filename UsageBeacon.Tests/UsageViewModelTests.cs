using UsageBeacon.Models;
using UsageBeacon.Providers;
using UsageBeacon.Services;
using UsageBeacon.ViewModels;

namespace UsageBeacon.Tests;

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

    private static UsageViewModel CreateViewModel(
        string directory,
        IUsageProvider claude,
        IUsageProvider? codex = null) => new(
        claude: claude,
        codex: codex ?? new StubUsageProvider(),
        settingsStore: new AppSettingsStore(Path.Combine(directory, "settings.json")),
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
