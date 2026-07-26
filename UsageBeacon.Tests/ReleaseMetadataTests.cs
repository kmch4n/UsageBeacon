using System.Text.RegularExpressions;

namespace UsageBeacon.Tests;

/// <summary>
/// Guards the release invariant that the tag workflow also enforces: the
/// project version and the newest released changelog heading must agree.
/// </summary>
public sealed class ReleaseMetadataTests
{
    [Fact]
    public void CsprojVersion_MatchesTheLatestChangelogRelease()
    {
        var root = RepositoryRoot();

        var csproj = File.ReadAllText(Path.Combine(root, "UsageBeacon", "UsageBeacon.csproj"));
        var version = Regex.Match(csproj, @"<Version>(?<value>[^<]+)</Version>").Groups["value"].Value;

        var changelog = File.ReadAllText(Path.Combine(root, "docs", "CHANGELOG.md"));
        var released = Regex.Match(changelog, @"^## (?<value>\d+\.\d+\.\d+) - ", RegexOptions.Multiline)
            .Groups["value"].Value;

        Assert.False(string.IsNullOrEmpty(version), "UsageBeacon.csproj has no <Version> element.");
        Assert.False(string.IsNullOrEmpty(released), "docs/CHANGELOG.md has no released version heading.");
        Assert.Equal(released, version);
    }

    [Fact]
    public void Changelog_KeepsAnUnreleasedSection()
    {
        var changelog = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "CHANGELOG.md"));

        // The release procedure renames this heading; losing it means pending
        // changes have nowhere to be recorded.
        Assert.Contains("## Unreleased", changelog);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UsageBeacon.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
