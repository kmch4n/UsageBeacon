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
    private enum DisplayMode { Wide, Compact, Vertical, AboveTaskbar }

    private const double WideWidth = 118;
    private const double CompactWidth = 70;
    private const double VerticalWidth = 44;
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
        vm.SnapshotChanged += () => Dispatcher.Invoke(UpdateLabels);
        ApplyLocalization();
        UpdateLabels();
    }

    internal WpfButton ToggleButton => Root;

    private void OnClosed(object? sender, EventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        _topmostTimer?.Stop();
    }

    private void OnLanguageChanged()
        => Dispatcher.Invoke(ApplyLocalization);

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
            ShowWindow(hwnd, SW_SHOWNA);
            ReassertTopmost();
            if (++_positionTick % 5 == 0)
                PositionOnSelectedTaskbar(_vm.WidgetPlacement);
        };
        _topmostTimer.Start();
        ReassertTopmost();
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

    private void PositionOnSelectedTaskbar(WidgetPlacement placement)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return;

        var selected = CreateLayout(_screenIndex, placement);
        if (selected != null)
        {
            ApplyLayout(selected);
            return;
        }

        PositionAtScreenEdge(_screenIndex, placement);
    }

    private static Layout? CreateLayout(int screenIndex, WidgetPlacement placement)
    {
        var tb = TaskbarPosition.Get(screenIndex);
        if (tb == null) return null;

        var leftSlot = (tb.WidgetsRight ?? tb.TaskbarLeft) + 4;
        var available = placement == WidgetPlacement.Left
            ? (tb.ContentLeft ?? tb.NotifyLeft) - leftSlot - 4
            : tb.NotifyLeft - (tb.ContentRight ?? (tb.WidgetsRight ?? tb.TaskbarLeft)) - 6;
        return new Layout(placement, tb, leftSlot, available);
    }

    private void ApplyLayout(Layout layout)
    {
        Height = layout.Taskbar.TaskbarHeight;
        var mode = ApplyDisplayMode(layout.AvailableWidth);
        Left = layout.Placement == WidgetPlacement.Left
            ? layout.LeftSlot
            : layout.Taskbar.NotifyLeft - Width - 2;
        Top = mode == DisplayMode.AboveTaskbar
            ? layout.Taskbar.TaskbarTop - Height - 4
            : layout.Taskbar.TaskbarTop;
    }

    private void PositionAtScreenEdge(int screenIndex, WidgetPlacement placement)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;

        // Taskbar inspection failed: keep the widget visible at the selected screen edge.
        var screen = screenIndex < screens.Length ? screens[screenIndex] : screens[0];
        using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        var dpi = g.DpiX / 96.0;

        // Estimate taskbar height, using the primary taskbar when available.
        double tbHeight = TaskbarPosition.Get()?.TaskbarHeight ?? 48 / dpi;
        Height = tbHeight;
        ApplyDisplayMode(WideWidth);
        var fallbackRightSlot = screen.Bounds.Right / dpi - Width - 4;
        Left   = placement == WidgetPlacement.Left
            ? fallbackRightSlot - ActualWidth - 6
            : fallbackRightSlot;
        Top    = screen.Bounds.Bottom / dpi - tbHeight;
    }

    // Label updates.

    private void UpdateLabels()
    {
        var snap       = _vm.Snapshot;
        var claudeUtil = snap.ClaudeUsage?.FiveHour?.Utilization ?? snap.ClaudeUsage?.Weekly?.Utilization;
        var codexUtil  = snap.CodexUsage?.FiveHour?.Utilization  ?? snap.CodexUsage?.Weekly?.Utilization;

        ClaudeLabel.Text       = claudeUtil.HasValue ? $"{(int)(claudeUtil.Value * 100)}%" : "--%";
        ClaudeLabel.Foreground = UtilBrush(claudeUtil);
        CodexLabel.Text        = codexUtil.HasValue  ? $"{(int)(codexUtil.Value  * 100)}%" : "--%";
        CodexLabel.Foreground  = UtilBrush(codexUtil);
        CompactClaudeLabel.Text       = claudeUtil.HasValue ? $"{(int)(claudeUtil.Value * 100)}" : "--";
        CompactClaudeLabel.Foreground = UtilBrush(claudeUtil);
        CompactCodexLabel.Text        = codexUtil.HasValue ? $"{(int)(codexUtil.Value * 100)}" : "--";
        CompactCodexLabel.Foreground  = UtilBrush(codexUtil);
        VerticalClaudeLabel.Text       = claudeUtil.HasValue ? $"{(int)(claudeUtil.Value * 100)}" : "--";
        VerticalClaudeLabel.Foreground = UtilBrush(claudeUtil);
        VerticalCodexLabel.Text        = codexUtil.HasValue ? $"{(int)(codexUtil.Value * 100)}" : "--";
        VerticalCodexLabel.Foreground  = UtilBrush(codexUtil);
    }

    private DisplayMode ApplyDisplayMode(double availableWidth)
    {
        var mode = availableWidth >= WideWidth ? DisplayMode.Wide
            : availableWidth >= CompactWidth ? DisplayMode.Compact
            : availableWidth >= VerticalWidth ? DisplayMode.Vertical
            : DisplayMode.AboveTaskbar;

        Width = mode switch
        {
            DisplayMode.Wide => WideWidth,
            DisplayMode.Compact => CompactWidth,
            DisplayMode.Vertical => VerticalWidth,
            _ => WideWidth,
        };
        WideContent.Visibility = mode is DisplayMode.Wide or DisplayMode.AboveTaskbar
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
