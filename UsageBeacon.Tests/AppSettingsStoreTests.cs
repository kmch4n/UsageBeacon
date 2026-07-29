using System.Text.Json;
using UsageBeacon.Models;
using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void Load_PreservesLegacySettings_AndDefaultsLanguageToSystem()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(
                path,
                """
                {
                    "pollingInterval": 300,
                    "widgetPlacement": "Left",
                    "popupTransparency": "Percent30",
                    "monitorDeviceName": "DISPLAY2",
                    "loginPrompted": true
                }
                """);
            var store = new AppSettingsStore(path);

            var settings = store.Load();

            Assert.Equal(300, settings.PollingInterval);
            Assert.Equal("Left", settings.WidgetPlacement);
            Assert.Equal("Percent30", settings.PopupTransparency);
            Assert.Equal("DISPLAY2", settings.MonitorDeviceName);
            Assert.True(settings.LoginPrompted);
            Assert.Equal("system", settings.UiLanguage);
            Assert.Equal("System", settings.AppTheme);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_RoundTripsLanguageAndExistingPreferences()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new AppSettingsStore(path);
            store.Save(new AppSettings
            {
                PollingInterval = 600,
                WidgetPlacement = "Right",
                PopupTransparency = "Percent20",
                MonitorDeviceName = "DISPLAY1",
                LoginPrompted = true,
                UiLanguage = "en",
                AppTheme = "Dark",
            });

            var settings = store.Load();
            using var document = JsonDocument.Parse(File.ReadAllText(path));

            Assert.Equal("en", settings.UiLanguage);
            Assert.Equal(600, settings.PollingInterval);
            Assert.Equal("Dark", settings.AppTheme);
            Assert.Equal("en", document.RootElement.GetProperty("uiLanguage").GetString());
            Assert.Equal("Dark", document.RootElement.GetProperty("appTheme").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_PreservesMalformedSettingsBeforeReturningDefaults()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            const string malformed = """{"pollingInterval": """;
            File.WriteAllText(path, malformed);
            var store = new AppSettingsStore(path);

            var settings = store.Load();

            Assert.Equal(300, settings.PollingInterval);
            Assert.False(File.Exists(path));
            var backup = Assert.Single(Directory.GetFiles(
                directory,
                "settings.json.corrupt-*"));
            Assert.Equal(malformed, File.ReadAllText(backup));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"UsageBeacon.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
