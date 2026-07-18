using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UsageBeacon.Services;

public sealed class ClaudeStatusLineIntegration
{
    private const string ResourceName = "UsageBeacon.Resources.ClaudeStatusLineBridge.ps1";
    private readonly string _settingsPath;
    private readonly string _bridgePath;
    private readonly string _integrationStatePath;

    public ClaudeStatusLineIntegration(
        string? settingsPath = null,
        string? dataDirectory = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var targetDirectory = dataDirectory ?? AppDataPaths.DirectoryPath;
        _settingsPath = settingsPath ?? Path.Combine(home, ".claude", "settings.json");
        _bridgePath = Path.Combine(targetDirectory, "claude-statusline-bridge.ps1");
        _integrationStatePath = Path.Combine(targetDirectory, "claude-statusline-integration.json");
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                var settings = LoadSettings();
                return string.Equals(
                    settings["statusLine"]?["command"]?.GetValue<string>(),
                    BuildBridgeCommand(),
                    StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }

    public void Enable()
    {
        var settings = LoadSettings();
        var currentStatusLine = settings["statusLine"]?.DeepClone();
        var currentCommand = currentStatusLine?["command"]?.GetValue<string>();
        if (string.Equals(currentCommand, BuildBridgeCommand(), StringComparison.Ordinal))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(_bridgePath)!);
        InstallBridgeScript();
        WriteJsonAtomic(
            _integrationStatePath,
            JsonSerializer.Serialize(new IntegrationState
            {
                OriginalStatusLineJson = currentStatusLine?.ToJsonString(),
                OriginalCommand = currentCommand,
            }));

        settings["statusLine"] = new JsonObject
        {
            ["type"] = "command",
            ["command"] = BuildBridgeCommand(),
        };
        SaveSettings(settings);
    }

    public bool Disable()
    {
        var settings = LoadSettings();
        var currentCommand = settings["statusLine"]?["command"]?.GetValue<string>();
        if (!string.Equals(currentCommand, BuildBridgeCommand(), StringComparison.Ordinal))
            return false;

        var state = LoadIntegrationState();
        if (state == null) return false;
        if (string.IsNullOrWhiteSpace(state?.OriginalStatusLineJson))
        {
            settings.Remove("statusLine");
        }
        else
        {
            settings["statusLine"] = JsonNode.Parse(state.OriginalStatusLineJson);
        }
        SaveSettings(settings);
        TryDelete(_integrationStatePath);
        TryDelete(_bridgePath);
        return true;
    }

    private JsonObject LoadSettings()
    {
        if (!File.Exists(_settingsPath)) return [];
        return JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject
               ?? throw new InvalidDataException("Claude settings must contain a JSON object.");
    }

    private void SaveSettings(JsonObject settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        WriteJsonAtomic(
            _settingsPath,
            settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private IntegrationState? LoadIntegrationState()
    {
        try
        {
            return File.Exists(_integrationStatePath)
                ? JsonSerializer.Deserialize<IntegrationState>(
                    File.ReadAllText(_integrationStatePath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private void InstallBridgeScript()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Claude status line bridge resource is missing.");
        using var reader = new StreamReader(stream);
        WriteTextAtomic(_bridgePath, reader.ReadToEnd(), new UTF8Encoding(true));
    }

    private string BuildBridgeCommand()
    {
        var normalizedPath = _bridgePath.Replace('\\', '/');
        return $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{normalizedPath}\"";
    }

    private static void WriteJsonAtomic(string path, string content) =>
        WriteTextAtomic(path, content + "\n");

    private static void WriteTextAtomic(
        string path,
        string content,
        Encoding? encoding = null)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, encoding ?? new UTF8Encoding(false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private sealed class IntegrationState
    {
        public string? OriginalStatusLineJson { get; init; }
        public string? OriginalCommand { get; init; }
    }
}
