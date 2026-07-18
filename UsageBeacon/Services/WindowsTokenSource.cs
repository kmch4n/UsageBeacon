using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsageBeacon.Models;

namespace UsageBeacon.Services;

/// <summary>
/// Windows 資格情報マネージャーから Claude Code の OAuth トークンを読み取る。
/// keytar が CRED_TYPE_GENERIC で保存した値を P/Invoke で取得する。
/// 見つからない場合は %USERPROFILE%\.claude\credentials.json にフォールバック。
/// </summary>
public sealed class WindowsTokenSource : IClaudeCredentialSource
{
    private const string ServiceName = "Claude Code-credentials";

    public async Task<string> ReadAccessTokenAsync(CancellationToken ct = default)
        => (await ReadCredentialAsync(ct)).AccessToken;

    public async Task<ClaudeCredential> ReadCredentialAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        ClaudeCredential? expiredCredential = null;

        // 1) Windows 資格情報マネージャーを試す（複数のターゲット名）
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
                if (credential?.IsUsableAt(now) == true) return credential;
                expiredCredential = PreferRefreshable(expiredCredential, credential);
            }
        }

        // 2) ファイルにフォールバック
        var home    = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var filePaths = new[]
        {
            // Claude Code が実際に保存する先（ファイル名は先頭ドット付き）
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
                if (credential?.IsUsableAt(now) == true) return credential;
                expiredCredential = PreferRefreshable(expiredCredential, credential);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }

        // 3) WSL ファイルシステムにフォールバック（WSL 内にのみ claude がある場合）
        foreach (var path in GetWslCredentialPaths())
        {
            if (!File.Exists(path)) continue;
            try
            {
                var json = await File.ReadAllTextAsync(path, ct);
                var credential = ParseCredential(json, "wsl-file");
                if (credential?.IsUsableAt(now) == true) return credential;
                expiredCredential = PreferRefreshable(expiredCredential, credential);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }

        if (expiredCredential != null) return expiredCredential;
        throw DomainError.TokenMissing();
    }

    private static ClaudeCredential? PreferRefreshable(
        ClaudeCredential? current,
        ClaudeCredential? candidate)
    {
        if (candidate == null) return current;
        if (current == null) return candidate;
        return string.IsNullOrWhiteSpace(current.RefreshToken) &&
               !string.IsNullOrWhiteSpace(candidate.RefreshToken)
            ? candidate
            : current;
    }

    private static IEnumerable<string> GetWslCredentialPaths()
    {
        string[] distros;
        string wslUser;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("wsl.exe", "--list --quiet")
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.Unicode,
            });
            if (p == null) yield break;
            var raw = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            distros = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                         .Select(d => d.Trim().Replace("\0", ""))
                         .Where(d => !string.IsNullOrWhiteSpace(d))
                         .ToArray();
            if (distros.Length == 0) yield break;
        }
        catch { yield break; }

        try
        {
            using var p = Process.Start(new ProcessStartInfo("wsl.exe", "-- whoami")
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            });
            if (p == null) yield break;
            wslUser = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(3000);
            if (string.IsNullOrWhiteSpace(wslUser)) yield break;
        }
        catch { yield break; }

        foreach (var distro in distros)
        {
            // Windows 11 は \\wsl.localhost\、Windows 10 は \\wsl$\ を使う
            foreach (var prefix in new[] { $@"\\wsl.localhost\{distro}", $@"\\wsl$\{distro}" })
            {
                yield return Path.Combine(prefix, "home", wslUser, ".claude", ".credentials.json");
                yield return Path.Combine(prefix, "home", wslUser, ".claude", "credentials.json");
            }
        }
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
                // keytar は UTF-16LE で保存する
                var json = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                if (!json.StartsWith('{')) json = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                return json;
            }
            finally
            {
                // 資格情報のコピーをメモリ上に残さない
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
