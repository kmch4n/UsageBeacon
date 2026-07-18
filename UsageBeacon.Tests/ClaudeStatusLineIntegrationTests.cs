using System.Diagnostics;
using System.Text.Json.Nodes;
using UsageBeacon.Models;
using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class ClaudeStatusLineIntegrationTests
{
    [Fact]
    public void EnableAndDisable_PreserveExistingStatusLine()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("UsageBeaconTests-");
        var settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
        File.WriteAllText(
            settingsPath,
            """
            {
                "theme": "dark",
                "statusLine": {
                    "type": "command",
                    "command": "bash ~/.claude/statusline.sh",
                    "padding": 2
                }
            }
            """);
        var integration = new ClaudeStatusLineIntegration(
            settingsPath,
            tempDirectory.FullName);

        try
        {
            integration.Enable();

            Assert.True(integration.IsEnabled);
            Assert.True(File.Exists(Path.Combine(
                tempDirectory.FullName,
                "claude-statusline-bridge.ps1")));
            var enabledSettings = JsonNode.Parse(File.ReadAllText(settingsPath));
            Assert.Equal("dark", enabledSettings?["theme"]?.GetValue<string>());
            Assert.Contains(
                "claude-statusline-bridge.ps1",
                enabledSettings?["statusLine"]?["command"]?.GetValue<string>());

            Assert.True(integration.Disable());

            var restoredSettings = JsonNode.Parse(File.ReadAllText(settingsPath));
            Assert.Equal(
                "bash ~/.claude/statusline.sh",
                restoredSettings?["statusLine"]?["command"]?.GetValue<string>());
            Assert.Equal(2, restoredSettings?["statusLine"]?["padding"]?.GetValue<int>());
            Assert.False(File.Exists(Path.Combine(
                tempDirectory.FullName,
                "claude-statusline-bridge.ps1")));
            Assert.False(File.Exists(Path.Combine(
                tempDirectory.FullName,
                "claude-statusline-integration.json")));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Disable_DoesNotOverwriteStatusLineChangedAfterIntegration()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("UsageBeaconTests-");
        var settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
        File.WriteAllText(settingsPath, "{}");
        var integration = new ClaudeStatusLineIntegration(
            settingsPath,
            tempDirectory.FullName);

        try
        {
            integration.Enable();
            var settings = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
            settings["statusLine"]!["command"] = "custom-command";
            File.WriteAllText(settingsPath, settings.ToJsonString());

            Assert.False(integration.Disable());

            var preservedSettings = JsonNode.Parse(File.ReadAllText(settingsPath));
            Assert.Equal(
                "custom-command",
                preservedSettings?["statusLine"]?["command"]?.GetValue<string>());
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BridgeScript_WritesRateLimitsWithoutSessionMetadata()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("UsageBeaconTests-");
        var settingsPath = Path.Combine(tempDirectory.FullName, "settings.json");
        File.WriteAllText(settingsPath, "{}");
        var integration = new ClaudeStatusLineIntegration(
            settingsPath,
            tempDirectory.FullName);

        try
        {
            integration.Enable();
            var scriptPath = Path.Combine(
                tempDirectory.FullName,
                "claude-statusline-bridge.ps1");
            using var process = Process.Start(new ProcessStartInfo(
                "powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            Assert.NotNull(process);
            await process.StandardInput.WriteAsync(
                """
                {
                    "cwd": "C:/private/project",
                    "transcript_path": "C:/private/transcript.jsonl",
                    "rate_limits": {
                        "five_hour": { "used_percentage": 23.5, "resets_at": 1784354400 },
                        "seven_day": { "used_percentage": 41.2, "resets_at": 1784959200 }
                    }
                }
                """);
            process.StandardInput.Close();
            await process.WaitForExitAsync();

            Assert.Equal(0, process.ExitCode);
            var usagePath = Path.Combine(
                tempDirectory.FullName,
                "claude-native-usage.json");
            var entry = ClaudeNativeUsageStore.Load(usagePath);
            Assert.NotNull(entry);
            Assert.Equal(UsageDataSource.ClaudeCodeStatusLine, entry.Source);
            Assert.NotNull(entry.Usage.FiveHour);
            Assert.Equal(0.235, entry.Usage.FiveHour.Utilization, precision: 3);
            var storedJson = File.ReadAllText(usagePath);
            Assert.DoesNotContain("private/project", storedJson);
            Assert.DoesNotContain("transcript", storedJson);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }
}
