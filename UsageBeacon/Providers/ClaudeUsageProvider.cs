using UsageBeacon.Models;
using UsageBeacon.Services;

namespace UsageBeacon.Providers;

public sealed class ClaudeUsageProvider : IUsageProvider
{
    private readonly WindowsTokenSource _tokenSource;
    private readonly AnthropicUsageApiClient _api;

    public ClaudeUsageProvider(
        WindowsTokenSource? tokenSource = null,
        AnthropicUsageApiClient? api = null)
    {
        _tokenSource = tokenSource ?? new WindowsTokenSource();
        _api = api ?? new AnthropicUsageApiClient();
    }

    public async Task<ServiceUsage> FetchAsync(CancellationToken ct = default)
    {
        var token = await _tokenSource.ReadAccessTokenAsync(ct);
        var dto   = await _api.FetchAsync(token, ct);

        return new ServiceUsage(
            FiveHour:     dto.FiveHour?.ToRateLimit(),
            Weekly:       dto.SevenDay?.ToRateLimit(),
            WeeklySonnet: dto.SevenDaySonnet?.ToRateLimit());
    }
}
