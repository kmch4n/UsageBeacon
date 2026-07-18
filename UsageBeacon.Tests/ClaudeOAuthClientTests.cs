using System.Net;
using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class ClaudeOAuthClientTests
{
    [Fact]
    public async Task RefreshAsync_ReturnsRefreshedCredential()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                        "access_token": "new-access",
                        "refresh_token": "new-refresh",
                        "expires_in": 3600,
                        "scope": "user:profile user:inference"
                    }
                    """),
            };
        });
        var now = new DateTimeOffset(2026, 7, 18, 5, 0, 0, TimeSpan.Zero);
        var cl = new ClaudeOAuthClient(
            new HttpClient(handler),
            new FixedTimeProvider(now));
        var credential = new ClaudeCredential(
            "old-access",
            "old-refresh",
            now.AddMinutes(-1),
            ["user:profile"],
            "test");

        var result = await cl.RefreshAsync(credential);

        Assert.Equal("new-access", result.AccessToken);
        Assert.Equal("new-refresh", result.RefreshToken);
        Assert.Equal(now.AddHours(1), result.ExpiresAt);
        Assert.Equal(["user:profile", "user:inference"], result.Scopes);
        Assert.Contains("\"grant_type\":\"refresh_token\"", requestBody);
        Assert.Contains("\"client_id\":\"9d1c250a-e61b-44d9-88ed-5944d1962f5e\"", requestBody);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
