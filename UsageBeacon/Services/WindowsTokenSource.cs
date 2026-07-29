using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsageBeacon.Models;

namespace UsageBeacon.Services;

/// <summary>
/// Reads Claude Code OAuth credentials from Windows Credential Manager.
/// Uses P/Invoke for keytar CRED_TYPE_GENERIC entries and falls back to credential files.
/// </summary>
public sealed class WindowsTokenSource : IClaudeCredentialSource
{
    private const string ServiceName = "Claude Code-credentials";
    private const string WslCredentialScript =
        "for p in \"$HOME/.claude/.credentials.json\" \"$HOME/.claude/credentials.json\"; " +
        "do if [ -f \"$p\" ]; then cat -- \"$p\"; exit 0; fi; done; exit 1";
    private static readonly TimeSpan DefaultWslTimeout = TimeSpan.FromSeconds(3);

    private readonly IProcessCommandRunner _processRunner;
    private readonly TimeSpan _wslTimeout;

    public WindowsTokenSource()
        : this(new ProcessCommandRunner(), DefaultWslTimeout)
    {
    }

    internal WindowsTokenSource(
        IProcessCommandRunner processRunner,
        TimeSpan wslTimeout)
    {
        _processRunner = processRunner;
        _wslTimeout = wslTimeout;
    }

    public async Task<string> ReadAccessTokenAsync(CancellationToken ct = default)
        => (await ReadCredentialAsync(ct)).AccessToken;

    public async Task<ClaudeCredential> ReadCredentialAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        ClaudeCredential? expiredCredential = null;

        // Try known Windows Credential Manager target names.
        var username = Environment.UserName;
        var targets = new[]
        {
            ServiceName,
            $"{ServiceName}/{username}",
            $"Claude Code/{username}",
        };

        foreach (var target in targets)
        {
            var json = TryReadCredential(target);
            if (json != null)
            {
                var credential = ParseCredential(json, $"credential-manager:{target}");
                if (credential != null) credential = credential with
                {
                    Origin = new ClaudeCredentialOrigin(
                        ClaudeCredentialOriginKind.CredentialManager,
                        target),
                };
                if (credential?.IsUsableAt(now) == true) return credential;
                expiredCredential = PreferRefreshable(expiredCredential, credential);
            }
        }

        // Fall back to Windows credential files.
        var home    = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var filePaths = new[]
        {
            // Claude Code uses the leading-dot filename in current releases.
            Path.Combine(home, ".claude", ".credentials.json"),
            Path.Combine(home, ".claude", "credentials.json"),
            Path.Combine(appData, "Claude", "credentials.json"),
        };

        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var json = await File.ReadAllTextAsync(path, ct);
                var credential = ParseCredential(json, $"file:{Path.GetFileName(path)}");
                if (credential != null) credential = credential with
                {
                    Origin = new ClaudeCredentialOrigin(
                        ClaudeCredentialOriginKind.WindowsFile,
                        Path.GetFullPath(path)),
                };
                if (credential?.IsUsableAt(now) == true) return credential;
                expiredCredential = PreferRefreshable(expiredCredential, credential);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }

        // Run inside each distribution so a stalled UNC provider cannot block
        // startup and each distribution resolves its own HOME.
        var wslCredential = await ReadWslCredentialAsync(now, ct);
        if (wslCredential?.IsUsableAt(now) == true) return wslCredential;
        expiredCredential = PreferRefreshable(expiredCredential, wslCredential);

        if (expiredCredential != null) return expiredCredential;
        throw DomainError.TokenMissing();
    }

    private static ClaudeCredential? PreferRefreshable(
        ClaudeCredential? current,
        ClaudeCredential? candidate)
    {
        if (candidate == null) return current;
        if (current == null) return candidate;
        var currentRefreshable = !string.IsNullOrWhiteSpace(current.RefreshToken);
        var candidateRefreshable = !string.IsNullOrWhiteSpace(candidate.RefreshToken);
        if (!currentRefreshable && candidateRefreshable) return candidate;
        if (currentRefreshable && candidateRefreshable &&
            current.Origin.Kind != ClaudeCredentialOriginKind.WindowsFile &&
            candidate.Origin.Kind == ClaudeCredentialOriginKind.WindowsFile)
            return candidate;
        return current;
    }

    internal async Task<ClaudeCredential?> ReadWslCredentialAsync(
        DateTimeOffset now,
        CancellationToken ct)
    {
        ProcessCommandResult listResult;
        try
        {
            listResult = await _processRunner.RunAsync(
                "wsl.exe",
                ["--list", "--quiet"],
                Encoding.Unicode,
                _wslTimeout,
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }

        if (listResult.TimedOut || listResult.ExitCode != 0) return null;
        var distros = listResult.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(distro => distro.Trim().Replace("\0", ""))
            .Where(distro => !string.IsNullOrWhiteSpace(distro))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        ClaudeCredential? expiredCredential = null;
        foreach (var distro in distros)
        {
            ProcessCommandResult credentialResult;
            try
            {
                credentialResult = await _processRunner.RunAsync(
                    "wsl.exe",
                    ["--distribution", distro, "--", "sh", "-lc", WslCredentialScript],
                    Encoding.UTF8,
                    _wslTimeout,
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { continue; }

            if (credentialResult.TimedOut ||
                credentialResult.ExitCode != 0 ||
                string.IsNullOrWhiteSpace(credentialResult.StandardOutput))
                continue;

            var source = $"wsl:{distro}";
            var credential = ParseCredential(credentialResult.StandardOutput, source);
            if (credential == null) continue;
            credential = credential with
            {
                Origin = new ClaudeCredentialOrigin(
                    ClaudeCredentialOriginKind.WslFile,
                    source),
            };
            if (credential.IsUsableAt(now)) return credential;
            expiredCredential = PreferRefreshable(expiredCredential, credential);
        }

        return expiredCredential;
    }

    // ── Windows Credential Manager (P/Invoke) ────────────────────────────

    private static string? TryReadCredential(string target)
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var ptr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero)
                return null;

            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                // keytar stores the credential as UTF-16LE.
                var json = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                if (!json.StartsWith('{')) json = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                return json;
            }
            finally
            {
                // Clear the unmanaged copy of the credential.
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(ptr);
        }
    }

    internal static ClaudeCredential? ParseCredential(string json, string source)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<KeychainPayload>(json);
            var oauth = payload?.ClaudeAiOauth;
            if (string.IsNullOrWhiteSpace(oauth?.AccessToken)) return null;

            DateTimeOffset? expiresAt = oauth.ExpiresAt.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(oauth.ExpiresAt.Value)
                : null;
            return new ClaudeCredential(
                oauth.AccessToken,
                oauth.RefreshToken,
                expiresAt,
                oauth.Scopes ?? [],
                source);
        }
        catch { return null; }
    }

    // ── P/Invoke ─────────────────────────────────────────────────────────

    private const uint CRED_TYPE_GENERIC = 1;

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW",
               CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint    Flags;
        public uint    Type;
        public string? TargetName;
        public string? Comment;
        public long    LastWritten;
        public uint    CredentialBlobSize;
        public IntPtr  CredentialBlob;
        public uint    Persist;
        public uint    AttributeCount;
        public IntPtr  Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}

// ── JSON payload ──────────────────────────────────────────────────────────

file sealed class KeychainPayload
{
    [JsonPropertyName("claudeAiOauth")] public OAuthPayload? ClaudeAiOauth { get; init; }
}

file sealed class OAuthPayload
{
    [JsonPropertyName("accessToken")]  public string? AccessToken  { get; init; }
    [JsonPropertyName("refreshToken")] public string? RefreshToken { get; init; }
    [JsonPropertyName("expiresAt")]    public long?   ExpiresAt    { get; init; }
    [JsonPropertyName("scopes")]       public string[]? Scopes     { get; init; }
}
