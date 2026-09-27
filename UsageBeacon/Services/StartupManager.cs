using Microsoft.Win32;

namespace UsageBeacon.Services;

/// <summary>
/// Manages startup at sign-in through the Windows registry Run key.
/// </summary>
public interface IStartupManager
{
    bool IsEnabled { get; set; }

    void MigrateLegacyRegistration();
}

public sealed class StartupManager : IStartupManager
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "UsageBeacon";
    private const string LegacyAppName = "TokenChecker";

    /// <summary>Migrates the startup entry created by TokenChecker.</summary>
    public void MigrateLegacyRegistration()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key.GetValue(LegacyAppName) is null) return;

        var exe = Environment.ProcessPath;
        if (exe is null) return;
        if (ShouldReplaceRegistrationDuringLegacyMigration(
                key.GetValue(AppName) as string, exe))
        {
            key.SetValue(AppName, $"\"{exe}\"");
        }

        key.DeleteValue(LegacyAppName, throwOnMissingValue: false);
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return IsCurrentExecutableRegistration(
                       key?.GetValue(AppName) as string,
                       Environment.ProcessPath) ||
                   key?.GetValue(LegacyAppName) is not null;
        }
        set
        {
            // Create the Run key when it does not already exist.
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (value)
            {
                var exe = Environment.ProcessPath;
                if (exe is null)
                    throw new InvalidOperationException("The executable path is unavailable.");
                key.SetValue(AppName, $"\"{exe}\"");
                key.DeleteValue(LegacyAppName, throwOnMissingValue: false);
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
                key.DeleteValue(LegacyAppName, throwOnMissingValue: false);
            }
        }
    }

    internal static bool IsCurrentExecutableRegistration(
        string? registration,
        string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(registration) ||
            string.IsNullOrWhiteSpace(executablePath))
            return false;

        var path = registration.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
            path = path[1..^1];
        return string.Equals(path, executablePath, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldReplaceRegistrationDuringLegacyMigration(
        string? registration,
        string executablePath) =>
        !IsCurrentExecutableRegistration(registration, executablePath);
}
