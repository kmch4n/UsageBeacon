using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UsageBeacon.Services;

public sealed class ClaudeCredentialFileStore : IClaudeCredentialStore
{
    private const int ReplaceAttempts = 3;
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public bool CanPersist(ClaudeCredential credential) =>
        credential.Origin is
        {
            Kind: ClaudeCredentialOriginKind.WindowsFile,
            Identifier: not null,
        } origin &&
        Path.IsPathFullyQualified(origin.Identifier) &&
        !origin.Identifier.StartsWith(@"\\", StringComparison.Ordinal);

    public async Task<ClaudeCredentialPersistenceStatus> PersistRefreshedCredentialAsync(
        ClaudeCredential original,
        ClaudeCredential refreshed,
        CancellationToken ct = default)
    {
        if (!CanPersist(original))
            return ClaudeCredentialPersistenceStatus.Unsupported;

        var path = original.Origin.Identifier!;
        for (var attempt = 0; attempt < ReplaceAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await TryPersistOnceAsync(path, original, refreshed, ct);
            if (result != ClaudeCredentialPersistenceStatus.Failed ||
                attempt == ReplaceAttempts - 1)
                return result;

            await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)), ct);
        }

        return ClaudeCredentialPersistenceStatus.Failed;
    }

    private static async Task<ClaudeCredentialPersistenceStatus> TryPersistOnceAsync(
        string path,
        ClaudeCredential original,
        ClaudeCredential refreshed,
        CancellationToken ct)
    {
        string currentJson;
        try
        {
            currentJson = await File.ReadAllTextAsync(path, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ClaudeCredentialPersistenceStatus.Failed;
        }

        var current = WindowsTokenSource.ParseCredential(currentJson, original.Source);
        if (current == null || !HasSameOAuthState(current, original))
            return ClaudeCredentialPersistenceStatus.SourceChanged;

        JsonObject root;
        JsonObject oauth;
        try
        {
            root = JsonNode.Parse(currentJson) as JsonObject
                ?? throw new JsonException();
            oauth = root["claudeAiOauth"] as JsonObject
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return ClaudeCredentialPersistenceStatus.SourceChanged;
        }

        oauth["accessToken"] = refreshed.AccessToken;
        oauth["refreshToken"] = refreshed.RefreshToken;
        oauth["expiresAt"] = refreshed.ExpiresAt?.ToUnixTimeMilliseconds();
        oauth["scopes"] = JsonSerializer.SerializeToNode(refreshed.Scopes, JsonOptions);

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            return ClaudeCredentialPersistenceStatus.Failed;

        var nonce = Guid.NewGuid().ToString("N");
        var tempPath = Path.Combine(directory, $".usagebeacon-{nonce}.tmp");
        var backupPath = Path.Combine(directory, $".usagebeacon-{nonce}.bak");
        var replaced = false;

        try
        {
            await WriteThroughAsync(
                tempPath,
                root.ToJsonString(JsonOptions) + "\n",
                ct);

            var latestJson = await File.ReadAllTextAsync(path, ct);
            var latest = WindowsTokenSource.ParseCredential(latestJson, original.Source);
            if (latest == null || !HasSameOAuthState(latest, original))
                return ClaudeCredentialPersistenceStatus.SourceChanged;

            File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: false);
            replaced = true;
            File.Delete(backupPath);
            return ClaudeCredentialPersistenceStatus.Persisted;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ClaudeCredentialPersistenceStatus.Failed;
        }
        finally
        {
            if (!File.Exists(path) && File.Exists(backupPath))
            {
                try
                {
                    File.Move(backupPath, path);
                }
                catch
                {
                    // The caller retains the refreshed credential in memory.
                }
            }

            TryDelete(tempPath);
            if (replaced || File.Exists(path)) TryDelete(backupPath);
        }
    }

    private static bool HasSameOAuthState(
        ClaudeCredential left,
        ClaudeCredential right) =>
        string.Equals(left.AccessToken, right.AccessToken, StringComparison.Ordinal) &&
        string.Equals(left.RefreshToken, right.RefreshToken, StringComparison.Ordinal) &&
        left.ExpiresAt?.ToUnixTimeMilliseconds() ==
            right.ExpiresAt?.ToUnixTimeMilliseconds() &&
        left.Scopes.SequenceEqual(right.Scopes, StringComparer.Ordinal);

    private static async Task WriteThroughAsync(
        string path,
        string contents,
        CancellationToken ct)
    {
        await using (var emptyStream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough))
        {
            emptyStream.Flush(flushToDisk: true);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough | FileOptions.Asynchronous);
        var bytes = Utf8NoBom.GetBytes(contents);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
        stream.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }
}
