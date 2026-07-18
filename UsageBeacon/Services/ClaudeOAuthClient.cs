using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsageBeacon.Models;

namespace UsageBeacon.Services;

public sealed class ClaudeOAuthClient : IClaudeTokenRefresher
{
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private static readonly string[] DefaultScopes =
    [
        "user:profile",
        "user:inference",
        "user:sessions:claude_code",
        "user:mcp_servers",
        "user:file_upload",
    ];
    private static readonly Uri TokenUrl = new("https://platform.claude.com/v1/oauth/token");
    private static readonly HttpClient SharedHttp = new(
        new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30),
        MaxResponseContentBufferSize = 64 * 1024,
    };

    private readonly HttpClient _http;
    private readonly TimeProvider _timeProvider;

    public ClaudeOAuthClient(HttpClient? http = null, TimeProvider? timeProvider = null)
    {
        _http = http ?? SharedHttp;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ClaudeCredential> RefreshAsync(
        ClaudeCredential credential,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(credential.RefreshToken))
            throw DomainError.AnthropicUnauthorized();

        var requestedScopes = credential.Scopes.Count > 0
            ? credential.Scopes
            : DefaultScopes;
        var requestBody = JsonSerializer.Serialize(new
        {
            grant_type = "refresh_token",
            refresh_token = credential.RefreshToken,
            client_id = ClientId,
            scope = string.Join(" ", requestedScopes),
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw DomainError.Timeout();
        }
        catch (Exception e)
        {
            throw DomainError.Network(e.Message);
        }

        using (response)
        {
            if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or
                System.Net.HttpStatusCode.Unauthorized or
                System.Net.HttpStatusCode.Forbidden)
                throw DomainError.AnthropicUnauthorized();
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                throw DomainError.AnthropicRateLimited(GetRetryAfterSeconds(response));
            if (!response.IsSuccessStatusCode)
                throw DomainError.AnthropicHttp((int)response.StatusCode);

            var body = await response.Content.ReadAsStringAsync(ct);
            OAuthRefreshResponse? payload;
            try
            {
                payload = JsonSerializer.Deserialize<OAuthRefreshResponse>(body);
            }
            catch (JsonException e)
            {
                throw DomainError.Decoding($"Claude OAuth refresh: {e.Message}");
            }

            if (string.IsNullOrWhiteSpace(payload?.AccessToken) || payload.ExpiresIn <= 0)
                throw DomainError.Decoding("Claude OAuth refresh: incomplete response");

            var refreshedScopes = ParseScopes(payload.Scope, credential.Scopes);
            return credential with
            {
                AccessToken = payload.AccessToken,
                RefreshToken = string.IsNullOrWhiteSpace(payload.RefreshToken)
                    ? credential.RefreshToken
                    : payload.RefreshToken,
                ExpiresAt = _timeProvider.GetUtcNow().AddSeconds(payload.ExpiresIn),
                Scopes = refreshedScopes,
            };
        }
    }

    private double? GetRetryAfterSeconds(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta) return Math.Max(0, delta.TotalSeconds);
        if (retryAfter?.Date is { } date)
            return Math.Max(0, (date - _timeProvider.GetUtcNow()).TotalSeconds);
        return null;
    }

    private static IReadOnlyList<string> ParseScopes(
        string? rawScopes,
        IReadOnlyList<string> fallback)
    {
        if (string.IsNullOrWhiteSpace(rawScopes)) return fallback;
        return rawScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class OAuthRefreshResponse
    {
        [JsonPropertyName("access_token")]  public string? AccessToken  { get; init; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; init; }
        [JsonPropertyName("expires_in")]    public int     ExpiresIn    { get; init; }
        [JsonPropertyName("scope")]         public string? Scope        { get; init; }
    }
}
