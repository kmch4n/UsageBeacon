using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using UsageBeacon.Localization;
using UsageBeacon.Models.Insights;
using UsageBeacon.Services;
using UsageBeacon.Services.Insights;
using UsageBeacon.ViewModels;

namespace UsageBeacon.Views;

public partial class DashboardWindow : Window
{
    private const double ChartMaxBarHeight = 120.0;

    private readonly DashboardViewModel _vm;
    private readonly CancellationTokenSource _cts = new();
    private DashboardData? _data;
    private DateTime _lastScanLocal;
    private bool _isLoading;

    public DashboardWindow()
    {
        InitializeComponent();
        _vm = new DashboardViewModel(ModelPricingCatalog.LoadDefault());

        ApplyTheme();
        ApplyLocalization();
        LocalizationService.LanguageChanged += OnLanguageChanged;
        ThemeService.ThemeChanged += OnThemeChanged;
        Closed += (_, _) =>
        {
            LocalizationService.LanguageChanged -= OnLanguageChanged;
            ThemeService.ThemeChanged -= OnThemeChanged;
            _cts.Cancel();
            _cts.Dispose();
        };
        Loaded += async (_, _) => await RefreshDataAsync();
    }

    private void OnLanguageChanged()
        => Dispatcher.Invoke(ApplyLocalization);

    private void OnThemeChanged()
        => Dispatcher.Invoke(ApplyTheme);

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await RefreshDataAsync();

    private async Task RefreshDataAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        StatusText.Text = LocalizationService.Get("DashboardScanning");
        StatusText.Visibility = Visibility.Visible;
        ContentScroll.Visibility = Visibility.Collapsed;
        try
        {
            if (!_vm.HasAnyLogDirectory)
            {
                StatusText.Text = LocalizationService.Get("DashboardNoData");
                return;
            }
            _data = await _vm.LoadAsync(_cts.Token);
            _lastScanLocal = DateTime.Now;
            StatusText.Visibility = Visibility.Collapsed;
            ContentScroll.Visibility = Visibility.Visible;
            Render();
        }
        catch (OperationCanceledException)
        {
            // Window closed during the scan.
        }
        catch (Exception)
        {
            StatusText.Text = LocalizationService.Get("DashboardScanFailed");
        }
        finally
        {
            _isLoading = false;
        }
    }

    // Localization and theming.

    private void ApplyLocalization()
    {
        Title = LocalizationService.Get("DashboardTitle");
        TitleText.Text = Title;
        RefreshBtn.Content = LocalizationService.Get("DashboardRefresh");
        LifetimeTitle.Text = LocalizationService.Get("DashboardLifetime");
        AutomationProperties.SetName(LifetimeCard, LifetimeTitle.Text);
        TodayTitle.Text = LocalizationService.Get("DashboardToday");
        WeekTitle.Text = LocalizationService.Get("DashboardLast7Days");
        MonthTitle.Text = LocalizationService.Get("DashboardLast30Days");
        ChartTitle.Text = LocalizationService.Get("DashboardChartTitle");
        TableTitle.Text = LocalizationService.Get("DashboardTableTitle");
        ColModel.Text = LocalizationService.Get("DashboardColModel");
        ColService.Text = LocalizationService.Get("DashboardColService");
        ColInput.Text = LocalizationService.Get("DashboardColInput");
        ColCached.Text = LocalizationService.Get("DashboardColCached");
        ColOutput.Text = LocalizationService.Get("DashboardColOutput");
        ColCost.Text = LocalizationService.Get("DashboardColCost");
        DisclaimerText.Text = LocalizationService.Get("DashboardDisclaimer");
        if (_data != null) Render();
    }

    private void ApplyTheme()
    {
        var dark = ThemeService.IsDark;
        Resources["WindowBg"]      = Rgb(dark ? 0x1F1F1Fu : 0xF2F2F2u);
        Resources["CardBg"]        = Rgb(dark ? 0x2A2A2Au : 0xFFFFFFu);
        Resources["PrimaryText"]   = Rgb(dark ? 0xF0F0F0u : 0x1A1A1Au);
        Resources["SecondaryText"] = Rgb(dark ? 0xC0C0C0u : 0x454545u);
        Resources["TertiaryText"]  = Rgb(dark ? 0x909090u : 0x707070u);
        Resources["BorderBrush2"]  = Argb(dark ? 0x35FFFFFFu : 0x28000000u);
        Resources["HoverBg"]       = Argb(dark ? 0x18FFFFFFu : 0x18000000u);
        Resources["PressedBg"]     = Argb(dark ? 0x28FFFFFFu : 0x28000000u);
    }

    private static SolidColorBrush Rgb(uint color) => new(MediaColor.FromRgb(
        (byte)(color >> 16), (byte)(color >> 8), (byte)color));

    private static SolidColorBrush Argb(uint color) => new(MediaColor.FromArgb(
        (byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color));

    // Rendering.

    private void Render()
    {
        if (_data is not { } data) return;

        RenderLifetime(data.Lifetime);
        RenderCard(data.Today, TodayCost, TodaySplit, TodayTokens);
        RenderCard(data.Last7Days, WeekCost, WeekSplit, WeekTokens);
        RenderCard(data.Last30Days, MonthCost, MonthSplit, MonthTokens);
        RenderChart(data.Days);
        RenderTable(data.Models);

        UnknownNote.Visibility = data.UnknownModels.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        UnknownNote.Text = data.UnknownModels.Count > 0
            ? LocalizationService.Format(
                "DashboardUnknownModels", string.Join(", ", data.UnknownModels))
            : "";
        PricesAsOfText.Text = string.IsNullOrEmpty(_vm.PricesAsOf)
            ? ""
            : LocalizationService.Format("DashboardPricesAsOf", _vm.PricesAsOf);
        LastScanText.Text = LocalizationService.Format(
            "DashboardLastScan",
            _lastScanLocal.ToString("g", LocalizationService.Culture));
    }

    private void RenderLifetime(LifetimeTokenSummary summary)
    {
        LifetimeTotal.Text = LocalizationService.Format(
            "DashboardLifetimeTotal",
            FormatTokens(summary.TotalTokens));
        LifetimeTokens.Text = LocalizationService.Format(
            "DashboardTokens",
            FormatTokens(summary.TotalInputTokens),
            FormatTokens(summary.TotalOutputTokens));
        LifetimeSince.Visibility = summary.FirstUsageDay is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        LifetimeSince.Text = summary.FirstUsageDay is { } firstDay
            ? LocalizationService.Format(
                "DashboardLifetimeSince",
                firstDay.ToString("d", LocalizationService.Culture))
            : "";
    }

    private static void RenderCard(
        UsagePeriodSummary summary,
        System.Windows.Controls.TextBlock costText,
        System.Windows.Controls.TextBlock splitText,
        System.Windows.Controls.TextBlock tokensText)
    {
        costText.Text = FormatUsd(summary.CostUsd) + (summary.HasUnknownModels ? "+" : "");
        splitText.Text = $"Claude {FormatUsd(summary.ClaudeCostUsd)} · Codex {FormatUsd(summary.CodexCostUsd)}";
        tokensText.Text = LocalizationService.Format(
            "DashboardTokens",
            FormatTokens(summary.TotalInputTokens),
            FormatTokens(summary.TotalOutputTokens));
    }

    private void RenderChart(IReadOnlyList<DailyUsagePoint> days)
    {
        var maxCost = days.Count > 0 ? days.Max(d => d.TotalCostUsd) : 0m;
        var bars = days.Select(day =>
        {
            var claudeHeight = maxCost > 0
                ? (double)(day.ClaudeCostUsd / maxCost) * ChartMaxBarHeight
                : 0.0;
            var codexHeight = maxCost > 0
                ? (double)(day.CodexCostUsd / maxCost) * ChartMaxBarHeight
                : 0.0;
            var tooltip = $"{day.Day.ToString("d", LocalizationService.Culture)}  " +
                          $"{FormatUsd(day.TotalCostUsd)}  " +
                          $"(Claude {FormatUsd(day.ClaudeCostUsd)} · Codex {FormatUsd(day.CodexCostUsd)})";
            return new ChartBarView(claudeHeight, codexHeight, tooltip);
        }).ToList();
        ChartHost.ItemsSource = bars;
        if (days.Count > 0)
        {
            ChartStartLabel.Text = days[0].Day.ToString("d", LocalizationService.Culture);
            ChartEndLabel.Text = days[^1].Day.ToString("d", LocalizationService.Culture);
        }
    }

    private void RenderTable(IReadOnlyList<ModelUsageBreakdown> models)
    {
        ModelRows.ItemsSource = models.Select(model => new ModelRowView(
            model.Model,
            model.Service == UsageService.Claude ? "Claude Code" : "Codex",
            FormatTokens(model.InputTokens),
            FormatTokens(model.CachedInputTokens),
            FormatTokens(model.OutputTokens),
            model.CostUsd is { } cost ? FormatUsd(cost) : "—")).ToList();
    }

    // Formatting: USD stays "$0.00" in every UI culture (ja-JP would render ¥).

    private static string FormatUsd(decimal value)
        => "$" + value.ToString("N2", CultureInfo.InvariantCulture);

    private static string FormatTokens(decimal value)
        => value.ToString("N0", LocalizationService.Culture);

    public sealed record ChartBarView(double ClaudeHeight, double CodexHeight, string Tooltip);

    public sealed record ModelRowView(
        string Model, string Service, string Input, string Cached, string Output, string Cost);
}
