using System.Diagnostics;
using System.Text;
using System.Text.Json;
using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class ClaudeStatusLineBridgeTests
{
    private const string PayloadJson =
        """
        {"rate_limits":{"five_hour":{"used_percentage":50,"resets_at":1700000000}}}
        """;

    [Fact]
    public void Bridge_ForwardsOriginalCommandWithQuotedPath_AndStoresUsage()
    {
        using var directory = new TempDirectory();
        var bridgePath = ExtractBridgeScript(directory.Path);

        // A quoted executable path containing a space is the realistic shape of
        // a preserved Windows status line command.
        var toolDirectory = Directory.CreateDirectory(
            Path.Combine(directory.Path, "tool dir"));
        var toolPath = Path.Combine(toolDirectory.FullName, "statusline.cmd");
        File.WriteAllText(toolPath, "@echo off\r\necho forwarded:%1\r\n");
        File.WriteAllText(
            Path.Combine(directory.Path, "claude-statusline-integration.json"),
            JsonSerializer.Serialize(new
            {
                OriginalStatusLineJson = (string?)null,
                OriginalCommand = $"\"{toolPath}\" hello",
            }));

        var output = RunBridge(bridgePath);

        Assert.Contains("forwarded:hello", output);
        var usage = ClaudeNativeUsageStore.Load(
            Path.Combine(directory.Path, "claude-native-usage.json"));
        Assert.NotNull(usage);
        Assert.Equal(0.5, usage!.Usage.FiveHour?.Utilization);
    }

    [Fact]
    public void Bridge_WritesFallbackStatusLine_WhenNoOriginalCommandExists()
    {
        using var directory = new TempDirectory();
        var bridgePath = ExtractBridgeScript(directory.Path);

        var output = RunBridge(bridgePath);

        Assert.Contains("Claude 5h: 50%", output);
    }

    private static string ExtractBridgeScript(string directory)
    {
        using var stream = typeof(ClaudeStatusLineIntegration).Assembly
            .GetManifestResourceStream("UsageBeacon.Resources.ClaudeStatusLineBridge.ps1");
        Assert.NotNull(stream);
        var bridgePath = Path.Combine(directory, "claude-statusline-bridge.ps1");
        using var reader = new StreamReader(stream!);
        File.WriteAllText(bridgePath, reader.ReadToEnd(), new UTF8Encoding(true));
        return bridgePath;
    }

    private static string RunBridge(string bridgePath)
    {
        var psi = new ProcessStartInfo(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{bridgePath}\"")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var process = Process.Start(psi)!;
        process.StandardInput.Write(PayloadJson);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "bridge script timed out");
        return output;
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory =
            Directory.CreateTempSubdirectory("UsageBeaconTests-");

        public string Path => _directory.FullName;

        public void Dispose()
        {
            try { _directory.Delete(recursive: true); } catch { }
        }
    }
}
