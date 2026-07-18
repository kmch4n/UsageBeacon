using UsageBeacon.Models;
using UsageBeacon.Providers;
using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class ClaudeUsageProviderTests
{
    [Fact]
    public async Task FetchAsync_RefreshesExpiredCredential_BeforeFetchingUsage()
    {
        var expired = new ClaudeCredential(
            "expired",
            "refresh",
            DateTimeOffset.UtcNow.AddHours(-1),
            ["user:profile"],
            "test");
        var refreshed = expired with
        {
            AccessToken = "fresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        var api = new StubUsageApiClient();
        var refresher = new StubTokenRefresher(refreshed);
        var provider = new ClaudeUsageProvider(
            new StubCredentialSource(expired),
            api,
            refresher);

        var result = await provider.FetchAsync();

        Assert.Equal("fresh", api.LastAccessToken);
        Assert.Equal(1, refresher.CallCount);
        Assert.Equal(0.25, result.FiveHour?.Utilization);
    }

    [Fact]
    public async Task FetchAsync_RefreshesAndRetries_WhenUsageApiReturnsUnauthorized()
    {
        var valid = new ClaudeCredential(
            "rejected",
            "refresh",
            DateTimeOffset.UtcNow.AddHours(1),
            ["user:profile"],
            "test");
        var refreshed = valid with { AccessToken = "fresh" };
        var api = new StubUsageApiClient(rejectFirstRequest: true);
        var refresher = new StubTokenRefresher(refreshed);
        var provider = new ClaudeUsageProvider(
            new StubCredentialSource(valid),
            api,
            refresher);

        await provider.FetchAsync();

        Assert.Equal(2, api.CallCount);
        Assert.Equal("fresh", api.LastAccessToken);
        Assert.Equal(1, refresher.CallCount);
    }

    private sealed class StubCredentialSource(ClaudeCredential credential) : IClaudeCredentialSource
    {
        public Task<ClaudeCredential> ReadCredentialAsync(CancellationToken ct = default)
            => Task.FromResult(credential);
    }

    private sealed class StubTokenRefresher(ClaudeCredential credential) : IClaudeTokenRefresher
    {
        public int CallCount { get; private set; }

        public Task<ClaudeCredential> RefreshAsync(
            ClaudeCredential current,
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(credential);
        }
    }

    private sealed class StubUsageApiClient(bool rejectFirstRequest = false) : IAnthropicUsageApiClient
    {
        public int CallCount { get; private set; }
        public string? LastAccessToken { get; private set; }

        public Task<AnthropicUsageDto> FetchAsync(
            string accessToken,
            CancellationToken ct = default)
        {
            CallCount++;
            LastAccessToken = accessToken;
            if (rejectFirstRequest && CallCount == 1)
                throw DomainError.AnthropicUnauthorized();

            return Task.FromResult(new AnthropicUsageDto
            {
                FiveHour = new BucketDto { Utilization = 25 },
            });
        }
    }
}
