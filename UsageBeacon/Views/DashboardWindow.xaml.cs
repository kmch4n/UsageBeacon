using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using Button = System.Windows.Controls.Button;
using Canvas = System.Windows.Controls.Canvas;
using TextBlock = System.Windows.Controls.TextBlock;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using System.Windows.Shapes;
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
    private readonly UsageViewModel _settings;
    private readonly CancellationTokenSource _cts = new();
    private DashboardData? _data;
    private DateTime _lastScanLocal;
    private bool _isLoading;
    private int _chartDays = 30;
    private DateOnly? _selectedDay;

    public DashboardWindow(UsageViewModel settings)
    {
        InitializeComponent();
        _settings = settings;
        _vm = new DashboardViewModel(ModelPricingCatalog.LoadDefault());

        ApplyTheme();
        ApplyLocalization();
        UpdateSelectionStyles();
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

    private void Currency_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string currency }) return;
        _settings.DashboardCurrency = currency;
        UpdateSelectionStyles();
        Render();
    }

    private void ChartRange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string range } ||
            !int.TryParse(range, out var days)) return;
        _chartDays = days;
        _selectedDay = null;
        UpdateSelectionStyles();
        Render();
    }

    private void ChartDay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateOnly day }) return;
        _selectedDay = day;
        if (_data is { } data) RenderChart(data.Days);
    }

    private void ModelTableToggle_Click(object sender, RoutedEventArgs e)
    {
        ModelTableContent.Visibility = ModelTableContent.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
        UpdateModelTableToggle();
    }

    private void UpdateModelTableToggle()
    {
        ModelTableToggleBtn.Content = LocalizationService.Get(
            ModelTableContent.Visibility == Visibility.Visible
                ? "DashboardHideBreakdown" : "DashboardShowBreakdown");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        DragMove();
    }

    private void TitleBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        => SystemCommands.ShowSystemMenu(this, PointToScreen(e.GetPosition(this)));

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => SystemCommands.MinimizeWindow(this);

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e)
        => SystemCommands.CloseWindow(this);

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void UpdateSelectionStyles()
    {
        foreach (var button in new[] { UsdBtn, JpyBtn, EurBtn, Chart7Btn, Chart30Btn })
        {
            var selected = button.Tag as string ==
                (button == Chart7Btn || button == Chart30Btn
                    ? _chartDays.ToString(CultureInfo.InvariantCulture)
                    : _settings.DashboardCurrency);
            button.Background = selected
                ? (System.Windows.Media.Brush)Resources["HoverBg"]
                : System.Windows.Media.Brushes.Transparent;
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetName(button,
                (button.Content?.ToString() ?? "") + (selected ? " selected" : ""));
        }
    }

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
        Chart7Btn.Content = LocalizationService.Get("DashboardRange7");
        Chart30Btn.Content = LocalizationService.Get("DashboardRange30");
        AutomationProperties.SetName(MinimizeBtn, LocalizationService.Get("DashboardMinimize"));
        AutomationProperties.SetName(MaximizeBtn, LocalizationService.Get("DashboardMaximize"));
        AutomationProperties.SetName(CloseBtn, LocalizationService.Get("DashboardClose"));
        LifetimeTitle.Text = LocalizationService.Get("DashboardLifetime");
        AutomationProperties.SetName(LifetimeCard, LifetimeTitle.Text);
        TodayTitle.Text = LocalizationService.Get("DashboardToday");
        WeekTitle.Text = LocalizationService.Get("DashboardLast7Days");
        MonthTitle.Text = LocalizationService.Get("DashboardLast30Days");
        ChartTitle.Text = LocalizationService.Get("DashboardChartTitle");
        CostChartTitle.Text = LocalizationService.Get("DashboardCostChartTitle");
        TokenChartTitle.Text = LocalizationService.Get("DashboardTokenChartTitle");
        DailyValuesTitle.Text = LocalizationService.Get("DashboardDailyValuesTitle");
        InputLegend.Text = LocalizationService.Get("DashboardInputLegend");
        OutputLegend.Text = LocalizationService.Get("DashboardOutputLegend");
        LifetimeCoverageText.Text = LocalizationService.Get("DashboardIncompleteCost");
        PeriodCoverageText.Text = LocalizationService.Get("DashboardIncompleteCost");
        CostCoverageText.Text = LocalizationService.Get("DashboardIncompleteCost");
        SelectedDayCoverageText.Text = LocalizationService.Get("DashboardIncompleteDayCost");
        TableTitle.Text = LocalizationService.Get("DashboardTableTitle");
        UpdateModelTableToggle();
        ColModel.Text = LocalizationService.Get("DashboardColModel");
        ColService.Text = LocalizationService.Get("DashboardColService");
        ColInput.Text = LocalizationService.Get("DashboardColInput");
        ColCached.Text = LocalizationService.Get("DashboardColCached");
        ColOutput.Text = LocalizationService.Get("DashboardColOutput");
        ColCost.Text = LocalizationService.Get("DashboardColCost");
        DisclaimerText.Text = LocalizationService.Get("DashboardDisclaimer");
        CurrencyNote.Text = LocalizationService.Get("DashboardCurrencyNote");
        UpdateSelectionStyles();
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
        UpdateSelectionStyles();
        if (_data is { } data) RenderChart(data.Days);
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
        PeriodCoverageText.Visibility = data.Today.HasUnknownModels ||
            data.Last7Days.HasUnknownModels || data.Last30Days.HasUnknownModels
            ? Visibility.Visible : Visibility.Collapsed;
        RenderChart(data.Days);
        RenderTable(data.Models);

        var unknownNotes = new List<string>();
        if (data.UnknownModels.Count > 0)
        {
            unknownNotes.Add(LocalizationService.Format(
                "DashboardUnknownModels",
                string.Join(", ", data.UnknownModels)));
        }
        if (data.Lifetime.HasUnpricedLegacyUsage)
            unknownNotes.Add(LocalizationService.Get("DashboardLegacyLifetimeUnpriced"));
        UnknownNote.Visibility = unknownNotes.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        UnknownNote.Text = string.Join(Environment.NewLine, unknownNotes);
        PricesAsOfText.Text = string.IsNullOrEmpty(_vm.PricesAsOf)
            ? ""
            : LocalizationService.Format("DashboardPricesAsOf", _vm.PricesAsOf);
        LastScanText.Text = LocalizationService.Format(
            "DashboardLastScan",
            _lastScanLocal.ToString("g", LocalizationService.Culture));
    }

    private void RenderLifetime(LifetimeCostSummary summary)
    {
        LifetimeTotal.Text = FormatCost(summary.CostUsd);
        LifetimeSplit.Text =
            $"Claude {FormatCost(summary.ClaudeCostUsd)} · " +
            $"Codex {FormatCost(summary.CodexCostUsd)}";
        LifetimeCoverageText.Visibility = summary.HasUnknownCost
            ? Visibility.Visible : Visibility.Collapsed;
        LifetimeSince.Visibility = summary.FirstUsageDay is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        LifetimeSince.Text = summary.FirstUsageDay is { } firstDay
            ? LocalizationService.Format(
                "DashboardLifetimeSince",
                firstDay.ToString("d", LocalizationService.Culture))
            : "";
    }

    private void RenderCard(
        UsagePeriodSummary summary,
        System.Windows.Controls.TextBlock costText,
        System.Windows.Controls.TextBlock splitText,
        System.Windows.Controls.TextBlock tokensText)
    {
        costText.Text = FormatCost(summary.CostUsd);
        splitText.Text = $"Claude {FormatCost(summary.ClaudeCostUsd)} · Codex {FormatCost(summary.CodexCostUsd)}";
        tokensText.Text = LocalizationService.Format(
            "DashboardTokens",
            FormatTokens(summary.TotalInputTokens),
            FormatTokens(summary.TotalOutputTokens));
    }

    private void RenderChart(IReadOnlyList<DailyUsagePoint> days)
    {
        days = days.TakeLast(_chartDays).ToList();
        if (_selectedDay is null || !days.Any(day => day.Day == _selectedDay))
            _selectedDay = days.Count > 0 ? days[^1].Day : null;
        var analysis = DashboardDayAnalysis.Build(days);
        var costScale = DashboardChartScale.Create(analysis.MaxCostUsd, 0.01m);
        var tokenScale = DashboardChartScale.Create(analysis.MaxTokens, 1m);
        var slotWidth = _chartDays == 7 ? 81.0 : 19.0;
        var bodyWidth = _chartDays == 7 ? 30.0 : 15.0;
        var bars = days.Select(day =>
        {
            var claudeHeight = costScale.Ceiling > 0
                ? (double)(day.ClaudeCostUsd / costScale.Ceiling) * ChartMaxBarHeight
                : 0.0;
            var codexHeight = costScale.Ceiling > 0
                ? (double)(day.CodexCostUsd / costScale.Ceiling) * ChartMaxBarHeight
                : 0.0;
            var tooltip = $"{day.Day.ToString("d", LocalizationService.Culture)}  " +
                          $"{FormatCost(day.TotalCostUsd)}  " +
                          $"(Claude {FormatCost(day.ClaudeCostUsd)} · Codex {FormatCost(day.CodexCostUsd)})";
            if (day.HasUnknownModels)
                tooltip += "  " + LocalizationService.Get("DashboardIncompleteDayCost");
            return new ChartBarView(day.Day, claudeHeight, codexHeight,
                slotWidth, bodyWidth, tooltip,
                day.Day == _selectedDay
                    ? (System.Windows.Media.Brush)Resources["PrimaryText"]
                    : System.Windows.Media.Brushes.Transparent);
        }).ToList();
        var tokenBars = days.Select(day =>
        {
            var inputHeight = tokenScale.Ceiling > 0
                ? (double)(day.InputTokens / tokenScale.Ceiling) * ChartMaxBarHeight
                : 0.0;
            var outputHeight = tokenScale.Ceiling > 0
                ? (double)(day.OutputTokens / tokenScale.Ceiling) * ChartMaxBarHeight
                : 0.0;
            var tooltip = $"{day.Day.ToString("d", LocalizationService.Culture)}  " +
                LocalizationService.Format("DashboardTokens",
                    FormatTokens(day.InputTokens), FormatTokens(day.OutputTokens));
            return new TokenChartBarView(day.Day, inputHeight, outputHeight,
                slotWidth, bodyWidth, tooltip,
                day.Day == _selectedDay
                    ? (System.Windows.Media.Brush)Resources["PrimaryText"]
                    : System.Windows.Media.Brushes.Transparent);
        }).ToList();
        ChartHost.ItemsSource = bars;
        TokenChartHost.ItemsSource = tokenBars;
        RenderAxis(CostAxisCanvas, CostGridCanvas, costScale,
            value => DashboardCurrency.FormatAxisTick(value, _settings.DashboardCurrency));
        RenderAxis(TokenAxisCanvas, TokenGridCanvas, tokenScale,
            value => DashboardDayAnalysis.FormatCompactTokens((long)value));
        DailyValueRows.ItemsSource = days.Select(day => new DailyValueView(
            day.Day,
            day.Day.ToString("M/d", LocalizationService.Culture),
            FormatCost(day.TotalCostUsd),
            DashboardDayAnalysis.FormatCompactTokens(day.InputTokens + day.OutputTokens),
            day.Day.ToString("d", LocalizationService.Culture) + " · " +
                FormatCost(day.TotalCostUsd) + " · " +
                LocalizationService.Format("DashboardTokens",
                    FormatTokens(day.InputTokens), FormatTokens(day.OutputTokens)),
            day.Day == _selectedDay
                ? (Brush)Resources["HoverBg"]
                : Brushes.Transparent)).ToList();
        CostCoverageText.Visibility = analysis.HasIncompleteCost
            ? Visibility.Visible : Visibility.Collapsed;
        RenderPeak(CostPeakBtn, analysis.HighestCostDay, "DashboardPeakCost",
            analysis.HighestCostDay is { } costDay ? FormatCost(costDay.TotalCostUsd) : "");
        RenderPeak(TokenPeakBtn, analysis.HighestTokenDay, "DashboardPeakTokens",
            analysis.HighestTokenDay is { } tokenDay
                ? FormatTokens(tokenDay.InputTokens + tokenDay.OutputTokens) : "");
        if (days.Count > 0)
        {
            ChartStartLabel.Text = days[0].Day.ToString("d", LocalizationService.Culture);
            ChartEndLabel.Text = days[^1].Day.ToString("d", LocalizationService.Culture);
        }
        RenderSelectedDay();
    }

    private void RenderAxis(Canvas axis, Canvas grid, DashboardChartScale scale,
        Func<decimal, string> format)
    {
        axis.Children.Clear();
        grid.Children.Clear();
        var textStyle = (Style)Resources["Tiny"];
        var gridBrush = (Brush)Resources["BorderBrush2"];
        foreach (var tick in scale.Ticks)
        {
            var position = scale.Ceiling > 0
                ? (double)((scale.Ceiling - tick) / scale.Ceiling) * ChartMaxBarHeight
                : ChartMaxBarHeight;
            var label = new TextBlock
            {
                Text = format(tick),
                Style = textStyle,
                Width = 68,
                TextAlignment = TextAlignment.Right
            };
            Canvas.SetTop(label, Math.Clamp(position - 8, 0, ChartMaxBarHeight - 16));
            axis.Children.Add(label);
            var line = new Line
            {
                X1 = 0,
                X2 = 570,
                Y1 = 0,
                Y2 = 0,
                Stroke = gridBrush,
                StrokeThickness = 1
            };
            Canvas.SetTop(line, position);
            grid.Children.Add(line);
        }
    }

    private static void RenderPeak(Button button, DailyUsagePoint? day, string resourceKey,
        string value)
    {
        button.Visibility = day is null ? Visibility.Collapsed : Visibility.Visible;
        if (day is null) return;
        button.Tag = day.Day;
        button.Content = LocalizationService.Format(resourceKey,
            day.Day.ToString("d", LocalizationService.Culture), value);
        AutomationProperties.SetName(button, button.Content.ToString());
    }

    private void RenderSelectedDay()
    {
        var day = _data?.Days.FirstOrDefault(point => point.Day == _selectedDay);
        if (day is null) return;
        SelectedDayTitle.Text = day.Day.ToString("D", LocalizationService.Culture);
        SelectedDayDetails.Text = $"{FormatCost(day.TotalCostUsd)}  ·  " +
            $"Claude {FormatCost(day.ClaudeCostUsd)}  ·  Codex {FormatCost(day.CodexCostUsd)}";
        SelectedDayTokens.Text = LocalizationService.Format("DashboardTokens",
            FormatTokens(day.InputTokens), FormatTokens(day.OutputTokens));
        SelectedDayCoverageText.Visibility = day.HasUnknownModels
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderTable(IReadOnlyList<ModelUsageBreakdown> models)
    {
        var maxCost = models.Count > 0 ? models.Max(model => model.CostUsd ?? 0m) : 0m;
        ModelRows.ItemsSource = models.Select(model => new ModelRowView(
            model.Model,
            model.Service == UsageService.Claude ? "Claude Code" : "Codex",
            FormatTokens(model.InputTokens),
            FormatTokens(model.CachedInputTokens),
            FormatTokens(model.OutputTokens),
            model.CostUsd is { } cost ? FormatCost(cost) : "—",
            model.Service == UsageService.Claude
                ? (System.Windows.Media.Brush)Resources["ClaudeBrush"]
                : (System.Windows.Media.Brush)Resources["CodexBrush"],
            maxCost > 0 && model.CostUsd is { } costValue
                ? (double)(costValue / maxCost) * 70.0
                : 0.0)).ToList();
    }

    private string FormatCost(decimal value)
        => DashboardCurrency.FormatCost(value, _settings.DashboardCurrency);

    private static string FormatTokens(decimal value)
        => value.ToString("N0", LocalizationService.Culture);

    public sealed record ChartBarView(DateOnly Day, double ClaudeHeight, double CodexHeight,
        double SlotWidth, double BodyWidth, string Tooltip,
        System.Windows.Media.Brush SelectionBrush);

    public sealed record TokenChartBarView(DateOnly Day, double InputHeight, double OutputHeight,
        double SlotWidth, double BodyWidth, string Tooltip,
        System.Windows.Media.Brush SelectionBrush);

    public sealed record DailyValueView(DateOnly Day, string DateLabel, string Cost,
        string Tokens, string AccessibleLabel, Brush SelectionBrush);

    public sealed record ModelRowView(
        string Model, string Service, string Input, string Cached, string Output, string Cost,
        System.Windows.Media.Brush ServiceBrush, double CostBarWidth);
}
