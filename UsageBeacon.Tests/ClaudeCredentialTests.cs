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
}
