using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void LocalDirectoryPath_ResolvesUnderLocalApplicationData()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var path = AppDataPaths.LocalDirectoryPath;

        Assert.Equal(Path.Combine(localAppData, "UsageBeacon"), path);
    }

    [Fact]
    public void LocalDirectoryPath_DiffersFromTheRoamingDirectory()
    {
        // Caches and logs are machine-local; settings roam.
        Assert.NotEqual(AppDataPaths.DirectoryPath, AppDataPaths.LocalDirectoryPath);
    }
}
