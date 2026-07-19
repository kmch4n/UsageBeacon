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
            refresher,
            new StubCredentialStore());

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
            refresher,
            new StubCredentialStore());

        await provider.FetchAsync();

        Assert.Equal(2, api.CallCount);
        Assert.Equal("fresh", api.LastAccessToken);
        Assert.Equal(1, refresher.CallCount);
    }

    [Fact]
    public async Task FetchAsync_DoesNotRefresh_WhenCredentialCannotBePersisted()
    {
        var expired = new ClaudeCredential(
            "expired",
            "refresh",
            DateTimeOffset.UtcNow.AddHours(-1),
            [],
            "credential-manager:test");
        var refresher = new StubTokenRefresher(expired with
        {
            AccessToken = "fresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        });
        var provider = new ClaudeUsageProvider(
            new StubCredentialSource(expired),
            new StubUsageApiClient(),
            refresher,
            new StubCredentialStore(canPersist: false));

        var error = await Assert.ThrowsAsync<DomainError>(() => provider.FetchAsync());

        Assert.Equal(DomainErrorKind.AnthropicUnauthorized, error.Kind);
        Assert.Equal(0, refresher.CallCount);
    }

    [Fact]
    public async Task FetchAsync_RefreshesOnlyOnce_WhenCallsOverlap()
    {
        var expired = new ClaudeCredential(
            "expired",
            "refresh",
            DateTimeOffset.UtcNow.AddHours(-1),
            [],
            "test");
        var refreshed = expired with
        {
            AccessToken = "fresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        var refresher = new StubTokenRefresher(refreshed, TimeSpan.FromMilliseconds(50));
        var provider = new ClaudeUsageProvider(
            new StubCredentialSource(expired),
            new StubUsageApiClient(),
            refresher,
            new StubCredentialStore());

        await Task.WhenAll(provider.FetchAsync(), provider.FetchAsync());

        Assert.Equal(1, refresher.CallCount);
    }

    [Fact]
    public async Task FetchAsync_RetainsRefreshedCredential_WhenPersistenceFails()
    {
        var expired = new ClaudeCredential(
            "expired",
            "refresh",
            DateTimeOffset.UtcNow.AddHours(-1),
            [],
            "test");
        var refreshed = expired with
        {
            AccessToken = "fresh",
            RefreshToken = "rotated",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        var api = new StubUsageApiClient();
        var refresher = new StubTokenRefresher(refreshed);
        var store = new StubCredentialStore(
            status: ClaudeCredentialPersistenceStatus.Failed);
        var provider = new ClaudeUsageProvider(
            new StubCredentialSource(expired),
            api,
            refresher,
            store);

        await provider.FetchAsync();
        await provider.FetchAsync();

        Assert.Equal(1, refresher.CallCount);
        Assert.Equal("fresh", api.LastAccessToken);
        Assert.True(store.CallCount >= 2);
    }

    [Fact]
    public async Task FetchAsync_RefreshesFromPendingCredential_WhenItExpiresBeforePersistence()
    {
        var fileCredential = new ClaudeCredential(
            "expired",
            "file-refresh",
            DateTimeOffset.UtcNow.AddHours(-1),
            [],
            "test");
        var refreshCount = 0;
        var refresher = new StubTokenRefresher(current =>
        {
            refreshCount++;
            return current with
            {
                AccessToken = $"fresh-{refreshCount}",
                RefreshToken = $"rotated-{refreshCount}",
                // The first rotated credential expires immediately so the next
                // fetch must renew it while persistence is still failing.
                ExpiresAt = refreshCount == 1
                    ? DateTimeOffset.UtcNow
                    : DateTimeOffset.UtcNow.AddHours(1),
            };
        });
        var api = new StubUsageApiClient();
        var store = new StubCredentialStore(
            status: ClaudeCredentialPersistenceStatus.Failed);
        var provider = new ClaudeUsageProvider(
            new StubCredentialSource(fileCredential),
            api,
            refresher,
            store);

        await provider.FetchAsync();
        await provider.FetchAsync();

        // The second refresh must use the rotated token held in memory, never
        // the stale on-disk token that was already consumed by the rotation.
        Assert.Equal(["file-refresh", "rotated-1"], refresher.InputRefreshTokens);
        Assert.Equal("fresh-2", api.LastAccessToken);
        // Persistence must still compare against the credential on disk.
        Assert.Equal("file-refresh", store.LastOriginal?.RefreshToken);
        Assert.Equal("rotated-2", store.LastRefreshed?.RefreshToken);
    }

    [Fact]
    public async Task FetchAsync_AdoptsReplacedSourceCredential_WhenPendingCredentialExpires()
    {
        var fileCredential = new ClaudeCredential(
            "expired",
            "file-refresh",
            DateTimeOffset.UtcNow.AddHours(-1),
            [],
            "test");
        var source = new StubCredentialSource(fileCredential);
        var refresher = new StubTokenRefresher(current => current with
        {
            AccessToken = "fresh",
            RefreshToken = "rotated",
            ExpiresAt = DateTimeOffset.UtcNow,
        });
        var api = new StubUsageApiClient();
        var store = new StubCredentialStore(
            status: ClaudeCredentialPersistenceStatus.Failed);
        var provider = new ClaudeUsageProvider(source, api, refresher, store);

        await provider.FetchAsync();
        source.Credential = fileCredential with
        {
            AccessToken = "relogin",
            RefreshToken = "new-refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        await provider.FetchAsync();
        var persistCallsAfterAdoption = store.CallCount;
        await provider.FetchAsync();

        Assert.Equal("relogin", api.LastAccessToken);
        Assert.Equal(1, refresher.CallCount);
        // Adopting the re-login credential clears the pending update.
        Assert.Equal(persistCallsAfterAdoption, store.CallCount);
    }

    private sealed class StubCredentialSource(ClaudeCredential credential) : IClaudeCredentialSource
    {
        public ClaudeCredential Credential { get; set; } = credential;

        public Task<ClaudeCredential> ReadCredentialAsync(CancellationToken ct = default)
            => Task.FromResult(Credential);
    }

    private sealed class StubTokenRefresher : IClaudeTokenRefresher
    {
        private readonly Func<ClaudeCredential, ClaudeCredential> _transform;
        private readonly TimeSpan? _delay;

        public StubTokenRefresher(ClaudeCredential credential, TimeSpan? delay = null)
            : this(_ => credential, delay)
        {
        }

        public StubTokenRefresher(
            Func<ClaudeCredential, ClaudeCredential> transform,
            TimeSpan? delay = null)
        {
            _transform = transform;
            _delay = delay;
        }

        public int CallCount { get; private set; }
        public List<string?> InputRefreshTokens { get; } = [];

        public async Task<ClaudeCredential> RefreshAsync(
            ClaudeCredential current,
            CancellationToken ct = default)
        {
            CallCount++;
            InputRefreshTokens.Add(current.RefreshToken);
            if (_delay.HasValue) await Task.Delay(_delay.Value, ct);
            return _transform(current);
        }
    }

    private sealed class StubCredentialStore(
        bool canPersist = true,
        ClaudeCredentialPersistenceStatus status =
            ClaudeCredentialPersistenceStatus.Persisted) : IClaudeCredentialStore
    {
        public int CallCount { get; private set; }
        public ClaudeCredential? LastOriginal { get; private set; }
        public ClaudeCredential? LastRefreshed { get; private set; }

        public bool CanPersist(ClaudeCredential credential) => canPersist;

        public Task<ClaudeCredentialPersistenceStatus> PersistRefreshedCredentialAsync(
            ClaudeCredential original,
            ClaudeCredential refreshed,
            CancellationToken ct = default)
        {
            CallCount++;
            LastOriginal = original;
            LastRefreshed = refreshed;
            return Task.FromResult(status);
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
