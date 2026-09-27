using System.Reflection;
using System.Text.Json;
using UsageBeacon.Models;
using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class CodexAppServerClientTests
{
    [Fact]
    public async Task StartAsync_RestartsServerAfterInitializeTimesOut()
    {
        var directory = Directory.CreateTempSubdirectory("UsageBeaconCodex-");
        try
        {
            var launcher = Path.Combine(directory.FullName, "fake-codex.cmd");
            var script = Path.Combine(directory.FullName, "fake-server.ps1");
            var starts = Path.Combine(directory.FullName, "starts.txt");
            var respond = Path.Combine(directory.FullName, "respond.txt");
            File.WriteAllText(launcher,
                "@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%~dp0fake-server.ps1\"\r\n");
            File.WriteAllText(script, """
                $startsPath = Join-Path $PSScriptRoot 'starts.txt'
                [System.IO.File]::AppendAllText($startsPath, "start`n")
                while (($line = [Console]::ReadLine()) -ne $null) {
                    $request = $line | ConvertFrom-Json
                    if ($request.method -eq 'initialize' -and
                        (Test-Path (Join-Path $PSScriptRoot 'respond.txt'))) {
                        [Console]::WriteLine('{"jsonrpc":"2.0","id":' + $request.id + ',"result":{}}')
                    }
                }
                """);

            await using var cl = new CodexAppServerClient(
                [launcher], TimeSpan.FromSeconds(8));
            var failure = await Assert.ThrowsAsync<DomainError>(() => cl.StartAsync());
            Assert.Equal(DomainErrorKind.Timeout, failure.Kind);
            Assert.True(File.Exists(starts), "The first fake server did not start.");

            File.WriteAllText(respond, string.Empty);
            await cl.StartAsync();

            Assert.Equal(2, File.ReadAllLines(starts).Length);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReadRateLimitsAsync_ClearsPendingRequest_WhenPipeWriteFails()
    {
        await using var cl = new CodexAppServerClient([]);
        var stdin = new StreamWriter(new MemoryStream());
        stdin.Dispose();
        typeof(CodexAppServerClient)
            .GetField("_stdin", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(cl, stdin);

        var error = await Assert.ThrowsAsync<DomainError>(
            () => cl.ReadRateLimitsAsync());

        Assert.Equal(DomainErrorKind.CodexProcessExited, error.Kind);
        var pending = (System.Collections.IDictionary)typeof(CodexAppServerClient)
            .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cl)!;
        Assert.Empty(pending.Keys);
    }

    [Fact]
    public void Window_UsesNoResetSentinel_WhenResetsAtIsMissing()
    {
        var dto = JsonSerializer.Deserialize<CodexRateLimitsDto>(
            """
            {
                "rateLimits": {
                    "primary": { "usedPercent": 40, "windowDurationMins": 10080 }
                }
            }
            """)!;

        var weekly = dto.WeeklyRateLimit();

        Assert.NotNull(weekly);
        Assert.Equal(DateTime.MinValue, weekly!.ResetsAt);
        Assert.Equal(0.40, weekly.Utilization, precision: 10);
    }

    [Fact]
    public void Window_AcceptsFractionalUsedPercent()
    {
        var dto = JsonSerializer.Deserialize<CodexRateLimitsDto>(
            """
            {
                "rateLimits": {
                    "primary": {
                        "usedPercent": 12.5,
                        "windowDurationMins": 300,
                        "resetsAt": 1700000000
                    }
                }
            }
            """)!;

        var fiveHour = dto.FiveHourRateLimit();

        Assert.NotNull(fiveHour);
        Assert.Equal(0.125, fiveHour!.Utilization, precision: 10);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1700000000).LocalDateTime,
            fiveHour.ResetsAt);
    }

    [Fact]
    public void ResolveExecutable_PrefersNvmSymlinkOverPath()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("UsageBeaconTests-");
        var nvmDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory.FullName, "nvm"));
        var pathDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory.FullName, "path"));
        var originalNvmSymlink = Environment.GetEnvironmentVariable("NVM_SYMLINK");
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            var nvmCommandPath = Path.Combine(nvmDirectory.FullName, "codex.cmd");
            File.WriteAllText(nvmCommandPath, "@echo off");
            File.WriteAllText(Path.Combine(pathDirectory.FullName, "codex.exe"), string.Empty);
            Environment.SetEnvironmentVariable("NVM_SYMLINK", nvmDirectory.FullName);
            Environment.SetEnvironmentVariable("PATH", pathDirectory.FullName);

            var cl = new CodexAppServerClient();
            var method = typeof(CodexAppServerClient).GetMethod(
                "ResolveExecutable",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var result = method.Invoke(cl, null);

            Assert.Equal(nvmCommandPath, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NVM_SYMLINK", originalNvmSymlink);
            Environment.SetEnvironmentVariable("PATH", originalPath);
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveExecutable_SkipsUnsupportedWhereResult()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("UsageBeaconTests-");
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            var extensionlessPath = Path.Combine(tempDirectory.FullName, "codex");
            var commandPath = Path.Combine(tempDirectory.FullName, "codex.cmd");
            File.WriteAllText(extensionlessPath, string.Empty);
            File.WriteAllText(commandPath, "@echo off");
            Environment.SetEnvironmentVariable("PATH", tempDirectory.FullName);

            var cl = new CodexAppServerClient([]);
            var method = typeof(CodexAppServerClient).GetMethod(
                "ResolveExecutable",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var result = method.Invoke(cl, null);

            Assert.Equal(commandPath, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            tempDirectory.Delete(recursive: true);
        }
    }
}
