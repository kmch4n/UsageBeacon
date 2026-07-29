using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using UsageBeacon.Providers;
using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class ClaudeCredentialFileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"UsageBeacon.Tests-{Guid.NewGuid():N}");

    public ClaudeCredentialFileStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task PersistRefreshedCredentialAsync_PreservesUnknownFields()
    {
        var path = CreateCredentialFile();
        var original = await ReadCredentialAsync(path);
        var refreshed = original with
        {
            AccessToken = "new-access",
            RefreshToken = "new-refresh",
            ExpiresAt = new DateTimeOffset(2026, 7, 20, 5, 0, 0, TimeSpan.Zero),
            Scopes = ["user:profile", "user:inference"],
        };
        var store = new ClaudeCredentialFileStore();

        var result = await store.PersistRefreshedCredentialAsync(original, refreshed);

        Assert.Equal(ClaudeCredentialPersistenceStatus.Persisted, result);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var oauth = root.GetProperty("claudeAiOauth");
        Assert.Equal("kept-root", root.GetProperty("rootExtension").GetString());
        Assert.Equal("kept-oauth", oauth.GetProperty("oauthExtension").GetString());
        Assert.Equal("new-access", oauth.GetProperty("accessToken").GetString());
        Assert.Equal("new-refresh", oauth.GetProperty("refreshToken").GetString());
        Assert.Equal(
            refreshed.ExpiresAt.Value.ToUnixTimeMilliseconds(),
            oauth.GetProperty("expiresAt").GetInt64());
        Assert.Equal(
            ["user:profile", "user:inference"],
            oauth.GetProperty("scopes").EnumerateArray().Select(value => value.GetString()));
        Assert.Empty(Directory.EnumerateFiles(_directory, ".usagebeacon-*"));
    }

    [Fact]
    public async Task PersistRefreshedCredentialAsync_PreservesFileAccessRules()
    {
        var path = CreateCredentialFile();
        var original = await ReadCredentialAsync(path);
        var before = ReadDacl(path);
        var store = new ClaudeCredentialFileStore();

        var result = await store.PersistRefreshedCredentialAsync(
            original,
            original with { AccessToken = "new-access" });

        var after = ReadDacl(path);
        Assert.Equal(ClaudeCredentialPersistenceStatus.Persisted, result);
        Assert.Equal(before.IsProtected, after.IsProtected);
        Assert.Equal(before.Rules, after.Rules);
    }

    [Fact]
    public async Task PersistRefreshedCredentialAsync_PreservesProtectedAllowAndDenyRules()
    {
        var path = CreateCredentialFile();
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier("S-1-5-21-1-2-3-1001"),
            FileSystemRights.ReadAttributes,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier("S-1-5-21-1-2-3-1002"),
            FileSystemRights.WriteAttributes,
            AccessControlType.Deny));
        new FileInfo(path).SetAccessControl(security);
        var before = ReadDacl(path);
        var original = await ReadCredentialAsync(path);
        var store = new ClaudeCredentialFileStore();

        var result = await store.PersistRefreshedCredentialAsync(
            original,
            original with { AccessToken = "new-access" });

        var after = ReadDacl(path);
        Assert.Equal(ClaudeCredentialPersistenceStatus.Persisted, result);
        Assert.True(after.IsProtected);
        Assert.Equal(before.Rules, after.Rules);
    }

    [Fact]
    public async Task PersistRefreshedCredentialAsync_AllowsUnrotatedRefreshToken()
    {
        var path = CreateCredentialFile();
        var original = await ReadCredentialAsync(path);
        var store = new ClaudeCredentialFileStore();

        var result = await store.PersistRefreshedCredentialAsync(
            original,
            original with { AccessToken = "new-access" });

        var stored = await ReadCredentialAsync(path);
        Assert.Equal(ClaudeCredentialPersistenceStatus.Persisted, result);
        Assert.Equal("new-access", stored.AccessToken);
        Assert.Equal("old-refresh", stored.RefreshToken);
    }

    [Fact]
    public async Task PersistRefreshedCredentialAsync_RejectsAccessTokenConflict()
    {
        var path = CreateCredentialFile();
        var original = await ReadCredentialAsync(path);
        var changedJson = (await File.ReadAllTextAsync(path))
            .Replace("old-access", "external-access", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, changedJson, new UTF8Encoding(false));
        var store = new ClaudeCredentialFileStore();

        var result = await store.PersistRefreshedCredentialAsync(
            original,
            original with { AccessToken = "new-access" });

        Assert.Equal(ClaudeCredentialPersistenceStatus.SourceChanged, result);
        Assert.Contains("external-access", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.EnumerateFiles(_directory, ".usagebeacon-*"));
    }

    [Fact]
    public async Task PersistRefreshedCredentialAsync_RejectsMalformedCredentialFile()
    {
        var path = CreateCredentialFile();
        var original = await ReadCredentialAsync(path);
        await File.WriteAllTextAsync(path, "{", new UTF8Encoding(false));
        var store = new ClaudeCredentialFileStore();

        var result = await store.PersistRefreshedCredentialAsync(
            original,
            original with { AccessToken = "new-access" });

        Assert.Equal(ClaudeCredentialPersistenceStatus.SourceChanged, result);
        Assert.Equal("{", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task PersistRefreshedCredentialAsync_LeavesOriginal_WhenFileIsLocked()
    {
        var path = CreateCredentialFile();
        var original = await ReadCredentialAsync(path);
        var before = await File.ReadAllBytesAsync(path);
        var store = new ClaudeCredentialFileStore();
        var fileLock = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        ClaudeCredentialPersistenceStatus result;
        try
        {
            result = await store.PersistRefreshedCredentialAsync(
                original,
                original with { AccessToken = "new-access" });
        }
        finally
        {
            await fileLock.DisposeAsync();
        }

        Assert.Equal(ClaudeCredentialPersistenceStatus.Failed, result);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.EnumerateFiles(_directory, ".usagebeacon-*"));
    }

    [Fact]
    public async Task FetchAsync_PersistsRotatedCredential_AcrossProviderInstances()
    {
        var path = CreateCredentialFile(expired: true);
        var source = new FileCredentialSource(path);
        var original = await source.ReadCredentialAsync();
        var refreshed = original with
        {
            AccessToken = "new-access",
            RefreshToken = "new-refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        var firstRefresher = new StubTokenRefresher(refreshed);
        var firstApi = new StubUsageApiClient();
        var firstProvider = new ClaudeUsageProvider(
            source,
            firstApi,
            firstRefresher,
            new ClaudeCredentialFileStore());

        await firstProvider.FetchAsync();

        var secondRefresher = new StubTokenRefresher(refreshed);
        var secondApi = new StubUsageApiClient();
        var secondProvider = new ClaudeUsageProvider(
            source,
            secondApi,
            secondRefresher,
            new ClaudeCredentialFileStore());
        await secondProvider.FetchAsync();

        Assert.Equal(1, firstRefresher.CallCount);
        Assert.Equal(0, secondRefresher.CallCount);
        Assert.Equal("new-access", secondApi.LastAccessToken);
        var stored = await source.ReadCredentialAsync();
        Assert.Equal("new-refresh", stored.RefreshToken);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private string CreateCredentialFile(bool expired = false)
    {
        var expiresAt = expired
            ? DateTimeOffset.UtcNow.AddHours(-1)
            : DateTimeOffset.UtcNow.AddHours(1);
        var path = Path.Combine(_directory, ".credentials.json");
        var json = $$"""
            {
                "rootExtension": "kept-root",
                "claudeAiOauth": {
                    "accessToken": "old-access",
                    "refreshToken": "old-refresh",
                    "expiresAt": {{expiresAt.ToUnixTimeMilliseconds()}},
                    "scopes": ["user:profile"],
                    "oauthExtension": "kept-oauth"
                }
            }
            """;
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }

    private static async Task<ClaudeCredential> ReadCredentialAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var credential = WindowsTokenSource.ParseCredential(json, "test-file")
            ?? throw new InvalidOperationException("The test credential is invalid.");
        return credential with
        {
            Origin = new ClaudeCredentialOrigin(
                ClaudeCredentialOriginKind.WindowsFile,
                Path.GetFullPath(path)),
        };
    }

    private static DaclSnapshot ReadDacl(string path)
    {
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        var rules = security
            .GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => new AccessRuleSnapshot(
                rule.IdentityReference.Value,
                rule.AccessControlType,
                rule.FileSystemRights,
                rule.InheritanceFlags,
                rule.PropagationFlags))
            .OrderBy(rule => rule.Identity, StringComparer.Ordinal)
            .ThenBy(rule => rule.AccessControlType)
            .ThenBy(rule => rule.Rights)
            .ToArray();
        return new DaclSnapshot(security.AreAccessRulesProtected, rules);
    }

    private sealed record DaclSnapshot(
        bool IsProtected,
        IReadOnlyList<AccessRuleSnapshot> Rules);

    private sealed record AccessRuleSnapshot(
        string Identity,
        AccessControlType AccessControlType,
        FileSystemRights Rights,
        InheritanceFlags InheritanceFlags,
        PropagationFlags PropagationFlags);

    private sealed class FileCredentialSource(string path) : IClaudeCredentialSource
    {
        public Task<ClaudeCredential> ReadCredentialAsync(CancellationToken ct = default) =>
            ClaudeCredentialFileStoreTests.ReadCredentialAsync(path);
    }

    private sealed class StubTokenRefresher(ClaudeCredential refreshed) : IClaudeTokenRefresher
    {
        public int CallCount { get; private set; }

        public Task<ClaudeCredential> RefreshAsync(
            ClaudeCredential credential,
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(refreshed);
        }
    }

    private sealed class StubUsageApiClient : IAnthropicUsageApiClient
    {
        public string? LastAccessToken { get; private set; }

        public Task<AnthropicUsageDto> FetchAsync(
            string accessToken,
            CancellationToken ct = default)
        {
            LastAccessToken = accessToken;
            return Task.FromResult(new AnthropicUsageDto());
        }
    }
}
