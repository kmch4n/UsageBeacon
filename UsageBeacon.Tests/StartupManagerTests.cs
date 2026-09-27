using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class StartupManagerTests
{
    [Theory]
    [InlineData("\"C:\\Apps\\UsageBeacon.exe\"", true)]
    [InlineData("C:\\Apps\\UsageBeacon.exe", true)]
    [InlineData("\"C:\\Old\\UsageBeacon.exe\"", false)]
    [InlineData("\"C:\\Apps\\UsageBeacon.exe\" --other", false)]
    [InlineData("", false)]
    public void IsCurrentExecutableRegistration_RejectsStaleOrDifferentCommands(
        string registration,
        bool expected)
    {
        Assert.Equal(expected, StartupManager.IsCurrentExecutableRegistration(
            registration, "C:\\Apps\\UsageBeacon.exe"));
    }

    [Theory]
    [InlineData("\"C:\\Old\\UsageBeacon.exe\"", true)]
    [InlineData("\"C:\\Apps\\UsageBeacon.exe\"", false)]
    [InlineData(null, true)]
    public void ShouldReplaceRegistrationDuringLegacyMigration_RepairsStaleEntry(
        string? registration,
        bool expected)
    {
        Assert.Equal(expected, StartupManager.ShouldReplaceRegistrationDuringLegacyMigration(
            registration, "C:\\Apps\\UsageBeacon.exe"));
    }
}
