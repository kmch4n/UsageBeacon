using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using UsageBeacon.Models;

namespace UsageBeacon.Providers;

/// <summary>Exit code and captured standard output of one agy invocation.</summary>
public sealed record AgyCommandResult(int ExitCode, string Output);

/// <summary>
/// Reads the Antigravity Gemini quota through the non-interactive usage report
/// (<c>agy -p /usage --output-format json</c>). Since agy 1.1.11 the command
/// does not start an agent turn, spend quota, or create a conversation.
/// Older releases forward <c>/usage</c> to the model as a literal prompt, so the
/// provider refuses to run the report until the installed version is verified.
/// Only the Gemini group is reported; the separate quota for third-party models
/// (Claude and GPT through Antigravity) is intentionally ignored.
/// </summary>
public sealed class AgyUsageProvider : IUsageProvider
{
    public static readonly Version MinimumVersion = new(1, 1, 11);

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan UsageTimeout = TimeSpan.FromSeconds(90);
    private static readonly string[] VersionArguments = ["--version"];
    private static readonly string[] UsageArguments = ["-p", "/usage", "--output-format", "json"];
    private const int MaxOutputChars = 1024 * 1024;
    internal const string DisableAutoUpdateVariable = "AGY_CLI_DISABLE_AUTO_UPDATE";

    private readonly Func<string?> _resolveExecutable;
    private readonly Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<AgyCommandResult>> _run;
    private (string Executable, DateTime WrittenAtUtc)? _verifiedExecutable;

    public AgyUsageProvider(
        Func<string?>? resolveExecutable = null,
        Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<AgyCommandResult>>? run = null)
    {
        _resolveExecutable = resolveExecutable ?? ResolveExecutable;
        _run = run ?? RunAsync;
    }

    public async Task<ServiceUsage> FetchAsync(CancellationToken ct = default)
    {
        var executable = _resolveExecutable() ?? throw DomainError.AgyNotFound();
        await EnsureSupportedVersionAsync(executable, ct);

        var result = await _run(executable, UsageArguments, UsageTimeout, ct);
        if (result.ExitCode != 0)
            throw DomainError.AgyCommandFailed($"exit code {result.ExitCode}");
        return AgyUsageParser.Parse(result.Output);
    }

    private async Task EnsureSupportedVersionAsync(string executable, CancellationToken ct)
    {
        // agy updates itself in place, so a new write time requires a new check.
        var writtenAtUtc = GetLastWriteTimeUtc(executable);
        if (_verifiedExecutable is { } verified &&
            verified.Executable == executable &&
            verified.WrittenAtUtc == writtenAtUtc)
            return;

        var result = await _run(executable, VersionArguments, VersionTimeout, ct);
        var version = result.ExitCode == 0 ? ParseVersion(result.Output) : null;
        if (version == null || version < MinimumVersion)
            throw DomainError.AgyUnsupportedVersion(version?.ToString());
        _verifiedExecutable = (executable, writtenAtUtc);
    }

    /// <summary>Extracts the first dotted version number from <c>agy --version</c> output.</summary>
    public static Version? ParseVersion(string output)
    {
        var match = System.Text.RegularExpressions.Regex.Match(output, @"\b(\d+)\.(\d+)\.(\d+)\b");
        return match.Success && Version.TryParse(match.Value, out var version) ? version : null;
    }

    /// <summary>Finds agy.exe in its default install location or on PATH.</summary>
    public static string? ResolveExecutable()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installed = Path.Combine(localAppData, "agy", "bin", "agy.exe");
        if (File.Exists(installed)) return installed;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), "agy.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    private static DateTime GetLastWriteTimeUtc(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string executable,
        IReadOnlyList<string> arguments,
        string workDirectory)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        // Every 15 minutes agy spawns a detached `agy --bg-updater`, which runs
        // `agy --version` in a new, visible console. agy accepts only "true" here
        // ("1" is ignored). Interactive agy sessions still update themselves.
        startInfo.Environment[DisableAutoUpdateVariable] = "true";
        return startInfo;
    }

    private static async Task<AgyCommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeoutAfter,
        CancellationToken ct)
    {
        // Run in a private empty directory so agy never treats a user project as its workspace.
        var workDirectory = Path.Combine(Path.GetTempPath(), "UsageBeacon", "agy-usage");
        Directory.CreateDirectory(workDirectory);

        var startInfo = CreateStartInfo(executable, arguments, workDirectory);
        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw DomainError.AgyNotFound();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw DomainError.AgyNotFound();
        }

        using (process)
        {
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutAfter);
            var stdoutTask = ReadLimitedAsync(process.StandardOutput);
            var stderrTask = ReadLimitedAsync(process.StandardError);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                KillTree(process);
                ct.ThrowIfCancellationRequested();
                throw DomainError.Timeout();
            }

            var stdout = await stdoutTask;
            await stderrTask;
            return new AgyCommandResult(process.ExitCode, stdout);
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            // Keep draining so the child never blocks on a full pipe, but cap memory.
            if (builder.Length < MaxOutputChars)
                builder.Append(buffer, 0, Math.Min(read, MaxOutputChars - builder.Length));
        }
        return builder.ToString();
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch { }
    }
}

/// <summary>
/// Parses the JSON report printed by <c>agy -p /usage --output-format json</c>
/// and returns the shared quota of the Gemini model group.
/// </summary>
public static class AgyUsageParser
{
    public static ServiceUsage Parse(string output)
    {
        using var document = ParseDocument(output);
        var root = document.RootElement;

        if (root.TryGetProperty("status", out var status) &&
            status.ValueKind == JsonValueKind.String &&
            !string.Equals(status.GetString(), "SUCCESS", StringComparison.OrdinalIgnoreCase))
            throw DomainError.AgyCommandFailed($"status {status.GetString()}");

        if (!root.TryGetProperty("command", out var command) ||
            !command.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("groups", out var groups) ||
            groups.ValueKind != JsonValueKind.Array)
            throw DomainError.Decoding("agy usage report has no quota groups");

        foreach (var group in groups.EnumerateArray())
        {
            if (IsGeminiGroup(group) && ParseGroup(group) is { } usage)
                return usage;
        }
        throw DomainError.Decoding("agy usage report has no Gemini quota");
    }

    private static JsonDocument ParseDocument(string output)
    {
        var trimmed = output.Trim();
        try
        {
            return JsonDocument.Parse(trimmed);
        }
        catch (JsonException)
        {
            // Tolerate diagnostic lines printed before the JSON report.
            var line = trimmed
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(l => l.StartsWith('{'));
            if (line == null) throw DomainError.Decoding("agy usage report is not JSON");
            try { return JsonDocument.Parse(line); }
            catch (JsonException e) { throw DomainError.Decoding(e.Message); }
        }
    }

    // Bucket ids ("gemini-5h", "gemini-weekly") are stable; the display name is a fallback.
    private static bool IsGeminiGroup(JsonElement group)
    {
        if ((GetString(group, "name") ?? "").Contains("Gemini", StringComparison.OrdinalIgnoreCase))
            return true;
        return group.TryGetProperty("buckets", out var buckets) &&
               buckets.ValueKind == JsonValueKind.Array &&
               buckets.EnumerateArray().Any(bucket =>
                   (GetString(bucket, "id") ?? "").StartsWith("gemini", StringComparison.OrdinalIgnoreCase));
    }

    private static ServiceUsage? ParseGroup(JsonElement group)
    {
        if (!group.TryGetProperty("buckets", out var buckets) ||
            buckets.ValueKind != JsonValueKind.Array)
            return null;

        RateLimit? fiveHour = null;
        RateLimit? weekly = null;
        foreach (var bucket in buckets.EnumerateArray())
        {
            // Disabled buckets omit the remaining fraction.
            if (!bucket.TryGetProperty("remaining_fraction", out var remaining) ||
                remaining.ValueKind != JsonValueKind.Number)
                continue;

            var utilization = Math.Clamp(1 - remaining.GetDouble(), 0, 1);
            var limit = new RateLimit(utilization, ParseResetTime(GetString(bucket, "reset_time")));
            switch (GetString(bucket, "window"))
            {
                case "5h": fiveHour ??= limit; break;
                case "weekly": weekly ??= limit; break;
            }
        }

        return fiveHour == null && weekly == null
            ? null
            : new ServiceUsage(fiveHour, weekly, null);
    }

    private static DateTime ParseResetTime(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : DateTime.MinValue;

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
