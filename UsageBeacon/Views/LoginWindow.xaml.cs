using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using UsageBeacon.Localization;
using UsageBeacon.Models;
using UsageBeacon.Services;
using UsageBeacon.Utilities;
using UsageBeacon.ViewModels;
using MediaColor = System.Windows.Media.Color;

namespace UsageBeacon.Views;

public partial class LoginWindow : Window
{
    private readonly string _cliCommand;
    private readonly string _service;
    private readonly WindowsTokenSource? _tokenSource;
    private readonly UsageViewModel _vm;
    private string? _tokenSnapshot;
    private CancellationTokenSource? _pollCts;
    private string? _statusKey;
    private object?[] _statusArguments = [];

    public LoginWindow(string service, string cliCommand, UsageViewModel vm, WindowsTokenSource? tokenSource = null)
    {
        _service = service;
        _cliCommand  = cliCommand;
        _tokenSource = tokenSource;
        _vm          = vm;

        InitializeComponent();
        ApplyTheme();
        LocalizationService.LanguageChanged += OnLanguageChanged;
        ThemeService.ThemeChanged += OnThemeChanged;
        Closed += (_, _) =>
        {
            LocalizationService.LanguageChanged -= OnLanguageChanged;
            ThemeService.ThemeChanged -= OnThemeChanged;
        };
        ApplyLocalization();

        Loaded += async (_, _) =>
        {
            WindowEffects.Apply(this, lightMode: !ThemeService.IsDark);
            await SnapshotCurrentTokenAsync();
        };
    }

    private void OnLanguageChanged()
        => Dispatcher.Invoke(ApplyLocalization);

    private void OnThemeChanged()
        => Dispatcher.Invoke(() =>
        {
            ApplyTheme();
            // Re-applying only updates DWM attributes and the accent policy,
            // which is safe on a live window handle.
            if (IsLoaded)
                WindowEffects.Apply(this, lightMode: !ThemeService.IsDark);
        });

    private void ApplyTheme()
    {
        var dark = ThemeService.IsDark;
        Resources["SurfaceBrush"]  = Argb(dark ? 0xE8202020u : 0xE8F5F5F5u);
        Resources["BorderBrush2"]  = Argb(dark ? 0x28FFFFFFu : 0x28000000u);
        Resources["PrimaryText"]   = Rgb(dark ? 0xF0F0F0u : 0x1A1A1Au);
        Resources["SecondaryText"] = Rgb(dark ? 0x909090u : 0x707070u);
        Resources["GhostHoverBg"]  = Argb(dark ? 0x18FFFFFFu : 0x18000000u);
        Resources["DisabledBtnBg"] = Argb(dark ? 0x20FFFFFFu : 0x20000000u);
        Resources["DisabledBtnFg"] = Rgb(dark ? 0x505050u : 0xA0A0A0u);
        Resources["StatusBg"]      = Argb(dark ? 0x0AFFFFFFu : 0x0A000000u);
    }

    private static SolidColorBrush Rgb(uint color) => new(MediaColor.FromRgb(
        (byte)(color >> 16), (byte)(color >> 8), (byte)color));

    private static SolidColorBrush Argb(uint color) => new(MediaColor.FromArgb(
        (byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color));

    private void ApplyLocalization()
    {
        Title = LocalizationService.Get("LoginWindowTitle");
        TitleLabel.Text = LocalizationService.Format("LoginTitle", _service);
        OpenTerminalBtn.Content = LocalizationService.Format("LoginOpenBrowser", _cliCommand);
        DescLabel.Text = LocalizationService.Get("LoginDescription");
        CopyCommandBtn.Content = LocalizationService.Get("LoginCopyCommand");
        CancelBtn.Content = LocalizationService.Get("CommonCancel");
        DoneBtn.Content = LocalizationService.Get("CommonLoginComplete");
        if (_statusKey != null)
            StatusLabel.Text = LocalizationService.Format(_statusKey, _statusArguments);
    }

    // Initialization.

    private async Task SnapshotCurrentTokenAsync()
    {
        if (_tokenSource == null) return;
        try { _tokenSnapshot = await _tokenSource.ReadAccessTokenAsync(); }
        catch { _tokenSnapshot = null; }
    }

    // Button handlers.

    private void OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/k {_cliCommand}")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            ShowStatus("LoginTerminalFailed");
            return;
        }

        // Release topmost mode so it does not obstruct the browser or terminal flow.
        Topmost = false;

        OpenTerminalBtn.IsEnabled = false;
        DoneBtn.IsEnabled         = true;
        ShowStatus("LoginBrowserPending");

        if (_tokenSource != null)
            _ = PollForNewTokenAsync();
    }

    private void OpenWsl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Load both .bashrc and .profile so the CLI is available on the WSL PATH.
            // cmd.exe does not interpret the single quotes, so WSL receives them unchanged.
            Process.Start(new ProcessStartInfo("cmd.exe", $"/k wsl -- bash -il -c '{_cliCommand}'")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            ShowStatus("LoginWslFailed");
            return;
        }

        Topmost = false;
        OpenTerminalBtn.IsEnabled = false;
        OpenWslBtn.IsEnabled      = false;
        DoneBtn.IsEnabled         = true;
        ShowStatus("LoginWslPending");

        if (_tokenSource != null)
            _ = PollForNewTokenAsync();
    }

    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        try { System.Windows.Clipboard.SetText(_cliCommand); }
        catch { }
        ShowStatus("LoginCommandCopied", _cliCommand);
        DoneBtn.IsEnabled = true;
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        _pollCts?.Cancel();
        _ = _vm.RefreshAsync(force: true);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _pollCts?.Cancel();
        Close();
    }

    // Token polling.

    private async Task PollForNewTokenAsync()
    {
        _pollCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = _pollCts.Token;

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(3000, ct); }
            catch (OperationCanceledException) { break; }

            try
            {
                var token = await _tokenSource!.ReadAccessTokenAsync(ct);
                if (token != _tokenSnapshot)
                {
                    Dispatcher.Invoke(() => ShowStatus("LoginSucceeded"));
                    await Task.Delay(1400, ct);
                    Dispatcher.Invoke(() =>
                    {
                        _ = _vm.RefreshAsync(force: true);
                        Close();
                    });
                    return;
                }
            }
            catch (DomainError e) when (e.Kind == DomainErrorKind.TokenMissing) { }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    // Helpers.

    private void ShowStatus(string key, params object?[] arguments)
    {
        _statusKey = key;
        _statusArguments = arguments;
        StatusLabel.Text = LocalizationService.Format(key, arguments);
        StatusArea.Visibility = Visibility.Visible;
    }
}
