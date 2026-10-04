using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using UsageBeacon.Localization;
using UsageBeacon.Models;
using UsageBeacon.Providers;
using UsageBeacon.Services;
using UsageBeacon.Utilities;
using UsageBeacon.ViewModels;
using UsageBeacon.Views;

namespace UsageBeacon.Tests;

[Collection("ThemeServiceState")]
public sealed class UsagePopupWindowTests
{
    [Fact]
    public void LanguagePicker_SelectsSavedLanguageOnInitialLoad()
    {
        RunSta(() =>
        {
            using var directory = new TempDirectory();
            var vm = new UsageViewModel(new StubUsageProvider(),
                new StubUsageProvider(), new FailingSettingsStore(),
                new StubStartupManager(), directory.Path);
            vm.UiLanguage = "en";
            var popup = new UsagePopupWindow(vm);
            try
            {
                var picker = Assert.IsType<ComboBox>(popup.FindName("LanguagePicker"));
                Assert.Equal("en", Assert.IsType<LanguageOption>(picker.SelectedItem).Code);
            }
            finally
            {
                popup.Close();
                DisposeViewModel(vm);
            }
        });
    }

    [Fact]
    public void ServiceHeaders_ShowOfficialIcons()
    {
        RunSta(() =>
        {
            using var directory = new TempDirectory();
            var vm = new UsageViewModel(new StubUsageProvider(),
                new StubUsageProvider(), new FailingSettingsStore(),
                new StubStartupManager(), directory.Path);
            var popup = new UsagePopupWindow(vm);
            try
            {
                var claude = Assert.IsType<Image>(popup.FindName("ClaudeHeaderIcon"));
                var codex = Assert.IsType<Image>(popup.FindName("CodexHeaderIcon"));
                Assert.EndsWith("Resources/claude.png", claude.Source.ToString());
                Assert.EndsWith("Resources/openai.png", codex.Source.ToString());
                var agy = Assert.IsType<Image>(popup.FindName("AgyHeaderIcon"));
                Assert.EndsWith("Resources/antigravity.png", agy.Source.ToString());
            }
            finally
            {
                popup.Close();
                DisposeViewModel(vm);
            }
        });
    }

    [Fact]
    public void WeeklyWidgetCheckbox_RestoresAcceptedValueAfterSaveFailure()
    {
        RunSta(() =>
        {
            using var directory = new TempDirectory();
            var store = new FailingSettingsStore();
            var vm = new UsageViewModel(
                new StubUsageProvider(), new StubUsageProvider(), store,
                new StubStartupManager(), directory.Path);
            var popup = new UsagePopupWindow(vm);
            try
            {
                var checkbox = Assert.IsType<CheckBox>(popup.FindName("WeeklyWidgetChk"));
                Assert.Same(popup.FindName("WeeklyWidgetLabel"),
                    System.Windows.Automation.AutomationProperties.GetLabeledBy(checkbox));
                Assert.False(checkbox.IsChecked);

                store.ThrowOnSave = true;
                checkbox.IsChecked = true;
                Assert.False(checkbox.IsChecked);
                Assert.False(vm.ShowWeeklyInWidget);

                store.ThrowOnSave = false;
                checkbox.IsChecked = true;
                Assert.True(checkbox.IsChecked);
                Assert.True(vm.ShowWeeklyInWidget);
                Assert.Equal(2, store.SaveCount);
            }
            finally
            {
                popup.Close();
                DisposeViewModel(vm);
            }
        });
    }

    [Fact]
    public void SettingsControls_ExposeAccessibleNamesAndThemedFocusVisuals()
    {
        RunSta(() =>
        {
            using var directory = new TempDirectory();
            var vm = new UsageViewModel(new StubUsageProvider(),
                new StubUsageProvider(), new FailingSettingsStore(),
                new StubStartupManager(), directory.Path);
            var popup = new UsagePopupWindow(vm);
            try
            {
                foreach (var (control, label) in new[]
                {
                    ("IntervalPicker", "PollingIntervalLabel"),
                    ("TransparencyPicker", "TransparencyLabel"),
                    ("LanguagePicker", "LanguageLabel"),
                    ("ThemePicker", "ThemeLabel"),
                    ("StartupChk", "StartupLabel"),
                    ("UsageAlertsChk", "UsageAlertsLabel"),
                })
                {
                    var element = Assert.IsAssignableFrom<Control>(popup.FindName(control));
                    var labelBlock = Assert.IsType<TextBlock>(popup.FindName(label));
                    Assert.Same(labelBlock,
                        System.Windows.Automation.AutomationProperties.GetLabeledBy(element));
                    Assert.False(string.IsNullOrWhiteSpace(labelBlock.Text), label);
                    Assert.Same(popup.FindResource("FocusRing"), element.FocusVisualStyle);
                }

                foreach (var name in new[] { "RefreshBtn", "CloseBtn" })
                {
                    var button = Assert.IsType<Button>(popup.FindName(name));
                    Assert.False(string.IsNullOrWhiteSpace(
                        System.Windows.Automation.AutomationProperties.GetName(button)), name);
                }

                foreach (var (name, label) in new[]
                {
                    ("MonitorBtn", "MonitorLabel"),
                    ("PlacementBtn", "PositionLabel"),
                    ("DashboardBtn", "DashboardLabel"),
                })
                {
                    var button = Assert.IsType<Button>(popup.FindName(name));
                    var accessibleName = System.Windows.Automation.AutomationProperties.GetName(button);
                    Assert.Contains(Assert.IsType<TextBlock>(popup.FindName(label)).Text, accessibleName);
                    Assert.Contains(button.Content.ToString()!, accessibleName);
                }
                Assert.Equal(System.Windows.Input.KeyboardNavigationMode.Cycle,
                    System.Windows.Input.KeyboardNavigation.GetTabNavigation(popup));
            }
            finally
            {
                popup.Close();
                DisposeViewModel(vm);
            }
        });
    }

    [Fact]
    public void UsageAlertsCheckbox_RestoresAcceptedValueAfterSaveFailure()
    {
        RunSta(() =>
        {
            using var directory = new TempDirectory();
            var store = new FailingSettingsStore();
            var vm = new UsageViewModel(
                new StubUsageProvider(), new StubUsageProvider(), store,
                new StubStartupManager(), directory.Path);
            var popup = new UsagePopupWindow(vm);
            try
            {
                var checkbox = Assert.IsType<CheckBox>(popup.FindName("UsageAlertsChk"));
                Assert.True(checkbox.IsChecked);

                store.ThrowOnSave = true;
                checkbox.IsChecked = false;
                Assert.True(checkbox.IsChecked);
                Assert.True(vm.UsageAlertsEnabled);

                store.ThrowOnSave = false;
                checkbox.IsChecked = false;
                Assert.False(checkbox.IsChecked);
                Assert.False(vm.UsageAlertsEnabled);
            }
            finally
            {
                popup.Close();
                DisposeViewModel(vm);
            }
        });
    }

    [Theory]
    [InlineData("IntervalPicker")]
    [InlineData("TransparencyPicker")]
    [InlineData("LanguagePicker")]
    [InlineData("ThemePicker")]
    public void RejectedPickerSelection_RestoresAcceptedValue_AndCanBeRetried(string pickerName)
    {
        RunSta(() =>
        {
            using var directory = new TempDirectory();
            var store = new FailingSettingsStore();
            var vm = new UsageViewModel(
                new StubUsageProvider(),
                new StubUsageProvider(),
                store,
                new StubStartupManager(),
                directory.Path);
            var popup = new UsagePopupWindow(vm);
            try
            {
                var picker = Assert.IsType<ComboBox>(popup.FindName(pickerName));
                if (pickerName == "LanguagePicker")
                    picker.SelectedItem = picker.Items.Cast<LanguageOption>()
                        .First(option => option.Code == vm.UiLanguage);
                var accepted = picker.SelectedItem;
                var rejected = picker.Items.Cast<object>().First(item => item != accepted);

                store.ThrowOnSave = true;
                picker.SelectedItem = rejected;

                Assert.Equal(accepted, picker.SelectedItem);
                Assert.Equal(PickerValue(accepted), ViewModelValue(vm, pickerName));
                Assert.Equal("SettingsSaveFailed", vm.SettingsErrorKey);
                Assert.Equal(Visibility.Visible,
                    Assert.IsType<TextBlock>(popup.FindName("SettingsErrorText")).Visibility);
                Assert.Equal(1, store.SaveCount);

                store.ThrowOnSave = false;
                picker.SelectedItem = rejected;

                Assert.Equal(rejected, picker.SelectedItem);
                Assert.Equal(PickerValue(rejected), ViewModelValue(vm, pickerName));
                Assert.Null(vm.SettingsErrorKey);
                Assert.Equal(2, store.SaveCount);
                Assert.Equal(Visibility.Collapsed,
                    Assert.IsType<TextBlock>(popup.FindName("SettingsErrorText")).Visibility);
            }
            finally
            {
                popup.Close();
                DisposeViewModel(vm);
                LocalizationService.SetLanguage("system");
                ThemeService.SetTheme(AppTheme.System);
            }
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void DisposeViewModel(UsageViewModel vm)
        => vm.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static object? PickerValue(object? item)
        => item is LanguageOption language
            ? language.Code
            : item?.GetType().GetProperty("Value")?.GetValue(item);

    private static object ViewModelValue(UsageViewModel vm, string pickerName)
        => pickerName switch
        {
            "IntervalPicker" => vm.PollingInterval,
            "TransparencyPicker" => vm.PopupTransparency,
            "LanguagePicker" => vm.UiLanguage,
            "ThemePicker" => vm.AppTheme,
            _ => throw new ArgumentOutOfRangeException(nameof(pickerName)),
        };

    private sealed class FailingSettingsStore : IAppSettingsStore
    {
        public bool ThrowOnSave { get; set; }
        public int SaveCount { get; private set; }

        public AppSettings Load() => new();

        public void Save(AppSettings settings)
        {
            SaveCount++;
            if (ThrowOnSave) throw new IOException("save failed");
        }
    }

    private sealed class StubUsageProvider : IUsageProvider
    {
        public Task<ServiceUsage> FetchAsync(CancellationToken ct = default)
            => Task.FromResult(new ServiceUsage(null, null, null));
    }

    private sealed class StubStartupManager : IStartupManager
    {
        public bool IsEnabled { get; set; }
        public void MigrateLegacyRegistration() { }
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory =
            Directory.CreateTempSubdirectory("UsageBeaconPopupTests-");

        public string Path => _directory.FullName;

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
