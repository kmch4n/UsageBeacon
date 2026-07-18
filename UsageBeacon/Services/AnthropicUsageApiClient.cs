using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsageBeacon.Models;

namespace UsageBeacon.Services;

public sealed class AnthropicUsageApiClient
{
    private static readonly Uri UsageUrl =
        new("https://api.anthropic.com/api/oauth/usage");

    // リダイレクトを追従すると Bearer トークンや anthropic-beta ヘッダーが
    // 攻撃者ホストへ送られうるため明示的に無効化する。レスポンスサイズも上限を設ける。
    private static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = 64 * 1024,
    };

    public async Task<AnthropicUsageDto> FetchAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // 終了・キャンセルはネットワークエラーに化けさせない
        }
        catch (OperationCanceledException)
        {
            throw DomainError.Timeout(); // HttpClient.Timeout 経過
        }
        catch (Exception e)
        {
            throw DomainError.Network(e.Message);
        }

        switch ((int)resp.StatusCode)
        {
            case 200: break;
            case 401: throw DomainError.AnthropicUnauthorized();
            case 429:
                double? retryAfter = null;
                if (resp.Headers.TryGetValues("Retry-After", out var vals))
                    retryAfter = double.TryParse(vals.FirstOrDefault(), out var s) ? s : null;
                throw DomainError.AnthropicRateLimited(retryAfter);
            default:
                throw DomainError.AnthropicHttp((int)resp.StatusCode);
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        try
        {
            return JsonSerializer.Deserialize<AnthropicUsageDto>(body, JsonOpts)
                   ?? throw DomainError.Decoding("Anthropic usage: null");
        }
        catch (JsonException e)
        {
            throw DomainError.Decoding($"Anthropic usage: {e.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

// ── DTO ─────────────────────────────────────────────────────────────────────

public sealed class AnthropicUsageDto
{
    [JsonPropertyName("five_hour")]        public BucketDto? FiveHour      { get; init; }
    [JsonPropertyName("seven_day")]        public BucketDto? SevenDay      { get; init; }
    [JsonPropertyName("seven_day_sonnet")] public BucketDto? SevenDaySonnet { get; init; }
}

public sealed class BucketDto
{
    [JsonPropertyName("utilization")] public double? Utilization { get; init; }
    [JsonPropertyName("resets_at")]   public string? ResetsAt    { get; init; }

    public RateLimit? ToRateLimit()
    {
        if (Utilization == null) return null;
        var date = DateTime.MinValue;
        if (ResetsAt != null)
            DateTime.TryParse(ResetsAt, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out date);
        return new RateLimit(Utilization.Value / 100.0, date);
    }
}
