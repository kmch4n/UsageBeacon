using UsageBeacon.Models.Insights;
using UsageBeacon.Services.Insights;

namespace UsageBeacon.Tests;

public sealed class DashboardPresentationTests
{
    [Theory]
    [InlineData("USD", "$12.50")]
    [InlineData("JPY", "¥1,875")]
    [InlineData("EUR", "€11.25")]
    public void FormatCost_ConvertsOnlyForDisplay(string currency, string expected)
    {
        Assert.Equal(expected, DashboardCurrency.FormatCost(12.5m, currency));
    }

    [Theory]
    [InlineData("USD", "0.001", "<$0.01")]
    [InlineData("JPY", "0.001", "<¥1")]
    [InlineData("EUR", "0.001", "<€0.01")]
    public void FormatCost_ShowsPositiveAmountsBelowDisplayPrecision(
        string currency, string amount, string expected)
    {
        Assert.Equal(expected, DashboardCurrency.FormatCost(decimal.Parse(amount), currency));
    }

    [Theory]
    [InlineData("USD", "1000", "$1,000")]
    [InlineData("USD", "0.5", "$0.5")]
    [InlineData("EUR", "1000", "€900")]
    [InlineData("JPY", "1000", "¥150,000")]
    public void FormatAxisTick_OmitsUnneededDecimals(
        string currency, string amount, string expected)
    {
        Assert.Equal(expected, DashboardCurrency.FormatAxisTick(decimal.Parse(amount), currency));
    }

    [Fact]
    public void Normalize_RejectsUnknownSavedPreference()
    {
        Assert.Equal("USD", DashboardCurrency.Normalize("GBP"));
        Assert.Equal("EUR", DashboardCurrency.Normalize("eur"));
    }

    [Fact]
    public void DailyPoint_ProvidesTokensAndUnknownCostFlag()
    {
        var day = new DailyUsagePoint(new DateOnly(2026, 9, 25), 2m, 1m, 120, 30, true);

        Assert.Equal(3m, day.TotalCostUsd);
        Assert.Equal(120, day.InputTokens);
        Assert.Equal(30, day.OutputTokens);
        Assert.True(day.HasUnknownModels);
    }
}
