using System.Net;
using System.Text;
using FactChecker.Core.Enums;
using FactChecker.Infrastructure.LlmProviders.Anthropic;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using AnthropicException = FactChecker.Infrastructure.Anthropic.AnthropicException;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace FactChecker.Infrastructure.Tests.LlmProviders.Anthropic;

public class AnthropicLlmClientTests
{
    // ── test infrastructure ──────────────────────────────────────────────────

    private static AnthropicLlmClient CreateClient(
        HttpMessageHandler handler,
        int maxRetries = 0)
    {
        var factory = new FakeHttpClientFactory(handler);
        var options = OptionsFactory.Create(new AnthropicOptions
        {
            ApiKey = "test-key",
            FastModel = "claude-haiku-4-5-20251001",
            StandardModel = "claude-sonnet-4-20250514",
            PremiumModel = "claude-sonnet-4-20250514",
            MaxRetries = maxRetries
        });
        return new AnthropicLlmClient(factory, options, NullLogger<AnthropicLlmClient>.Instance);
    }

    private static string LoadFixture(string filename)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "LlmProviders", "Anthropic", "Fixtures", filename);
        return File.ReadAllText(path);
    }

    private static LlmRequest MakeRequest(ModelTier tier = ModelTier.Fast) =>
        new("test-stage", tier, "You are a test.", "Test input.");

    // ── CompleteAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_SuccessResponse_ReturnsContentAndUsage()
    {
        var fixture = LoadFixture("complete_success.json");
        var handler = new StaticResponseHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(handler);

        var result = await client.CompleteAsync(MakeRequest());

        Assert.Contains("Science", result.Content);
        Assert.Equal(150, result.Usage.InputTokens);
        Assert.Equal(25, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task CompleteAsync_EmptyContent_ReturnsEmptyString()
    {
        var fixture = LoadFixture("complete_empty.json");
        var handler = new StaticResponseHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(handler);

        var result = await client.CompleteAsync(MakeRequest());

        Assert.Equal(string.Empty, result.Content);
    }

    // ── Model tier routing ──────────────────────────────────────────────────

    [Theory]
    [InlineData(ModelTier.Fast, "claude-haiku-4-5-20251001")]
    [InlineData(ModelTier.Standard, "claude-sonnet-4-20250514")]
    [InlineData(ModelTier.Premium, "claude-sonnet-4-20250514")]
    public async Task CompleteAsync_CorrectModelSentForTier(ModelTier tier, string expectedModel)
    {
        var fixture = LoadFixture("complete_success.json");
        var capturingHandler = new CapturingHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(capturingHandler);

        await client.CompleteAsync(MakeRequest(tier));

        Assert.Contains($"\"model\":\"{expectedModel}\"", capturingHandler.LastRequestBody);
    }

    // ── Temperature ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_IncludesTemperatureInRequest()
    {
        var fixture = LoadFixture("complete_success.json");
        var capturingHandler = new CapturingHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(capturingHandler);

        await client.CompleteAsync(MakeRequest());

        Assert.Contains("\"temperature\":0", capturingHandler.LastRequestBody);
    }

    // ── CompleteWithSearchAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CompleteWithSearchAsync_MultipleSources_ReturnsContentAndSources()
    {
        var fixture = LoadFixture("search_multiple_sources.json");
        var handler = new StaticResponseHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(handler);

        var result = await client.CompleteWithSearchAsync(MakeRequest(ModelTier.Premium));

        Assert.Contains("verdict", result.Content);
        Assert.Equal(2, result.Sources.Count);
        Assert.Equal(new Uri("https://www.example.com/article1"), result.Sources[0].Url);
        Assert.Equal(500, result.Usage.InputTokens);
        Assert.Equal(180, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task CompleteWithSearchAsync_NoSearchResults_ReturnsEmptySources()
    {
        var fixture = LoadFixture("search_no_results.json");
        var handler = new StaticResponseHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(handler);

        var result = await client.CompleteWithSearchAsync(MakeRequest(ModelTier.Premium));

        Assert.Empty(result.Sources);
        Assert.Contains("unable to find", result.Content);
    }

    [Fact]
    public async Task CompleteWithSearchAsync_IncludesWebSearchTool()
    {
        var fixture = LoadFixture("search_no_results.json");
        var capturingHandler = new CapturingHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(capturingHandler);

        await client.CompleteWithSearchAsync(MakeRequest(ModelTier.Premium));

        Assert.Contains("web_search_20250305", capturingHandler.LastRequestBody);
        Assert.Contains("web_search", capturingHandler.LastRequestBody);
    }

    [Fact]
    public async Task CompleteWithSearchAsync_IncludesWebSearchBetaHeader()
    {
        var fixture = LoadFixture("search_no_results.json");
        var capturingHandler = new CapturingHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(capturingHandler);

        await client.CompleteWithSearchAsync(MakeRequest(ModelTier.Premium));

        Assert.Contains("web-search-2025-03-05", capturingHandler.LastBetaHeader);
    }

    // ── Retry behaviour ─────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_429ThenSuccess_RetriesAndReturns()
    {
        var fixture = LoadFixture("complete_success.json");
        var errorFixture = LoadFixture("error_unauthorized.json"); // body doesn't matter for 429
        var handler = new SequentialResponseHandler(
            (HttpStatusCode.TooManyRequests, errorFixture),
            (HttpStatusCode.OK, fixture));

        var client = CreateClient(handler, maxRetries: 2);
        var result = await client.CompleteAsync(MakeRequest());

        Assert.Contains("Science", result.Content);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_500ThenSuccess_RetriesAndReturns()
    {
        var fixture = LoadFixture("complete_success.json");
        var handler = new SequentialResponseHandler(
            (HttpStatusCode.InternalServerError, "{}"),
            (HttpStatusCode.OK, fixture));

        var client = CreateClient(handler, maxRetries: 2);
        var result = await client.CompleteAsync(MakeRequest());

        Assert.Contains("Science", result.Content);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_400Error_ThrowsWithoutRetry()
    {
        var fixture = LoadFixture("error_unauthorized.json");
        var handler = new StaticResponseHandler(HttpStatusCode.Unauthorized, fixture);
        var client = CreateClient(handler, maxRetries: 3);

        var ex = await Assert.ThrowsAsync<AnthropicException>(
            () => client.CompleteAsync(MakeRequest()));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal("authentication_error", ex.ErrorType);
        Assert.Equal(1, handler.CallCount); // No retries
    }

    // ── Cancellation ────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_CancelledToken_ThrowsOperationCancelled()
    {
        var handler = new DelayedHandler(TimeSpan.FromSeconds(10));
        var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CompleteAsync(MakeRequest(), cts.Token));
    }

    [Fact]
    public async Task CompleteWithSearchAsync_CancelledToken_ThrowsOperationCancelled()
    {
        var handler = new DelayedHandler(TimeSpan.FromSeconds(10));
        var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CompleteWithSearchAsync(MakeRequest(), cts.Token));
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
        public string LastBetaHeader { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestBody = await (request.Content?.ReadAsStringAsync(ct) ?? Task.FromResult(string.Empty))
                .ConfigureAwait(false);

            if (request.Headers.TryGetValues("anthropic-beta", out var values))
                LastBetaHeader = string.Join(",", values);

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
            await Task.Delay(delay, ct).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
