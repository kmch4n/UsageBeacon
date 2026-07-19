using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using UsageBeacon.Localization;
using UsageBeacon.Models;
using UsageBeacon.Services;
using UsageBeacon.Utilities;
using UsageBeacon.ViewModels;
using UsageBeacon.Views;

namespace UsageBeacon;

public partial class App : System.Windows.Application
{
    private NotifyIcon?              _tray;
    private TaskbarWidget?           _widget;
    private UsagePopupWindow?        _popup;
    private UsageViewModel?          _vm;
    private CancellationTokenSource? _pollCts;
    private Icon?                    _trayIcon;
    private Mutex?                   _singleInstanceMutex;
    private Mutex?                   _legacyInstanceMutex;
    private DateTime                 _popupHiddenAt;
    private int                      _targetScreenIndex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsStore = new AppSettingsStore();
        LocalizationService.SetLanguage(settingsStore.Load().UiLanguage);

        var instanceMutex = new Mutex(initiallyOwned: true, @"Local\UsageBeacon", out var isFirstInstance);
        if (!isFirstInstance)
        {
            instanceMutex.Dispose();
            Shutdown();
            return;
        }

        // Hold the legacy mutex as well so the renamed app cannot run alongside TokenChecker.
        var legacyInstanceMutex = new Mutex(
            initiallyOwned: true,
            @"Local\TokenChecker",
            out var isFirstLegacyInstance);
        if (!isFirstLegacyInstance)
        {
            instanceMutex.ReleaseMutex();
            instanceMutex.Dispose();
            legacyInstanceMutex.Dispose();
            Shutdown();
            return;
        }
        _singleInstanceMutex = instanceMutex;
        _legacyInstanceMutex = legacyInstanceMutex;

        DispatcherUnhandledException += (_, ex) =>
        {
            // Show only the exception summary because a full stack can expose local paths.
            System.Windows.MessageBox.Show(ex.Exception.Message,
                LocalizationService.Get("AppStartupErrorTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            ex.Handled = true;
            Shutdown(1);
        };

        try
        {
            _vm = new UsageViewModel(settingsStore: settingsStore);
            _targetScreenIndex = ResolveSavedScreenIndex(_vm.MonitorDeviceName);
            _vm.MonitorDeviceName = System.Windows.Forms.Screen.AllScreens[_targetScreenIndex].DeviceName;

            // Compact widget that remains visible next to the taskbar.
            _widget = new TaskbarWidget(_vm, _targetScreenIndex);
            _widget.PopupToggleRequested += TogglePopup;
            _widget.Show();

            // Detail popup toggled by the widget.
            _popup = new UsagePopupWindow(_vm);
            _popup.MonitorSwitchRequested += CycleMonitor;
            _popup.PlacementSwitchRequested += ToggleWidgetPlacement;
            _popup.UpdatePlacementLabel(_vm.WidgetPlacement);
            _popup.UpdateMonitorLabel(_targetScreenIndex, System.Windows.Forms.Screen.AllScreens.Length);
            _popup.SizeChanged += (_, _) =>
            {
                if (_popup.IsVisible)
                    PositionPopup();
            };

            // Hide the popup when it loses focus.
            _popup.Deactivated += (_, _) =>
            {
                if (_popup is not { IsVisible: true }) return;
                _popup.Hide();
                _popupHiddenAt = DateTime.UtcNow;
            };

            // Tray icon and context menu.
            _trayIcon = LoadTrayIcon();
            _tray = new NotifyIcon
            {
                Visible     = true,
                Text        = "UsageBeacon",
                Icon        = _trayIcon,
                ContextMenuStrip = BuildContextMenu(),
            };
            _tray.MouseClick += OnTrayClick;

            _tray.ShowBalloonTip(
                timeout: 4000,
                tipTitle: LocalizationService.Get("TrayRunningTitle"),
                tipText: LocalizationService.Get("TrayRunningText"),
                tipIcon: ToolTipIcon.Info);

            _vm.SnapshotChanged += UpdateTrayTooltip;
            LocalizationService.LanguageChanged += OnLanguageChanged;

            // Check sign-in once after the first fetch, then show the popup.
            var loginChecked = false;
            _vm.SnapshotChanged += () =>
            {
                if (loginChecked || _vm!.Snapshot.FetchedAt == DateTime.MinValue) return;
                loginChecked = true;
                Dispatcher.BeginInvoke(() =>
                {
                    PromptLoginIfNeeded();
                    PositionPopup();
                    _popup!.Show();
                    _popup.Activate();
                });
            };

            _pollCts = new CancellationTokenSource();
            _ = _vm.RunPollingLoopAsync(_pollCts.Token);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message,
                LocalizationService.Get("AppStartupErrorTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    // First-run sign-in prompt.

    private void PromptLoginIfNeeded()
    {
        if (_vm!.LoginPrompted) return;
        _vm.LoginPrompted = true;

        var snap = _vm.Snapshot;
        if (snap.ClaudeError?.Kind == DomainErrorKind.TokenMissing)
            new LoginWindow("Claude Code", "claude auth login", _vm, new WindowsTokenSource()).ShowDialog();
        if (snap.CodexError?.Kind is DomainErrorKind.CodexRpcError or DomainErrorKind.CodexUnauthorized)
            new LoginWindow("Codex", "codex login", _vm).ShowDialog();
    }

    // Popup visibility and placement.

    private void TogglePopup()
    {
        if (_popup!.IsVisible) { _popup.Hide(); _popupHiddenAt = DateTime.UtcNow; return; }

        // Ignore the same click that caused Deactivated to hide the popup.
        if ((DateTime.UtcNow - _popupHiddenAt).TotalMilliseconds < 250) return;

        PositionPopup();
        _popup.Show();
        _popup.Dispatcher.BeginInvoke(PositionPopup);
        _popup.Activate();
    }

    private void PositionPopup()
    {
        using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        var dpi     = g.DpiX / 96.0;

        var screens = System.Windows.Forms.Screen.AllScreens;
        var screen  = _targetScreenIndex < screens.Length
                      ? screens[_targetScreenIndex]
                      : (System.Windows.Forms.Screen.PrimaryScreen ?? screens[0]);
        var wa      = screen.WorkingArea;
        var popupH  = _popup!.ActualHeight > 10 ? _popup.ActualHeight : 480;
        var taskbarTop = TaskbarPosition.Get(_targetScreenIndex)?.TaskbarTop ?? wa.Bottom / dpi;

        double left = _widget != null
            ? _widget.Left + _widget.ActualWidth / 2 - _popup.Width / 2
            : wa.Right / dpi - _popup.Width - 12;
        double popupBottom = _widget != null
            ? _widget.Top - 8
            : wa.Bottom / dpi - 8;
        popupBottom = Math.Min(popupBottom, taskbarTop - 8);
        double top = popupBottom - popupH;

        // Keep the popup within the selected screen.
        if (left + _popup.Width > wa.Right / dpi) left = wa.Right / dpi - _popup.Width - 4;
        if (left < wa.Left  / dpi)                left = wa.Left  / dpi + 4;
        if (top  < wa.Top   / dpi)                top  = wa.Top   / dpi + 4;

        _popup.Left = left;
        _popup.Top  = top;
    }

    private void CycleMonitor()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length <= 1) return;

        var requestedScreenIndex = (_targetScreenIndex + 1) % screens.Length;
        _widget?.SnapToTaskbar(requestedScreenIndex, _vm!.WidgetPlacement);
        _targetScreenIndex = _widget?.CurrentScreenIndex ?? requestedScreenIndex;
        _vm!.MonitorDeviceName = screens[_targetScreenIndex].DeviceName;
        _popup?.UpdateMonitorLabel(_targetScreenIndex, screens.Length);
        if (_popup?.IsVisible == true)
            PositionPopup();
    }

    private static int ResolveSavedScreenIndex(string? deviceName)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return 0;
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            var primary = Array.FindIndex(screens, screen => screen.Primary);
            return primary >= 0 ? primary : 0;
        }

        var saved = Array.FindIndex(screens,
            screen => string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
        return saved >= 0 ? saved : 0;
    }

    private void ToggleWidgetPlacement()
    {
        _vm!.WidgetPlacement = _vm.WidgetPlacement == WidgetPlacement.Right
            ? WidgetPlacement.Left
            : WidgetPlacement.Right;
        _widget?.SnapToTaskbar(_targetScreenIndex, _vm.WidgetPlacement);
        _targetScreenIndex = _widget?.CurrentScreenIndex ?? _targetScreenIndex;
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (_targetScreenIndex >= 0 && _targetScreenIndex < screens.Length)
            _vm.MonitorDeviceName = screens[_targetScreenIndex].DeviceName;
        _popup?.UpdateMonitorLabel(_targetScreenIndex, System.Windows.Forms.Screen.AllScreens.Length);
        _popup?.UpdatePlacementLabel(_vm.WidgetPlacement);

        if (_popup?.IsVisible == true)
            PositionPopup();
    }

    // Tray icon.

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(LocalizationService.Get("TrayShowHide"), null, (_, _) => Dispatcher.Invoke(TogglePopup));
        menu.Items.Add(LocalizationService.Get("TrayRefreshNow"), null, (_, _) => _ = _vm!.RefreshAsync(force: true));
        menu.Items.Add(LocalizationService.Get("TraySwitchMonitor"), null, (_, _) => Dispatcher.Invoke(CycleMonitor));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(LocalizationService.Get("CommonExit"), null, (_, _) => Dispatcher.Invoke(() => Shutdown()));
        return menu;
    }

    private void OnLanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (_tray == null) return;
            var previousMenu = _tray.ContextMenuStrip;
            _tray.ContextMenuStrip = BuildContextMenu();
            previousMenu?.Dispose();
            _tray.Text = BuildTooltip();
        });
    }

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            Dispatcher.Invoke(TogglePopup);
    }

    private static Icon LoadTrayIcon()
    {
        var resource = GetResourceStream(
            new Uri("pack://application:,,,/Resources/tray.ico"))
            ?? throw new InvalidOperationException("The tray icon resource is missing.");
        using var stream = resource.Stream;
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    private void UpdateTrayTooltip()
    {
        Dispatcher.Invoke(() =>
        {
            if (_tray != null) _tray.Text = BuildTooltip();
        });
    }

    private string BuildTooltip()
    {
        var snap = _vm?.Snapshot;
        if (snap == null) return "UsageBeacon";
        var sb = new System.Text.StringBuilder("UsageBeacon");
        var cu = snap.ClaudeUsage;
        if (cu?.FiveHour is { } cf)
            sb.Append($"\n{LocalizationService.Format("TrayClaudeFiveHour", cf.Percent)}");
        else if (cu?.Weekly is { } cw)
            sb.Append($"\n{LocalizationService.Format("TrayClaudeWeekly", cw.Percent)}");
        var xu = snap.CodexUsage;
        if (xu?.FiveHour is { } xf)
            sb.Append($"\n{LocalizationService.Format("TrayCodexFiveHour", xf.Percent)}");
        else if (xu?.Weekly is { } xw)
            sb.Append($"\n{LocalizationService.Format("TrayCodexWeekly", xw.Percent)}");
        if (snap.FetchedAt > DateTime.MinValue)
        {
            sb.Append('\n');
            sb.Append(LocalizationService.Format(
                "TrayUpdated",
                snap.FetchedAt.ToString("T", LocalizationService.Culture)));
        }
        return sb.ToString();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _pollCts?.Cancel();
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        _tray?.Dispose();
        _trayIcon?.Dispose();
        if (_vm != null) await _vm.DisposeAsync();
        try { _singleInstanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        try { _legacyInstanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _singleInstanceMutex?.Dispose();
        _legacyInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
