using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsageBeacon.Models;

namespace UsageBeacon.Services;

public interface IAnthropicUsageApiClient
{
    Task<AnthropicUsageDto> FetchAsync(
        string accessToken,
        CancellationToken ct = default);
}

public sealed class AnthropicUsageApiClient : IAnthropicUsageApiClient
{
    private static readonly Uri UsageUrl =
        new("https://api.anthropic.com/api/oauth/usage");

    // リダイレクトを追従すると Bearer トークンや anthropic-beta ヘッダーが
    // 攻撃者ホストへ送られうるため明示的に無効化する。レスポンスサイズも上限を設ける。
    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = 64 * 1024,
    };

    private readonly HttpClient _http;
    private readonly TimeProvider _timeProvider;

    public AnthropicUsageApiClient(HttpClient? http = null, TimeProvider? timeProvider = null)
    {
        _http = http ?? SharedHttp;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AnthropicUsageDto> FetchAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
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

        using (resp)
        {
            switch ((int)resp.StatusCode)
            {
                case 200: break;
                case 401: throw DomainError.AnthropicUnauthorized();
                case 429: throw DomainError.AnthropicRateLimited(GetRetryAfterSeconds(resp));
                default: throw DomainError.AnthropicHttp((int)resp.StatusCode);
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
    }

    private double? GetRetryAfterSeconds(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta) return Math.Max(0, delta.TotalSeconds);
        if (retryAfter?.Date is { } date)
        {
            var now = _timeProvider.GetUtcNow();
            return Math.Max(0, (date - now).TotalSeconds);
        }
        return null;
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
