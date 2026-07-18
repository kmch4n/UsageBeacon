# Localization

UsageBeacon uses .NET resource files for its user-facing interface. English is the neutral language, Japanese is included, and unsupported system languages fall back to English.

## Runtime behavior

The popup offers three language choices:

- **System default** uses the Windows UI language when UsageBeacon supports it and otherwise uses English.
- **English** selects `en-US` explicitly.
- **日本語** selects `ja-JP` explicitly.

The selected preference is stored as `uiLanguage` in `%APPDATA%\UsageBeacon\settings.json`. Language changes apply immediately to open UsageBeacon windows, the tray menu, tooltips, messages, dates, times, and duration labels.

User-facing strings belong in:

- `UsageBeacon/Resources/Strings.resx` for neutral English;
- `UsageBeacon/Resources/Strings.<culture>.resx` for translations.

Production C# and XAML must not contain translated user-facing strings. Code comments, XML documentation, diagnostic messages, tests, and repository documentation remain English. Product names, CLI commands, resource keys, and protocol values are not translated.

## Adding a language

1. Copy `UsageBeacon/Resources/Strings.resx` to `Strings.<culture>.resx`.
2. Translate every value without changing resource keys or format placeholders.
3. Set `LanguageNativeName` to the language's native name.
4. Add one `LanguageDefinition` entry in `UsageBeacon/Localization/LanguageCatalog.cs`.
5. Run the tests to verify that every culture contains the same resource keys.
6. Build and publish the application, then verify the language in the popup, login window, tray menu, error states, and reset-time labels.

Keep placeholders such as `{0}` and `{1}` semantically equivalent across translations. Avoid concatenating translated fragments because word order differs between languages. Ensure longer translations wrap or size naturally instead of relying on fixed text widths.

## Validation

Run:

```powershell
dotnet test UsageBeacon.sln -c Debug
dotnet build UsageBeacon.sln -c Debug
dotnet build UsageBeacon.sln -c Release
dotnet publish UsageBeacon\UsageBeacon.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The localization tests verify English and Japanese resource-key parity, fallback normalization, runtime language changes, localized domain errors, and legacy settings compatibility.
