using System.Text;
using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class ClaudeCredentialTests
{
    [Fact]
    public void ParseCredential_ReadsExpiryAndScopes()
    {
        var json =
            """
            {
                "claudeAiOauth": {
                    "accessToken": "access",
                    "refreshToken": "refresh",
                    "expiresAt": 1784350800000,
                    "scopes": ["user:profile", "user:inference"]
                }
            }
            """;

        var credential = WindowsTokenSource.ParseCredential(json, "test");

        Assert.NotNull(credential);
        Assert.Equal("access", credential.AccessToken);
        Assert.Equal("refresh", credential.RefreshToken);
        Assert.Equal(new DateTimeOffset(2026, 7, 18, 5, 0, 0, TimeSpan.Zero), credential.ExpiresAt);
        Assert.Equal(["user:profile", "user:inference"], credential.Scopes);
        Assert.Equal("test", credential.Source);
    }

    [Fact]
    public void IsUsableAt_ReturnsFalse_WhenCredentialIsExpired()
    {
        var now = new DateTimeOffset(2026, 7, 18, 5, 0, 0, TimeSpan.Zero);
        var credential = new ClaudeCredential(
            "access",
            "refresh",
            now.AddMinutes(-1),
            [],
            "test");

        Assert.False(credential.IsUsableAt(now));
    }

    [Fact]
    public void IsUsableAt_ReturnsTrue_WhenExpiryIsUnknown()
    {
        var credential = new ClaudeCredential("access", null, null, [], "test");

        Assert.True(credential.IsUsableAt(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task ReadWslCredentialAsync_UsesEachDistroHomeAndContinuesAfterTimeout()
    {
        var runner = new FakeProcessCommandRunner(
            new ProcessCommandResult(0, "Slow Linux\0\r\nMy Distro\0\r\n", TimedOut: false),
            new ProcessCommandResult(-1, "", TimedOut: true),
            new ProcessCommandResult(0, CredentialJson("wsl-access"), TimedOut: false));
        var source = new WindowsTokenSource(runner, TimeSpan.FromMilliseconds(10));

        var credential = await source.ReadWslCredentialAsync(
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.NotNull(credential);
        Assert.Equal("wsl-access", credential.AccessToken);
        Assert.Equal("wsl:My Distro", credential.Origin.Identifier);
        Assert.Equal(3, runner.Commands.Count);
        Assert.Equal(["--distribution", "My Distro", "--", "sh", "-lc",
            "for p in \"$HOME/.claude/.credentials.json\" \"$HOME/.claude/credentials.json\"; " +
            "do if [ -f \"$p\" ]; then cat -- \"$p\"; exit 0; fi; done; exit 1"],
            runner.Commands[2].Arguments);
        Assert.DoesNotContain("wsl-access", credential.Origin.Identifier);
    }

    [Fact]
    public async Task ReadWslCredentialAsync_ReturnsNull_WhenListingTimesOut()
    {
        var runner = new FakeProcessCommandRunner(
            new ProcessCommandResult(-1, "", TimedOut: true));
        var source = new WindowsTokenSource(runner, TimeSpan.FromMilliseconds(10));

        var credential = await source.ReadWslCredentialAsync(
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Null(credential);
        Assert.Single(runner.Commands);
    }

    [Fact]
    public async Task ReadWslCredentialAsync_SkipsNonZeroExitCodes()
    {
        var runner = new FakeProcessCommandRunner(
            new ProcessCommandResult(0, "Broken\r\nWorking\r\n", TimedOut: false),
            new ProcessCommandResult(1, "", TimedOut: false),
            new ProcessCommandResult(0, CredentialJson("working-access"), TimedOut: false));
        var source = new WindowsTokenSource(runner, TimeSpan.FromMilliseconds(10));

        var credential = await source.ReadWslCredentialAsync(
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal("working-access", credential?.AccessToken);
    }

    [Fact]
    public async Task ReadWslCredentialAsync_PropagatesCallerCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var runner = new FakeProcessCommandRunner(
            new OperationCanceledException(cts.Token));
        var source = new WindowsTokenSource(runner, TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => source.ReadWslCredentialAsync(DateTimeOffset.UtcNow, cts.Token));
    }

    [Fact]
    public async Task ProcessCommandRunner_TimesOutWithoutWaitingForProcessOutput()
    {
        var runner = new ProcessCommandRunner();
        var startedAt = DateTime.UtcNow;

        var result = await runner.RunAsync(
            "powershell.exe",
            ["-NoProfile", "-Command", "Start-Sleep -Seconds 5; Write-Output secret"],
            Encoding.UTF8,
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.Empty(result.StandardOutput);
        Assert.True(DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(3));
    }

    private static string CredentialJson(string accessToken) =>
        $$"""
          {
              "claudeAiOauth": {
                  "accessToken": "{{accessToken}}",
                  "refreshToken": "refresh",
                  "expiresAt": 4102444800000,
                  "scopes": []
              }
          }
          """;

    private sealed class FakeProcessCommandRunner : IProcessCommandRunner
    {
        private readonly Queue<object> _results;

        public FakeProcessCommandRunner(params object[] results)
        {
            _results = new Queue<object>(results);
        }

        public List<ProcessCommand> Commands { get; } = [];

        public Task<ProcessCommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            Encoding standardOutputEncoding,
            TimeSpan timeout,
            CancellationToken ct)
        {
            Commands.Add(new ProcessCommand(fileName, arguments.ToArray()));
            var result = _results.Dequeue();
            return result is Exception exception
                ? Task.FromException<ProcessCommandResult>(exception)
                : Task.FromResult((ProcessCommandResult)result);
        }
    }

    private sealed record ProcessCommand(
        string FileName,
        IReadOnlyList<string> Arguments);
}
