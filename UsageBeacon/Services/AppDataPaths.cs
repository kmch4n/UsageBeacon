using System.IO;

namespace UsageBeacon.Services;

/// <summary>Resolves UsageBeacon storage and migrates the legacy TokenChecker directory.</summary>
public static class AppDataPaths
{
    private const string CurrentDirectoryName = "UsageBeacon";
    private const string LegacyDirectoryName = "TokenChecker";

    public static string DirectoryPath { get; } = ResolveDirectoryPath();

    private static string ResolveDirectoryPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var currentPath = Path.Combine(appData, CurrentDirectoryName);
        var legacyPath = Path.Combine(appData, LegacyDirectoryName);

        if (Directory.Exists(currentPath) || !Directory.Exists(legacyPath))
            return currentPath;

        try
        {
            Directory.Move(legacyPath, currentPath);
            return currentPath;
        }
        catch
        {
            // Keep using the existing directory if another process or policy blocks migration.
            return legacyPath;
        }
    }
}
