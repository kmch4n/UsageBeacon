namespace UsageBeacon.Utilities;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public static class AppThemeExtensions
{
    public static readonly AppTheme Default = AppTheme.System;

    public static readonly AppTheme[] All =
    [
        AppTheme.System,
        AppTheme.Light,
        AppTheme.Dark,
    ];
}
