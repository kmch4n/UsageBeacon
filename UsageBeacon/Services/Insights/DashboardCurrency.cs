using System.Globalization;

namespace UsageBeacon.Services.Insights;

/// <summary>Formats USD estimates using deliberately approximate display rates.</summary>
public static class DashboardCurrency
{
    public const decimal YenPerUsd = 150m;
    public const decimal EuroPerUsd = 0.90m;

    public static string Normalize(string? currency)
        => currency?.ToUpperInvariant() is "JPY" or "EUR" ? currency.ToUpperInvariant() : "USD";

    public static string FormatCost(decimal usd, string? currency)
    {
        var normalized = Normalize(currency);
        return normalized switch
        {
            "JPY" => FormatConverted(usd * YenPerUsd, "¥", 0),
            "EUR" => FormatConverted(usd * EuroPerUsd, "€", 2),
            _ => FormatConverted(usd, "$", 2),
        };
    }

    public static string FormatAxisTick(decimal usd, string? currency)
    {
        var formatted = FormatCost(usd, currency);
        return formatted.Contains('.') && !formatted.StartsWith('<')
            ? formatted.TrimEnd('0').TrimEnd('.')
            : formatted;
    }

    private static string FormatConverted(decimal amount, string symbol, int decimals)
    {
        if (amount > 0 && decimal.Round(amount, decimals) == 0)
            return "<" + symbol + (decimals == 0 ? "1" : "0.01");
        return symbol + amount.ToString("N" + decimals, CultureInfo.InvariantCulture);
    }
}
