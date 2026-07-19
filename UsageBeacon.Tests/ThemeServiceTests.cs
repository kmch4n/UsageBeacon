using UsageBeacon.Services;
using UsageBeacon.Utilities;

namespace UsageBeacon.Tests;

// ThemeService state is process-global, so every test that mutates it shares
// one collection with the view-model tests and restores the defaults.
[Collection("ThemeServiceState")]
public sealed class ThemeServiceTests
{
    [Theory]
    [InlineData(null, AppTheme.System)]
    [InlineData("", AppTheme.System)]
    [InlineData("garbage", AppTheme.System)]
    [InlineData("System", AppTheme.System)]
    [InlineData("system", AppTheme.System)]
    [InlineData("Light", AppTheme.Light)]
    [InlineData("light", AppTheme.Light)]
    [InlineData("DARK", AppTheme.Dark)]
    [InlineData("7", AppTheme.System)]
    public void NormalizePreference_FallsBackToSystem(string? value, AppTheme expected)
    {
        Assert.Equal(expected, ThemeService.NormalizePreference(value));
    }

    [Fact]
    public void SetTheme_RaisesThemeChangedOnce_AndIsIdempotent()
    {
        var changes = 0;
        void OnChanged() => changes++;
        ThemeService.SystemDarkOverride = () => false;
        ThemeService.SetTheme(AppTheme.System);
        ThemeService.ThemeChanged += OnChanged;

        try
        {
            ThemeService.SetTheme(AppTheme.Dark);
            Assert.True(ThemeService.IsDark);
            Assert.Equal(AppTheme.Dark, ThemeService.Preference);
            Assert.Equal(1, changes);

            ThemeService.SetTheme(AppTheme.Dark);
            Assert.Equal(1, changes);

            ThemeService.SetTheme(AppTheme.Light);
            Assert.False(ThemeService.IsDark);
            Assert.Equal(2, changes);
        }
        finally
        {
            ThemeService.ThemeChanged -= OnChanged;
            ThemeService.SystemDarkOverride = null;
            ThemeService.SetTheme(AppTheme.System);
        }
    }

    [Fact]
    public void NotifySystemThemeChanged_RaisesOnlyWhenEffectiveThemeFlips()
    {
        var changes = 0;
        var systemIsDark = false;
        void OnChanged() => changes++;
        ThemeService.SystemDarkOverride = () => systemIsDark;
        ThemeService.SetTheme(AppTheme.System);
        ThemeService.ThemeChanged += OnChanged;

        try
        {
            // An unrelated preference change must not repaint the windows.
            ThemeService.NotifySystemThemeChanged();
            Assert.Equal(0, changes);

            systemIsDark = true;
            ThemeService.NotifySystemThemeChanged();
            Assert.True(ThemeService.IsDark);
            Assert.Equal(1, changes);

            ThemeService.NotifySystemThemeChanged();
            Assert.Equal(1, changes);
        }
        finally
        {
            ThemeService.ThemeChanged -= OnChanged;
            ThemeService.SystemDarkOverride = null;
            ThemeService.SetTheme(AppTheme.System);
        }
    }

    [Fact]
    public void NotifySystemThemeChanged_IsIgnored_ForExplicitPreference()
    {
        var changes = 0;
        var systemIsDark = false;
        void OnChanged() => changes++;
        ThemeService.SystemDarkOverride = () => systemIsDark;
        ThemeService.SetTheme(AppTheme.Light);
        ThemeService.ThemeChanged += OnChanged;

        try
        {
            systemIsDark = true;
            ThemeService.NotifySystemThemeChanged();
            Assert.False(ThemeService.IsDark);
            Assert.Equal(0, changes);
        }
        finally
        {
            ThemeService.ThemeChanged -= OnChanged;
            ThemeService.SystemDarkOverride = null;
            ThemeService.SetTheme(AppTheme.System);
        }
    }
}
