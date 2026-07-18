using Microsoft.Win32;

namespace UsageBeacon.Services;

/// <summary>
/// Windows レジストリの Run キーを使ってログイン時自動起動を管理する。
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "UsageBeacon";
    private const string LegacyAppName = "TokenChecker";

    /// <summary>Migrates the startup entry created by TokenChecker.</summary>
    public static void MigrateLegacyRegistration()
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

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(AppName) is not null ||
                   key?.GetValue(LegacyAppName) is not null;
        }
        set
        {
            // Run キーが存在しないユーザーでも NRE にならないよう CreateSubKey を使う。
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (value)
            {
                var exe = Environment.ProcessPath;
                if (exe is not null)
                {
                    key.SetValue(AppName, $"\"{exe}\"");
                    key.DeleteValue(LegacyAppName, throwOnMissingValue: false);
                }
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
                key.DeleteValue(LegacyAppName, throwOnMissingValue: false);
            }
        }
    }
}
