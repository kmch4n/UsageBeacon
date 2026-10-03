using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;
using UsageBeacon.Localization;
using UsageBeacon.Utilities;
using UsageBeacon.ViewModels;
using MediaColor = System.Windows.Media.Color;
using WpfButton = System.Windows.Controls.Button;

namespace UsageBeacon.Views;

public partial class TaskbarWidget : Window
{
    private enum DisplayMode { Wide, Compact, Vertical, Unavailable }

    private const double WideWidth = 136;
    private const double ExtendedWidth = 240;
    private const int NotificationClearance = 26;
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOREDRAW   = 0x0008;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int  SW_HIDE        = 0;
    private const int  SW_SHOWNA      = 8;

    private readonly UsageViewModel _vm;
    private DispatcherTimer?        _topmostTimer;
    private int                     _screenIndex;
    private int                     _positionTick;
    private bool                    _placementInvalid;

    public event Action? PopupToggleRequested;

    public int CurrentScreenIndex => _screenIndex;

    public TaskbarWidget(UsageViewModel vm, int initialScreenIndex = 0)
    {
        _vm = vm;
        _screenIndex = initialScreenIndex;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        LocalizationService.LanguageChanged += OnLanguageChanged;
        vm.SnapshotChanged += OnSnapshotChanged;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        ApplyLocalization();
        ApplyWeeklySetting();
        UpdateLabels();
    }

    internal WpfButton ToggleButton => Root;

    private void OnClosed(object? sender, EventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        _vm.SnapshotChanged -= OnSnapshotChanged;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _topmostTimer?.Stop();
    }

    private void OnLanguageChanged()
        => Dispatcher.Invoke(() =>
        {
            ApplyLocalization();
            UpdateLabels();
        });

    private void OnSnapshotChanged()
        => Dispatcher.Invoke(UpdateLabels);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(UsageViewModel.ShowWeeklyInWidget) or
            nameof(UsageViewModel.ShowAgyUsage))) return;
        if (Dispatcher.CheckAccess()) ApplyWeeklySetting();
        else Dispatcher.BeginInvoke(ApplyWeeklySetting);
    }

    private void ApplyWeeklySetting()
    {
        var visibility = _vm.ShowWeeklyInWidget ? Visibility.Visible : Visibility.Collapsed;
        ClaudeWeeklySeparator.Visibility = visibility;
        ClaudeWeeklyLabel.Visibility = visibility;
        CodexWeeklySeparator.Visibility = visibility;
        CodexWeeklyLabel.Visibility = visibility;
        AgyWeeklySeparator.Visibility = visibility;
        AgyWeeklyLabel.Visibility = visibility;
        // The vertical layout keeps two rows so it fits the taskbar height.
        var agyVisibility = _vm.ShowAgyUsage ? Visibility.Visible : Visibility.Collapsed;
        AgyWidePanel.Visibility = agyVisibility;
        AgyCompactPanel.Visibility = agyVisibility;
        if (!IsLoaded)
            Width = _vm.ShowWeeklyInWidget ? ExtendedWidth : WideWidth;
        UpdateLabels();
    }

    private void ApplyLocalization()
    {
        var accessibleName = LocalizationService.Get("WidgetOpenUsage");
        AutomationProperties.SetName(Root, accessibleName);
        Root.ToolTip = accessibleName;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Cached taskbar geometry is stale after a display change.
        TaskbarPosition.Invalidate();
        // SystemEvents raises this on a worker thread; window properties may
        // only be touched on the dispatcher thread.
        Dispatcher.BeginInvoke(() => PositionOnSelectedTaskbar(_vm.WidgetPlacement));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Do not apply DWM effects because the widget must blend into the taskbar.
        // Acrylic on an AllowsTransparency layered window can lose content during click redraws.
        SnapToTaskbar(_screenIndex, _vm.WidgetPlacement);

        // Pin to every virtual desktop. SetPropW avoids polling when supported.
        var initialHwnd = new WindowInteropHelper(this).Handle;
        VirtualDesktopHelper.PinToAllDesktops(initialHwnd);

        // Refresh cached taskbar geometry when displays change.
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Reassert topmost because the taskbar can otherwise cover the widget.
        // Also follow virtual desktop changes.
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _topmostTimer.Tick += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (!VirtualDesktopHelper.IsOnCurrentDesktop(hwnd))
            {
                // Move to the active desktop and show again after a desktop change.
                VirtualDesktopHelper.MoveToCurrentDesktop(hwnd);
            }
            if (++_positionTick % 5 == 0)
                PositionOnSelectedTaskbar(_vm.WidgetPlacement);
            if (!EnsureNotificationClearance())
            {
                ShowWindow(hwnd, SW_HIDE);
                return;
            }
            ShowWindow(hwnd, SW_SHOWNA);
            ReassertTopmost();
        };
        _topmostTimer.Start();
        if (EnsureNotificationClearance())
            ReassertTopmost();
        else
            ShowWindow(initialHwnd, SW_HIDE);
    }

    private void ReassertTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOREDRAW);
    }

    // Taskbar-adjacent placement.

    public void SnapToTaskbar(int screenIndex, WidgetPlacement placement)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        _screenIndex = screenIndex >= 0 && screenIndex < screens.Length ? screenIndex : 0;
        PositionOnSelectedTaskbar(placement);
    }

    private sealed record Layout(
        WidgetPlacement Placement,
        TaskbarPosition.Info Taskbar,
        double LeftSlot,
        double AvailableWidth);

    private double LogicalPixelsPerWindowPixel
        => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;

    private void PositionOnSelectedTaskbar(WidgetPlacement placement)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return;

        if (TaskbarPosition.ReadCurrentBounds(_screenIndex)?.Notification is null)
        {
            _placementInvalid = true;
            HideIfClearanceCannotBeVerified();
            return;
        }

        var selected = CreateLayout(_screenIndex, placement);
        if (selected != null)
        {
            ApplyLayout(selected);
            HideIfClearanceCannotBeVerified();
            return;
        }

        _placementInvalid = true;
        HideIfClearanceCannotBeVerified();
    }

    private void HideIfClearanceCannotBeVerified()
    {
        if (IsLoaded && !EnsureNotificationClearance())
            ShowWindow(new WindowInteropHelper(this).Handle, SW_HIDE);
    }

    private Layout? CreateLayout(int screenIndex, WidgetPlacement placement)
    {
        var tb = TaskbarPosition.Get(screenIndex);
        if (tb == null) return null;

        var logicalScale = LogicalPixelsPerWindowPixel;
        var clearance = NotificationClearance * logicalScale;
        var leftSlot = (tb.WidgetsRight ?? tb.TaskbarLeft) + 4;
        var available = placement == WidgetPlacement.Left
            ? AvailableLeftWidth(tb)
            : tb.ContentRight is { } contentRight
                ? tb.NotifyLeft - contentRight - clearance
                : -1;
        return new Layout(placement, tb, leftSlot, available);
    }

    internal static double AvailableLeftWidth(TaskbarPosition.Info taskbar)
        => taskbar.WidgetsRight is { } widgetsRight &&
           taskbar.ContentLeft is { } contentLeft
            ? contentLeft - widgetsRight - 8
            : -1;

    private void ApplyLayout(Layout layout)
    {
        Height = layout.Taskbar.TaskbarHeight;
        AlignContent(layout.Placement);
        var mode = ApplyDisplayMode(layout.AvailableWidth);
        _placementInvalid = mode == DisplayMode.Unavailable;
        Top = layout.Taskbar.TaskbarTop;
        if (_placementInvalid) return;
        Left = layout.Placement == WidgetPlacement.Left
            ? layout.LeftSlot
            : layout.Taskbar.NotifyLeft - Width -
              NotificationClearance * LogicalPixelsPerWindowPixel;
    }

    private void AlignContent(WidgetPlacement placement)
    {
        var right = placement == WidgetPlacement.Right;
        foreach (var content in new[] { WideContent, CompactContent, VerticalContent })
        {
            content.HorizontalAlignment = right
                ? System.Windows.HorizontalAlignment.Right
                : System.Windows.HorizontalAlignment.Left;
            content.Margin = new Thickness(10, 0, 10, 0);
        }
    }

    private bool EnsureNotificationClearance()
    {
        var current = TaskbarPosition.ReadCurrentBounds(_screenIndex);
        if (_placementInvalid || current?.Notification is not { } notification)
            return false;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!TaskbarPosition.TryReadRectangle(hwnd, out var widget))
            return false;
        if (!TaskbarPosition.IsWithinTaskbar(widget, current.Taskbar))
            return false;
        var shift = TaskbarPosition.RequiredNotificationShift(widget,
            notification, NotificationClearance);
        if (shift > 0)
        {
            Left -= shift * LogicalPixelsPerWindowPixel;
            if (!TaskbarPosition.TryReadRectangle(hwnd, out widget))
                return false;
        }
        return TaskbarPosition.IsWithinTaskbar(widget, current.Taskbar) &&
               TaskbarPosition.HasNotificationClearance(widget, notification,
                   NotificationClearance);
    }

    // Label updates.

    private void UpdateLabels()
    {
        var snap       = _vm.Snapshot;
        var claudeUtil = snap.ClaudeUsage?.FiveHour?.Utilization ??
            (_vm.ShowWeeklyInWidget ? null : snap.ClaudeUsage?.Weekly?.Utilization);
        var codexUtil = snap.CodexUsage?.FiveHour?.Utilization ??
            (_vm.ShowWeeklyInWidget ? null : snap.CodexUsage?.Weekly?.Utilization);
        var agyUtil = snap.AgyUsage?.FiveHour?.Utilization ??
            (_vm.ShowWeeklyInWidget ? null : snap.AgyUsage?.Weekly?.Utilization);
        var claudeWeekly = snap.ClaudeUsage?.Weekly?.Utilization;
        var codexWeekly = snap.CodexUsage?.Weekly?.Utilization;
        var agyWeekly = snap.AgyUsage?.Weekly?.Utilization;

        ClaudeLabel.Text       = claudeUtil.HasValue ? $"{(int)(claudeUtil.Value * 100)}%" : "--%";
        ClaudeLabel.Foreground = UtilBrush(claudeUtil);
        CodexLabel.Text        = codexUtil.HasValue  ? $"{(int)(codexUtil.Value  * 100)}%" : "--%";
        CodexLabel.Foreground  = UtilBrush(codexUtil);
        ClaudeWeeklyLabel.Text = claudeWeekly.HasValue ? $"{(int)(claudeWeekly.Value * 100)}%" : "--%";
        ClaudeWeeklyLabel.Foreground = UtilBrush(claudeWeekly);
        CodexWeeklyLabel.Text = codexWeekly.HasValue ? $"{(int)(codexWeekly.Value * 100)}%" : "--%";
        CodexWeeklyLabel.Foreground = UtilBrush(codexWeekly);
        AgyLabel.Text = agyUtil.HasValue ? $"{(int)(agyUtil.Value * 100)}%" : "--%";
        AgyLabel.Foreground = UtilBrush(agyUtil);
        AgyWeeklyLabel.Text = agyWeekly.HasValue ? $"{(int)(agyWeekly.Value * 100)}%" : "--%";
        AgyWeeklyLabel.Foreground = UtilBrush(agyWeekly);
        CompactClaudeLabel.Text       = claudeUtil.HasValue ? $"{(int)(claudeUtil.Value * 100)}" : "--";
        CompactClaudeLabel.Foreground = UtilBrush(claudeUtil);
        CompactCodexLabel.Text        = codexUtil.HasValue ? $"{(int)(codexUtil.Value * 100)}" : "--";
        CompactCodexLabel.Foreground  = UtilBrush(codexUtil);
        CompactAgyLabel.Text          = agyUtil.HasValue ? $"{(int)(agyUtil.Value * 100)}" : "--";
        CompactAgyLabel.Foreground    = UtilBrush(agyUtil);
        VerticalClaudeLabel.Text       = claudeUtil.HasValue ? $"{(int)(claudeUtil.Value * 100)}" : "--";
        VerticalClaudeLabel.Foreground = UtilBrush(claudeUtil);
        VerticalCodexLabel.Text        = codexUtil.HasValue ? $"{(int)(codexUtil.Value * 100)}" : "--";
        VerticalCodexLabel.Foreground  = UtilBrush(codexUtil);
        UpdateAccessibleDescription(claudeUtil, claudeWeekly, codexUtil, codexWeekly,
            agyUtil, agyWeekly);
        if (IsLoaded)
            PositionOnSelectedTaskbar(_vm.WidgetPlacement);
    }

    private void UpdateAccessibleDescription(double? claudeFiveHour, double? claudeWeekly,
        double? codexFiveHour, double? codexWeekly, double? agyFiveHour, double? agyWeekly)
    {
        if (!_vm.ShowWeeklyInWidget)
        {
            var action = LocalizationService.Get("WidgetOpenUsage");
            AutomationProperties.SetName(Root, action);
            Root.ToolTip = action;
            return;
        }

        string Describe(double? value) => value.HasValue
            ? $"{(int)(value.Value * 100)}%"
            : LocalizationService.Get("WidgetUnavailable");
        var description = _vm.ShowAgyUsage
            ? LocalizationService.Format("WidgetWeeklyDescriptionWithAgy",
                Describe(claudeFiveHour), Describe(claudeWeekly),
                Describe(codexFiveHour), Describe(codexWeekly),
                Describe(agyFiveHour), Describe(agyWeekly))
            : LocalizationService.Format("WidgetWeeklyDescription",
                Describe(claudeFiveHour), Describe(claudeWeekly),
                Describe(codexFiveHour), Describe(codexWeekly));
        AutomationProperties.SetName(Root, description);
        Root.ToolTip = description;
    }

    private DisplayMode ApplyDisplayMode(double availableWidth)
    {
        WideContent.Visibility = Visibility.Visible;
        CompactContent.Visibility = Visibility.Visible;
        VerticalContent.Visibility = Visibility.Visible;
        var measureSize = new System.Windows.Size(
            double.PositiveInfinity, double.PositiveInfinity);
        WideContent.InvalidateMeasure();
        CompactContent.InvalidateMeasure();
        VerticalContent.InvalidateMeasure();
        WideContent.Measure(measureSize);
        CompactContent.Measure(measureSize);
        VerticalContent.Measure(measureSize);
        var requiredWidth = Math.Ceiling(WideContent.DesiredSize.Width);
        var compactWidth = Math.Ceiling(CompactContent.DesiredSize.Width);
        var verticalWidth = Math.Ceiling(VerticalContent.DesiredSize.Width);
        var mode = availableWidth >= requiredWidth ? DisplayMode.Wide
            : _vm.ShowWeeklyInWidget ? DisplayMode.Unavailable
            : availableWidth >= compactWidth ? DisplayMode.Compact
            : availableWidth >= verticalWidth ? DisplayMode.Vertical
            : DisplayMode.Unavailable;

        Width = mode switch
        {
            DisplayMode.Wide => requiredWidth,
            DisplayMode.Compact => compactWidth,
            DisplayMode.Vertical => verticalWidth,
            _ => requiredWidth,
        };
        WideContent.Visibility = mode is DisplayMode.Wide or DisplayMode.Unavailable
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompactContent.Visibility = mode == DisplayMode.Compact ? Visibility.Visible : Visibility.Collapsed;
        VerticalContent.Visibility = mode == DisplayMode.Vertical ? Visibility.Visible : Visibility.Collapsed;
        return mode;
    }

    private static System.Windows.Media.SolidColorBrush UtilBrush(double? v)
    {
        if (v == null) return new(MediaColor.FromRgb(0x90, 0x90, 0x90));
        return new(v < 0.75 ? MediaColor.FromRgb(0x4C, 0xAF, 0x50)
                 : v < 0.90 ? MediaColor.FromRgb(0xFF, 0xC1, 0x07)
                             : MediaColor.FromRgb(0xF4, 0x43, 0x36));
    }

    // Toggle the detail popup on click.

    private void Root_Click(object sender, RoutedEventArgs e)
        => PopupToggleRequested?.Invoke();
}
