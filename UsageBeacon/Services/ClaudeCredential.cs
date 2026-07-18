namespace UsageBeacon.Services;

public sealed record ClaudeCredential(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> Scopes,
    string Source)
{
    private static readonly TimeSpan ExpirationSkew = TimeSpan.FromMinutes(1);

    public bool IsUsableAt(DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        (!ExpiresAt.HasValue || ExpiresAt.Value > now.Add(ExpirationSkew));
}

public interface IClaudeCredentialSource
{
    Task<ClaudeCredential> ReadCredentialAsync(CancellationToken ct = default);
}

public interface IClaudeTokenRefresher
{
    Task<ClaudeCredential> RefreshAsync(
        ClaudeCredential credential,
        CancellationToken ct = default);
}
