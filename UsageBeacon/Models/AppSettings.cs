using System.Text.Json.Serialization;

namespace UsageBeacon.Models;

public sealed class AppSettings
{
    [JsonPropertyName("pollingInterval")]
    public int PollingInterval { get; init; } = 300;

    [JsonPropertyName("widgetPlacement")]
    public string WidgetPlacement { get; init; } = "Right";

    [JsonPropertyName("popupTransparency")]
    public string PopupTransparency { get; init; } = "Percent20";

    [JsonPropertyName("monitorDeviceName")]
    public string? MonitorDeviceName { get; init; }

    [JsonPropertyName("loginPrompted")]
    public bool LoginPrompted { get; init; }

    [JsonPropertyName("uiLanguage")]
    public string UiLanguage { get; init; } = "system";

    [JsonPropertyName("appTheme")]
    public string AppTheme { get; init; } = "System";

    [JsonPropertyName("dashboardCurrency")]
    public string DashboardCurrency { get; init; } = "USD";
}
