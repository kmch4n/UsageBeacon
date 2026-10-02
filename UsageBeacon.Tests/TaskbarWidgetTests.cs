using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using UsageBeacon.Models;
using UsageBeacon.Providers;
using UsageBeacon.Services;
using UsageBeacon.Utilities;
using UsageBeacon.ViewModels;
using UsageBeacon.Views;
using WpfButton = System.Windows.Controls.Button;

namespace UsageBeacon.Tests;

public sealed class TaskbarWidgetTests
{
    [Fact]
    public void ServiceIcons_LoadPackagedImagesInEveryDisplayMode()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new WidgetFixture();
            foreach (var name in new[]
            {
                "ClaudeIconWide", "OpenAiIconWide",
                "ClaudeIconCompact", "OpenAiIconCompact",
                "ClaudeIconVertical", "OpenAiIconVertical",
            })
            {
                var icon = Assert.IsType<System.Windows.Controls.Image>(
                    fixture.Widget.FindName(name));
                Assert.NotNull(icon.Source);
                Assert.Equal(System.Windows.Media.Stretch.Uniform, icon.Stretch);
            }
        });
    }

    [Fact]
    public void WeeklyOption_ExpandsHorizontalLayoutWithoutShrinkingPercentages()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new WidgetFixture();
            var taskbar = new TaskbarPosition.Info(0, 0, 40, 1000, 40,
                800, 100, 400, 500);

            ApplyLayout(fixture.Widget, WidgetPlacement.Right, taskbar, 274);
            var defaultWidth = fixture.Widget.Width;
            Assert.InRange(defaultWidth, 1, 136);

            fixture.ViewModel.ShowWeeklyInWidget = true;
            ApplyLayout(fixture.Widget, WidgetPlacement.Right, taskbar, 274);
            var measuredContent = Assert.IsType<System.Windows.Controls.StackPanel>(
                fixture.Widget.FindName("WideContent"));
            measuredContent.Measure(new Size(double.PositiveInfinity, 40));
            Assert.True(measuredContent.DesiredSize.Width > defaultWidth,
                $"Weekly content measured {measuredContent.DesiredSize.Width} DIP; default width was {defaultWidth}");

            var claudeWeekly = Assert.IsType<System.Windows.Controls.TextBlock>(
                fixture.Widget.FindName("ClaudeWeeklyLabel"));
            var codexWeekly = Assert.IsType<System.Windows.Controls.TextBlock>(
                fixture.Widget.FindName("CodexWeeklyLabel"));
            var claudeFiveHour = Assert.IsType<System.Windows.Controls.TextBlock>(
                fixture.Widget.FindName("ClaudeLabel"));
            Assert.InRange(fixture.Widget.Width, defaultWidth + 1, 274);
            Assert.Equal(Visibility.Visible, claudeWeekly.Visibility);
            Assert.Equal(Visibility.Visible, codexWeekly.Visibility);
            Assert.Equal(claudeFiveHour.FontSize, claudeWeekly.FontSize);
            Assert.True(fixture.Widget.Left + fixture.Widget.Width <= 774);

            fixture.ViewModel.ShowWeeklyInWidget = false;
            ApplyLayout(fixture.Widget, WidgetPlacement.Right, taskbar, 274);
            Assert.Equal(defaultWidth, fixture.Widget.Width);
            Assert.Equal(Visibility.Collapsed, claudeWeekly.Visibility);
        });
    }

    [Fact]
    public void WeeklyOption_KeepsMissingFiveHourSeparateFromWeeklyValue()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new WidgetFixture();
            fixture.ViewModel.ShowWeeklyInWidget = true;
            var usage = new UsageSnapshot
            {
                ClaudeUsage = new ServiceUsage(null,
                    new RateLimit(0.62, DateTime.UtcNow.AddDays(1)), null),
                CodexUsage = new ServiceUsage(
                    new RateLimit(0.20, DateTime.UtcNow.AddHours(1)),
                    new RateLimit(0.48, DateTime.UtcNow.AddDays(1)), null),
            };
            typeof(UsageViewModel).GetProperty(nameof(UsageViewModel.Snapshot))!
                .SetValue(fixture.ViewModel, usage);

            var claudeFiveHour = Assert.IsType<System.Windows.Controls.TextBlock>(
                fixture.Widget.FindName("ClaudeLabel"));
            var claudeWeekly = Assert.IsType<System.Windows.Controls.TextBlock>(
                fixture.Widget.FindName("ClaudeWeeklyLabel"));
            var codexWeekly = Assert.IsType<System.Windows.Controls.TextBlock>(
                fixture.Widget.FindName("CodexWeeklyLabel"));
            Assert.Equal("--%", claudeFiveHour.Text);
            Assert.Equal("62%", claudeWeekly.Text);
            Assert.Equal("48%", codexWeekly.Text);
            var accessibleName = AutomationProperties.GetName(fixture.Widget.ToggleButton);
            Assert.Contains("62%", accessibleName);
            Assert.Contains("48%", accessibleName);
        });
    }

    [Fact]
    public void WeeklyOption_DoesNotMoveIntoDesktopWhenTaskbarSlotIsTooNarrow()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new WidgetFixture();
            fixture.ViewModel.ShowWeeklyInWidget = true;
            var taskbar = new TaskbarPosition.Info(0, 0, 40, 1000, 40,
                800, 100, 400, 500);

            ApplyLayout(fixture.Widget, WidgetPlacement.Right, taskbar, 20);

            Assert.True(fixture.Widget.Width > 20);
            Assert.InRange(fixture.Widget.Top, taskbar.TaskbarTop,
                taskbar.TaskbarBottom - fixture.Widget.Height);
            Assert.Equal(Visibility.Visible,
                Assert.IsType<System.Windows.Controls.TextBlock>(
                    fixture.Widget.FindName("ClaudeWeeklyLabel")).Visibility);
        });
    }

    [Fact]
    public void LeftPlacement_RequiresBothWidgetAndContentBoundaries()
    {
        var unknown = new TaskbarPosition.Info(0, 0, 40, 1000, 40,
            800, null, null, 500);
        Assert.Equal(-1, TaskbarWidget.AvailableLeftWidth(unknown));

        var known = unknown with { WidgetsRight = 100, ContentLeft = 400 };
        Assert.Equal(292, TaskbarWidget.AvailableLeftWidth(known));
    }

    [Fact]
    public void WideLayout_FitsFourHundredPercentLabelsWithoutClipping()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new WidgetFixture();
            var full = new RateLimit(1, DateTime.UtcNow.AddHours(1));
            typeof(UsageViewModel).GetProperty(nameof(UsageViewModel.Snapshot))!
                .SetValue(fixture.ViewModel, new UsageSnapshot
                {
                    ClaudeUsage = new ServiceUsage(full, full, null),
                    CodexUsage = new ServiceUsage(full, full, null),
                });
            var content = Assert.IsType<System.Windows.Controls.StackPanel>(
                fixture.Widget.FindName("WideContent"));

            content.Measure(new Size(double.PositiveInfinity, 40));
            Assert.True(content.DesiredSize.Width <= fixture.Widget.Width,
                $"Default content needs {content.DesiredSize.Width} DIP");

            fixture.ViewModel.ShowWeeklyInWidget = true;
            content.Measure(new Size(double.PositiveInfinity, 40));
            Assert.True(content.DesiredSize.Width <= fixture.Widget.Width,
                $"Weekly content needs {content.DesiredSize.Width} DIP");
        });
    }

    [Fact]
    public void CompactAndVerticalLayouts_FitTheirPercentagesWithOfficialIcons()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new WidgetFixture();
            var full = new RateLimit(1, DateTime.UtcNow.AddHours(1));
            typeof(UsageViewModel).GetProperty(nameof(UsageViewModel.Snapshot))!
                .SetValue(fixture.ViewModel, new UsageSnapshot
                {
                    ClaudeUsage = new ServiceUsage(full, null, null),
                    CodexUsage = new ServiceUsage(full, null, null),
                });
            var taskbar = new TaskbarPosition.Info(0, 0, 40, 1000, 40,
                800, 100, 400, 500);

            ApplyLayout(fixture.Widget, WidgetPlacement.Right, taskbar, 110);
            var compact = Assert.IsType<System.Windows.Controls.StackPanel>(
                fixture.Widget.FindName("CompactContent"));
            Assert.Equal(Visibility.Visible, compact.Visibility);
            compact.Measure(new Size(double.PositiveInfinity, 40));
            Assert.True(compact.DesiredSize.Width <= fixture.Widget.Width,
                $"Compact content needs {compact.DesiredSize.Width} DIP");

            ApplyLayout(fixture.Widget, WidgetPlacement.Right, taskbar, 60);
            var vertical = Assert.IsType<System.Windows.Controls.StackPanel>(
                fixture.Widget.FindName("VerticalContent"));
            Assert.Equal(Visibility.Visible, vertical.Visibility);
            vertical.Measure(new Size(double.PositiveInfinity, 40));
            Assert.True(vertical.DesiredSize.Width <= fixture.Widget.Width,
                $"Vertical content needs {vertical.DesiredSize.Width} DIP");
        });
    }

    [Fact]
    public void ApplyLayout_ReservesNotificationAreaClearance()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new WidgetFixture();
            var taskbar = new TaskbarPosition.Info(100, 0, 140, 1000, 40,
                800, 100, 400, 600);

            ApplyLayout(fixture.Widget, WidgetPlacement.Right, taskbar, 696);

            Assert.True(fixture.Widget.Left + fixture.Widget.Width <= 774,
                $"Widget right edge was {fixture.Widget.Left + fixture.Widget.Width}");

            ApplyLayout(fixture.Widget, WidgetPlacement.Right, taskbar, 696);

            Assert.True(fixture.Widget.Left + fixture.Widget.Width <= 774,
                "A later full layout must preserve the clearance");
        });
    }

    [Fact]
    public void ApplyLayout_DoesNotMoveAboveBottomTaskbar_WhenInlineSlotIsTooNarrow()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new WidgetFixture();
            var taskbar = new TaskbarPosition.Info(960, 0, 1000, 1000, 40,
                800, 100, 400, 790);

            ApplyLayout(fixture.Widget, WidgetPlacement.Right, taskbar, 4);

            Assert.InRange(fixture.Widget.Top, taskbar.TaskbarTop,
                taskbar.TaskbarBottom - fixture.Widget.Height);
        });
    }

    [Fact]
    public void ApplyLayout_DoesNotMoveBelowTopTaskbar_WhenInlineSlotIsTooNarrow()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new WidgetFixture();
            var taskbar = new TaskbarPosition.Info(0, 0, 40, 1000, 40,
                800, 100, 400, 790);

            ApplyLayout(fixture.Widget, WidgetPlacement.Right, taskbar, 4);

            Assert.InRange(fixture.Widget.Top, taskbar.TaskbarTop,
                taskbar.TaskbarBottom - fixture.Widget.Height);
        });
    }

    [Fact]
    public void ApplyLayout_DoesNotMoveOutsideTaskbar_WhenTrayExpandsLeft()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new WidgetFixture();
            var taskbar = new TaskbarPosition.Info(0, 0, 40, 1000, 40,
                60, 100, 400, 790);

            ApplyLayout(fixture.Widget, WidgetPlacement.Right, taskbar, 4);

            Assert.InRange(fixture.Widget.Top, taskbar.TaskbarTop,
                taskbar.TaskbarBottom - fixture.Widget.Height);
        });
    }

    [Fact]
    public void RightPlacement_KeepsSmallBalancedPaddingAroundWideContent()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new WidgetFixture();
            var taskbar = new TaskbarPosition.Info(0, 0, 40, 1000, 40,
                800, 100, 400, 500);
            fixture.Widget.Show();
            ApplyLayout(fixture.Widget, WidgetPlacement.Right, taskbar, 274);
            fixture.Widget.UpdateLayout();

            var content = Assert.IsType<System.Windows.Controls.StackPanel>(
                fixture.Widget.FindName("WideContent"));
            var left = content.TranslatePoint(new Point(0, 0),
                fixture.Widget).X;
            var right = content.TranslatePoint(new Point(content.ActualWidth, 0),
                fixture.Widget).X;
            Assert.InRange(left, 8, 12);
            Assert.InRange(fixture.Widget.Width - right, 8, 12);

            fixture.ViewModel.ShowWeeklyInWidget = true;
            ApplyLayout(fixture.Widget, WidgetPlacement.Right, taskbar, 274);
            fixture.Widget.UpdateLayout();
            left = content.TranslatePoint(new Point(0, 0),
                fixture.Widget).X;
            right = content.TranslatePoint(new Point(content.ActualWidth, 0),
                fixture.Widget).X;
            Assert.InRange(left, 8, 12);
            Assert.InRange(fixture.Widget.Width - right, 8, 12);
        });
    }

    private static void ApplyLayout(TaskbarWidget widget, WidgetPlacement placement,
        TaskbarPosition.Info taskbar, double availableWidth)
    {
        var layoutType = typeof(TaskbarWidget).GetNestedType("Layout",
            BindingFlags.NonPublic)!;
        var layout = Activator.CreateInstance(layoutType,
            placement, taskbar, taskbar.TaskbarLeft + 4, availableWidth)!;
        typeof(TaskbarWidget).GetMethod("ApplyLayout",
            BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(widget, [layout]);
    }

    private sealed class WidgetFixture : IDisposable
    {
        private readonly DirectoryInfo _directory =
            Directory.CreateTempSubdirectory("UsageBeaconWidgetTests-");
        private readonly UsageViewModel _vm;

        public TaskbarWidget Widget { get; }
        public UsageViewModel ViewModel => _vm;

        public WidgetFixture()
        {
            _vm = new UsageViewModel(new StubUsageProvider(), new StubUsageProvider(),
                new StubSettingsStore(), new StubStartupManager(), _directory.FullName);
            Widget = new TaskbarWidget(_vm);
        }

        public void Dispose()
        {
            Widget.Close();
            _vm.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _directory.Delete(recursive: true);
        }
    }

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
