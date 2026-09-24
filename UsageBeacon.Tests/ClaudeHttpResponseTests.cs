using System.Net;
using System.Text;
using UsageBeacon.Models;
using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class ClaudeHttpResponseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_TimesOutWhileReadingBody_AndNextRequestSucceeds(bool oauth)
    {
        var requests = 0;
        using var http = CreateHttp(() => ++requests == 1
            ? new StalledContent()
            : new StringContent(SuccessBody(oauth)));
        using var cleanup = new CancellationTokenSource();
        try
        {
            var error = await Assert.ThrowsAsync<DomainError>(async () =>
                await InvokeAsync(http, oauth, cleanup.Token).WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Equal(DomainErrorKind.Timeout, error.Kind);
            await InvokeAsync(http, oauth, cleanup.Token);
        }
        finally { cleanup.Cancel(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Request_RejectsOversizedBody(bool oauth, bool knownLength)
    {
        var body = SuccessBody(oauth).PadRight(65537);
        using var http = CreateHttp(() => new BytesContent(body, knownLength));

        var error = await Assert.ThrowsAsync<DomainError>(() => InvokeAsync(http, oauth));

        Assert.Equal(DomainErrorKind.Network, error.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_AcceptsBodyExactlyAtLimit(bool oauth)
    {
        using var http = CreateHttp(() => new BytesContent(SuccessBody(oauth).PadRight(65536), false));

        await InvokeAsync(http, oauth);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_PreservesCallerCancellationDuringBodyRead(bool oauth)
    {
        var content = new StalledContent();
        using var http = CreateHttp(() => content);
        http.Timeout = TimeSpan.FromSeconds(10);
        using var cancellation = new CancellationTokenSource();
        var pending = InvokeAsync(http, oauth, cancellation.Token);
        try
        {
            await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(3)));
        }
        finally { cancellation.Cancel(); }
    }

    [Theory]
    [InlineData(false, 401)]
    [InlineData(true, 401)]
    [InlineData(false, 429)]
    [InlineData(true, 429)]
    public async Task Request_PreservesStatusMapping(bool oauth, int status)
    {
        using var http = new HttpClient(new ResponseHandler(() =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent("{}"),
            };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromSeconds(120));
            return response;
        }));

        var error = await Assert.ThrowsAsync<DomainError>(() => InvokeAsync(http, oauth));

        Assert.Equal(status == 401 ? DomainErrorKind.AnthropicUnauthorized
            : DomainErrorKind.AnthropicRateLimited, error.Kind);
        if (status == 429) Assert.Equal(120d, error.RetryAfterSeconds);
    }

    internal static HttpClient CreateHttp(Func<HttpContent> content) => new(new ResponseHandler(() =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = content() }))
    {
        Timeout = TimeSpan.FromMilliseconds(250),
        MaxResponseContentBufferSize = 64 * 1024,
    };

    private static string SuccessBody(bool oauth) => oauth
        ? """{"access_token":"test-access","refresh_token":"test-refresh","expires_in":3600}"""
        : """{"five_hour":{"utilization":25}}""";

    private static async Task InvokeAsync(HttpClient http, bool oauth, CancellationToken ct = default)
    {
        if (oauth)
        {
            var cl = new ClaudeOAuthClient(http);
            await cl.RefreshAsync(new ClaudeCredential("test-access", "test-refresh",
                DateTimeOffset.UtcNow.AddMinutes(-1), ["user:profile"], "test"), ct);
        }
        else
        {
            var cl = new AnthropicUsageApiClient(http);
            await cl.FetchAsync("test-access", ct);
        }
    }

    private sealed class ResponseHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response());
    }

    internal sealed class StalledContent : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class BytesContent(string body, bool knownLength) : HttpContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(body);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(_bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return knownLength;
        }
    }
}
