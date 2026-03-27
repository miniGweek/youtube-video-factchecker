using System.Net;
using System.Net.Http.Json;
using System.Text;
using FactChecker.Infrastructure.Anthropic;
using FactChecker.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace FactChecker.Infrastructure.Tests.Anthropic;

public class AnthropicClientWrapperTests
{
    private record DomainResult(string Domain);

    // ── test infrastructure ──────────────────────────────────────────────────

    private static AnthropicClientWrapper CreateWrapper(
        HttpMessageHandler handler,
        int maxRetries = 0)
    {
        var factory = new FakeHttpClientFactory(handler);
        var options = OptionsFactory.Create(new AnthropicOptions
        {
            ApiKey = "test-key",
            FastModel = "claude-haiku-4-5-20251001",
            StandardModel = "claude-sonnet-4-20250514",
            MaxRetries = maxRetries
        });
        return new AnthropicClientWrapper(factory, options, NullLogger<AnthropicClientWrapper>.Instance);
    }

    private static string LoadFixture(string filename)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Anthropic", "Fixtures", filename);
        return File.ReadAllText(path);
    }

    // ── happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_SuccessResponse_DeserializesResult()
    {
        var fixture = LoadFixture("success_response.json");
        var handler = new StaticResponseHandler(HttpStatusCode.OK, fixture);

        var wrapper = CreateWrapper(handler);
        var result = await wrapper.SendAsync<DomainResult>(
            "You are a classifier.", "Classify this.", ModelTier.Fast);

        Assert.Equal("Science", result.Domain);
    }

    [Fact]
    public async Task SendAsync_JsonInMarkdownFence_DeserializesResult()
    {
        var fixture = LoadFixture("success_response_markdown_fenced.json");
        var handler = new StaticResponseHandler(HttpStatusCode.OK, fixture);

        var wrapper = CreateWrapper(handler);
        var result = await wrapper.SendAsync<DomainResult>(
            "You are a classifier.", "Classify this.", ModelTier.Fast);

        Assert.Equal("Health", result.Domain);
    }

    // ── model tier routing ───────────────────────────────────────────────────

    [Theory]
    [InlineData(ModelTier.Fast, "claude-haiku-4-5-20251001")]
    [InlineData(ModelTier.Standard, "claude-sonnet-4-20250514")]
    public async Task SendAsync_CorrectModelSentForTier(ModelTier tier, string expectedModel)
    {
        var fixture = LoadFixture("success_response.json");
        var capturingHandler = new CapturingHandler(HttpStatusCode.OK, fixture);
        var wrapper = CreateWrapper(capturingHandler);

        await wrapper.SendAsync<DomainResult>("sys", "user", tier);

        Assert.Contains($"\"model\":\"{expectedModel}\"", capturingHandler.LastRequestBody);
    }

    // ── error handling ───────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_UnauthorizedResponse_ThrowsAnthropicException()
    {
        var fixture = LoadFixture("error_response_unauthorized.json");
        var handler = new StaticResponseHandler(HttpStatusCode.Unauthorized, fixture);
        var wrapper = CreateWrapper(handler);

        var ex = await Assert.ThrowsAsync<AnthropicException>(
            () => wrapper.SendAsync<DomainResult>("sys", "user", ModelTier.Fast));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal("authentication_error", ex.ErrorType);
    }

    [Fact]
    public async Task SendAsync_TransientError_RetriesAndSucceeds()
    {
        // First call returns 429, second returns success
        var fixture = LoadFixture("success_response.json");
        var handler = new SequentialResponseHandler(
            (HttpStatusCode.TooManyRequests, "{}"),
            (HttpStatusCode.OK, fixture));

        var wrapper = CreateWrapper(handler, maxRetries: 2);
        var result = await wrapper.SendAsync<DomainResult>("sys", "user", ModelTier.Fast);

        Assert.Equal("Science", result.Domain);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_PermanentError_DoesNotRetry()
    {
        var fixture = LoadFixture("error_response_unauthorized.json");
        var handler = new StaticResponseHandler(HttpStatusCode.Unauthorized, fixture);
        var wrapper = CreateWrapper(handler, maxRetries: 3);

        await Assert.ThrowsAsync<AnthropicException>(
            () => wrapper.SendAsync<DomainResult>("sys", "user", ModelTier.Fast));

        Assert.Equal(1, handler.CallCount); // No retries for 401
    }

    // ── cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_CancelledToken_ThrowsOperationCancelled()
    {
        var handler = new DelayedHandler(TimeSpan.FromSeconds(10));
        var wrapper = CreateWrapper(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => wrapper.SendAsync<DomainResult>("sys", "user", ModelTier.Fast, ct: cts.Token));
    }

    // ── fake HTTP infrastructure ─────────────────────────────────────────────

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string body)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            ct.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class CapturingHandler(HttpStatusCode statusCode, string body)
        : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestBody = await (request.Content?.ReadAsStringAsync(ct) ?? Task.FromResult(string.Empty));
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class SequentialResponseHandler(
        params (HttpStatusCode StatusCode, string Body)[] responses)
        : HttpMessageHandler
    {
        private int _index;
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            var (statusCode, body) = responses[Math.Min(_index++, responses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class DelayedHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(delay, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
