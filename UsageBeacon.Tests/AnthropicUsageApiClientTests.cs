using System.Net;
using System.Net.Http.Headers;
using UsageBeacon.Models;
using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class AnthropicUsageApiClientTests
{
    [Fact]
    public async Task FetchAsync_ReturnsUsage_WhenResponseIsSuccessful()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                    "five_hour": { "utilization": 12.5, "resets_at": "2026-07-18T12:00:00Z" },
                    "seven_day": { "utilization": 34.0, "resets_at": "2026-07-20T12:00:00Z" }
                }
                """),
        });
        var cl = new AnthropicUsageApiClient(new HttpClient(handler));

        var result = await cl.FetchAsync("test-token");

        Assert.Equal(12.5, result.FiveHour?.Utilization);
        Assert.Equal(34.0, result.SevenDay?.Utilization);
        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("test-token", handler.LastRequest?.Headers.Authorization?.Parameter);
        Assert.Contains("oauth-2025-04-20", handler.LastRequest!.Headers.GetValues("anthropic-beta"));
    }

    [Fact]
    public async Task FetchAsync_UsesRetryAfterDelta_WhenRateLimited()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(125));
        var cl = new AnthropicUsageApiClient(new HttpClient(new StubHttpMessageHandler(_ => response)));

        var error = await Assert.ThrowsAsync<DomainError>(() => cl.FetchAsync("test-token"));

        Assert.Equal(DomainErrorKind.AnthropicRateLimited, error.Kind);
        Assert.Equal(125, error.RetryAfterSeconds);
    }

    [Fact]
    public async Task FetchAsync_UsesRetryAfterDate_WhenRateLimited()
    {
        var now = new DateTimeOffset(2026, 7, 18, 5, 0, 0, TimeSpan.Zero);
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddMinutes(10));
        var cl = new AnthropicUsageApiClient(
            new HttpClient(new StubHttpMessageHandler(_ => response)),
            new FixedTimeProvider(now));

        var error = await Assert.ThrowsAsync<DomainError>(() => cl.FetchAsync("test-token"));

        Assert.Equal(600, error.RetryAfterSeconds);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
