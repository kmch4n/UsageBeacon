using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using UsageBeacon.Models;
using UsageBeacon.Providers;
using UsageBeacon.Services;
using UsageBeacon.ViewModels;
using UsageBeacon.Views;
using WpfButton = System.Windows.Controls.Button;

namespace UsageBeacon.Tests;

public sealed class TaskbarWidgetTests
{
    [Fact]
    public void ToggleControl_ExposesButtonInvokeSemanticsAndAccessibleName()
    {
        RunOnStaThread(() =>
        {
            var directory = Directory.CreateTempSubdirectory("UsageBeaconTests-");
            var vm = new UsageViewModel(
                new StubUsageProvider(),
                new StubUsageProvider(),
                new StubSettingsStore(),
                new StubStartupManager(),
                directory.FullName);
            var widget = new TaskbarWidget(vm);
            try
            {
                var button = Assert.IsType<WpfButton>(widget.ToggleButton);
                var peer = new ButtonAutomationPeer(button);

                Assert.NotNull(peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));

                var toggled = false;
                widget.PopupToggleRequested += () => toggled = true;
                button.RaiseEvent(new RoutedEventArgs(WpfButton.ClickEvent));
                Assert.True(toggled);
            }
            finally
            {
                widget.Close();
                try { directory.Delete(recursive: true); } catch { }
            }
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
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

        public void Save(AppSettings settings)
        {
        }
    }

    private sealed class StubStartupManager : IStartupManager
    {
        public bool IsEnabled { get; set; }

        public void MigrateLegacyRegistration()
        {
        }
    }
}
