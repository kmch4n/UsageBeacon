using Microsoft.Win32;
using UsageBeacon.Utilities;

namespace UsageBeacon.Services;

/// <summary>
/// Holds the process-wide theme preference and resolves the effective
/// light or dark appearance, mirroring how LocalizationService owns the
/// language preference.
/// </summary>
public static class ThemeService
{
    private const string PersonalizeKeyPath =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValueName = "AppsUseLightTheme";

    private static AppTheme _preference = AppTheme.System;
    private static bool _isDark = ResolveIsDark(AppTheme.System);

    /// <summary>Test seam that replaces the Windows registry lookup.</summary>
    internal static Func<bool>? SystemDarkOverride;

    public static event Action? ThemeChanged;

    public static AppTheme Preference => _preference;

    /// <summary>The effective appearance after resolving the System option.</summary>
    public static bool IsDark => _isDark;

    public static AppTheme NormalizePreference(string? value)
        => Enum.TryParse<AppTheme>(value, ignoreCase: true, out var theme) &&
           Enum.IsDefined(theme)
            ? theme
            : AppThemeExtensions.Default;

    public static void SetTheme(AppTheme preference)
    {
        var isDark = ResolveIsDark(preference);
        if (_preference == preference && _isDark == isDark) return;

        _preference = preference;
        _isDark = isDark;
        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Re-resolves the System preference after a Windows personalization
    /// change. Raises ThemeChanged only when the effective appearance flips,
    /// because UserPreferenceChanged fires for many unrelated changes.
    /// </summary>
    public static void NotifySystemThemeChanged()
    {
        if (_preference != AppTheme.System) return;

        var isDark = ResolveIsDark(_preference);
        if (isDark == _isDark) return;

        _isDark = isDark;
        ThemeChanged?.Invoke();
    }

    private static bool ResolveIsDark(AppTheme preference) => preference switch
    {
        AppTheme.Light => false,
        AppTheme.Dark => true,
        _ => SystemDarkOverride?.Invoke() ?? ReadSystemIsDark(),
    };

    private static bool ReadSystemIsDark()
    {
        try
        {
            // 0 means dark apps; a missing value or unreadable key falls back
            // to light, matching the application's historical appearance.
            var value = Registry.GetValue(
                PersonalizeKeyPath,
                AppsUseLightThemeValueName,
                defaultValue: null);
            return value is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }
}
