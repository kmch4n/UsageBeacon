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

        if (key.GetValue(AppName) is null)
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            key.SetValue(AppName, $"\"{exe}\"");
        }

        key.DeleteValue(LegacyAppName, throwOnMissingValue: false);
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(AppName) is not null ||
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
}
