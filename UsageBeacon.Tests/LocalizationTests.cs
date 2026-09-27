using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using UsageBeacon.Localization;
using UsageBeacon.Models;

namespace UsageBeacon.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void Resources_ContainTheSameKeys_ForEnglishAndJapanese()
    {
        var manager = new ResourceManager(
            "UsageBeacon.Resources.Strings",
            typeof(LocalizationService).Assembly);
        var english = ResourceKeys(manager, CultureInfo.GetCultureInfo("en"));
        var japanese = ResourceKeys(manager, CultureInfo.GetCultureInfo("ja"));

        Assert.Equal(english, japanese);
    }

    [Fact]
    public void Resources_PreserveFormatPlaceholders_AcrossLanguages()
    {
        var manager = new ResourceManager(
            "UsageBeacon.Resources.Strings",
            typeof(LocalizationService).Assembly);
        var english = ResourceValues(manager, CultureInfo.GetCultureInfo("en"));
        var japanese = ResourceValues(manager, CultureInfo.GetCultureInfo("ja"));

        foreach (var key in english.Keys)
        {
            Assert.Equal(
                FormatPlaceholders(english[key]),
                FormatPlaceholders(japanese[key]));
        }
    }

    [Fact]
    public void SetLanguage_UpdatesStringsAndRaisesEvent()
    {
        var original = LocalizationService.LanguagePreference;
        var changes = 0;
        void OnChanged() => changes++;
        LocalizationService.LanguageChanged += OnChanged;

        try
        {
            LocalizationService.SetLanguage("en");
            Assert.Equal("Language", LocalizationService.Get("SettingsLanguage"));
            Assert.Contains("credentials were not found", LocalizedText.DomainError(DomainError.TokenMissing()));

            LocalizationService.SetLanguage("ja");
            Assert.Equal("言語", LocalizationService.Get("SettingsLanguage"));
            Assert.Contains("認証情報が見つかりません", LocalizedText.DomainError(DomainError.TokenMissing()));
            Assert.True(changes >= 1);
        }
        finally
        {
            LocalizationService.LanguageChanged -= OnChanged;
            LocalizationService.SetLanguage(original);
        }
    }

    [Theory]
    [InlineData("AppUnexpectedError")]
    [InlineData("IntegrationFailed")]
    public void UnexpectedErrorMessage_DoesNotExposeExceptionDetails(string resourceKey)
    {
        var secret = "Authorization: Bearer synthetic-credential-123456";
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var exception = new InvalidOperationException($"{secret} in {profile}\\private.json");

        var message = LocalizedText.UnexpectedError(exception, resourceKey);

        Assert.Equal(LocalizationService.Get(resourceKey), message);
        Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
        Assert.DoesNotContain(profile, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private.json", message, StringComparison.Ordinal);
        Assert.DoesNotContain("[", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("rpc")]
    [InlineData("decoding")]
    public void DomainErrorMessage_DoesNotExposeUntrustedDetails(string kind)
    {
        var detail = "Authorization: Bearer synthetic-secret at C:\\Private\\credential.json";
        var error = kind switch
        {
            "network" => DomainError.Network(detail),
            "rpc" => DomainError.CodexRpcError(detail),
            _ => DomainError.Decoding(detail),
        };

        var message = LocalizedText.DomainError(error);

        Assert.DoesNotContain("synthetic-secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("credential.json", message, StringComparison.Ordinal);
        Assert.NotEmpty(message);
    }

    [Theory]
    [InlineData(null, "system")]
    [InlineData("", "system")]
    [InlineData("fr", "en")]
    [InlineData("en", "en")]
    [InlineData("ja", "ja")]
    public void NormalizePreference_UsesEnglishFallbackCatalog(
        string? preference,
        string expected)
    {
        Assert.Equal(expected, LocalizationService.NormalizePreference(preference));
    }

    private static string[] ResourceKeys(ResourceManager manager, CultureInfo culture)
        => ResourceValues(manager, culture).Keys
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

    private static Dictionary<string, string> ResourceValues(
        ResourceManager manager,
        CultureInfo culture)
    {
        var set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: true);
        Assert.NotNull(set);
        return set.Cast<DictionaryEntry>().ToDictionary(
            entry => (string)entry.Key,
            entry => (string)entry.Value!,
            StringComparer.Ordinal);
    }

    private static string[] FormatPlaceholders(string value)
        => Regex.Matches(value, @"\{\d+(?:[^}]*)?\}")
            .Select(match => match.Value)
            .OrderBy(placeholder => placeholder, StringComparer.Ordinal)
            .ToArray();
}
