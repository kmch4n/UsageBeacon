using UsageBeacon.Models;
using UsageBeacon.Services;

namespace UsageBeacon.Providers;

public sealed class ClaudeUsageProvider : IUsageProvider
{
    private readonly IClaudeCredentialSource _credentialSource;
    private readonly IAnthropicUsageApiClient _api;
    private readonly IClaudeTokenRefresher _tokenRefresher;
    private ClaudeCredential? _cachedCredential;

    public ClaudeUsageProvider(
        IClaudeCredentialSource? credentialSource = null,
        IAnthropicUsageApiClient? api = null,
        IClaudeTokenRefresher? tokenRefresher = null)
    {
        _credentialSource = credentialSource ?? new WindowsTokenSource();
        _api = api ?? new AnthropicUsageApiClient();
        _tokenRefresher = tokenRefresher ?? new ClaudeOAuthClient();
    }

    public async Task<ServiceUsage> FetchAsync(CancellationToken ct = default)
    {
        var credential = await GetUsableCredentialAsync(ct);
        AnthropicUsageDto dto;
        try
        {
            dto = await _api.FetchAsync(credential.AccessToken, ct);
        }
        catch (DomainError e) when (
            e.Kind == DomainErrorKind.AnthropicUnauthorized &&
            !string.IsNullOrWhiteSpace(credential.RefreshToken))
        {
            credential = await _tokenRefresher.RefreshAsync(credential, ct);
            _cachedCredential = credential;
            dto = await _api.FetchAsync(credential.AccessToken, ct);
        }

        return new ServiceUsage(
            FiveHour:     dto.FiveHour?.ToRateLimit(),
            Weekly:       dto.SevenDay?.ToRateLimit(),
            WeeklySonnet: dto.SevenDaySonnet?.ToRateLimit());
    }

    private async Task<ClaudeCredential> GetUsableCredentialAsync(CancellationToken ct)
    {
        var credential = _cachedCredential?.IsUsableAt(DateTimeOffset.UtcNow) == true
            ? _cachedCredential
            : await _credentialSource.ReadCredentialAsync(ct);
        if (credential.IsUsableAt(DateTimeOffset.UtcNow))
        {
            _cachedCredential = credential;
            return credential;
        }

        credential = await _tokenRefresher.RefreshAsync(credential, ct);
        _cachedCredential = credential;
        return credential;
    }
}
