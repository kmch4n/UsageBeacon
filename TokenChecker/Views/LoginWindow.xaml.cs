using System.Diagnostics;
using System.Windows;
using TokenChecker.Models;
using TokenChecker.Services;
using TokenChecker.Utilities;
using TokenChecker.ViewModels;

namespace TokenChecker.Views;

public partial class LoginWindow : Window
{
    private readonly string _cliCommand;
    private readonly WindowsTokenSource? _tokenSource;
    private readonly UsageViewModel _vm;
    private string? _tokenSnapshot;
    private CancellationTokenSource? _pollCts;

    public LoginWindow(string service, string cliCommand, UsageViewModel vm, WindowsTokenSource? tokenSource = null)
    {
        _cliCommand  = cliCommand;
        _tokenSource = tokenSource;
        _vm          = vm;

        InitializeComponent();

        TitleLabel.Text         = $"{service} ログイン";
        OpenTerminalBtn.Content = $"ターミナルを開く ({cliCommand})";
        DescLabel.Text          =
            $"ターミナルで「{cliCommand}」を実行してログインします。\n" +
            "下のボタンでターミナルを開いてください。\n" +
            "ログイン完了後、このウィンドウが自動的に閉じます。";

        Loaded += async (_, _) =>
        {
            WindowEffects.Apply(this);
            await SnapshotCurrentTokenAsync();
        };
    }

    // ── Initialization ────────────────────────────────────────────────────

    private async Task SnapshotCurrentTokenAsync()
    {
        if (_tokenSource == null) return;
        try { _tokenSnapshot = await _tokenSource.ReadAccessTokenAsync(); }
        catch { _tokenSnapshot = null; }
    }

    // ── Button handlers ───────────────────────────────────────────────────

    private void OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/k {_cliCommand}")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowStatus($"ターミナルを開けませんでした: {ex.Message}");
            return;
        }

        // OAuth のブラウザ/ターミナル作業を妨げないよう最前面固定を解除する。
        Topmost = false;

        OpenTerminalBtn.IsEnabled = false;
        DoneBtn.IsEnabled         = true;
        ShowStatus("ターミナルでログイン処理中... トークンを待機しています");

        if (_tokenSource != null)
            _ = PollForNewTokenAsync();
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        _pollCts?.Cancel();
        _ = _vm.RefreshAsync();
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _pollCts?.Cancel();
        Close();
    }

    // ── Token polling ─────────────────────────────────────────────────────

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
                    Dispatcher.Invoke(() => ShowStatus("✓ ログイン完了！ウィンドウを閉じます..."));
                    await Task.Delay(1400, ct);
                    Dispatcher.Invoke(() =>
                    {
                        _ = _vm.RefreshAsync();
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

    // ── Helpers ───────────────────────────────────────────────────────────

    private void ShowStatus(string message)
    {
        StatusLabel.Text      = message;
        StatusArea.Visibility = Visibility.Visible;
    }
}
