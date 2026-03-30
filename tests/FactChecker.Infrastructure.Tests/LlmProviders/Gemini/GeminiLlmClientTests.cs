using System.Net;
using System.Text;
using System.Text.Json;
using FactChecker.Core.Enums;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.LlmProviders.Gemini;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace FactChecker.Infrastructure.Tests.LlmProviders.Gemini;

public sealed class GeminiLlmClientTests
{
    private static readonly LlmRequest DefaultRequest = new(
        StageId: "test-stage",
        Tier: ModelTier.Fast,
        SystemPrompt: "You are a test assistant.",
        UserPrompt: "Return JSON.");

    // ── test infrastructure ──────────────────────────────────────────────────

    private static GeminiLlmClient CreateClient(
        HttpMessageHandler handler,
        int maxRetries = 0,
        string fastModel = "gemini-2.5-flash",
        string standardModel = "gemini-2.5-flash",
        string premiumModel = "gemini-2.5-pro")
    {
        var factory = new FakeHttpClientFactory(handler);
        var options = OptionsFactory.Create(new GeminiOptions
        {
            ApiKey = "test-api-key",
            FastModel = fastModel,
            StandardModel = standardModel,
            PremiumModel = premiumModel,
            MaxRetries = maxRetries,
        });
        return new GeminiLlmClient(factory, options, NullLogger<GeminiLlmClient>.Instance);
    }

    private static string LoadFixture(string fileName)
    {
        var path = Path.Combine("LlmProviders", "Gemini", "Fixtures", fileName);
        return File.ReadAllText(path);
    }

    // ── CompleteAsync: happy path ────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_SuccessResponse_ReturnsContentAndUsage()
    {
        var fixture = LoadFixture("complete-success.json");
        var handler = new StaticResponseHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(handler);

        var result = await client.CompleteAsync(DefaultRequest);

        Assert.Equal("{\"domain\": \"Science\"}", result.Content);
        Assert.Equal(150, result.Usage.InputTokens);
        Assert.Equal(25, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task CompleteAsync_EmptyCandidates_ReturnsEmptyContent()
    {
        var fixture = LoadFixture("complete-empty-candidates.json");
        var handler = new StaticResponseHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(handler);

        var result = await client.CompleteAsync(DefaultRequest);

        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task CompleteAsync_NoTextPart_ReturnsEmptyContent()
    {
        var fixture = LoadFixture("complete-no-text-part.json");
        var handler = new StaticResponseHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(handler);

        var result = await client.CompleteAsync(DefaultRequest);

        Assert.Equal(string.Empty, result.Content);
    }

    // ── CompleteWithSearchAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CompleteWithSearchAsync_GroundedResponse_ReturnsSources()
    {
        var fixture = LoadFixture("complete-with-grounding.json");
        var handler = new StaticResponseHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(handler);

        var request = DefaultRequest with { Tier = ModelTier.Premium };
        var result = await client.CompleteWithSearchAsync(request);

        Assert.Contains("Supported", result.Content);
        Assert.Equal(3, result.Sources.Count);
        Assert.Equal(500, result.Usage.InputTokens);
        Assert.Equal(150, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task CompleteWithSearchAsync_NoGrounding_ReturnsEmptySources()
    {
        var fixture = LoadFixture("complete-no-grounding.json");
        var handler = new StaticResponseHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(handler);

        var request = DefaultRequest with { Tier = ModelTier.Premium };
        var result = await client.CompleteWithSearchAsync(request);

        Assert.Empty(result.Sources);
        Assert.Contains("Unverifiable", result.Content);
    }

    [Fact]
    public async Task CompleteWithSearchAsync_RequestIncludesGoogleSearchTool()
    {
        var fixture = LoadFixture("complete-with-grounding.json");
        var capturingHandler = new CapturingHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(capturingHandler);

        var request = DefaultRequest with { Tier = ModelTier.Premium };
        await client.CompleteWithSearchAsync(request);

        Assert.Contains("google_search", capturingHandler.LastRequestBody);
    }

    // ── Model tier routing ──────────────────────────────────────────────────

    [Theory]
    [InlineData(ModelTier.Fast, "gemini-2.5-flash")]
    [InlineData(ModelTier.Standard, "gemini-2.5-flash")]
    [InlineData(ModelTier.Premium, "gemini-2.5-pro")]
    public async Task CompleteAsync_UsesCorrectModelForTier(ModelTier tier, string expectedModel)
    {
        var fixture = LoadFixture("complete-success.json");
        var capturingHandler = new CapturingHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(capturingHandler);

        var request = DefaultRequest with { Tier = tier };
        await client.CompleteAsync(request);

        Assert.Contains(expectedModel, capturingHandler.LastRequestUrl);
    }

    // ── Request format ──────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_SendsCorrectRequestStructure()
    {
        var fixture = LoadFixture("complete-success.json");
        var capturingHandler = new CapturingHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(capturingHandler);

        await client.CompleteAsync(DefaultRequest);

        var body = capturingHandler.LastRequestBody;
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // System instruction
        var systemText = root
            .GetProperty("systemInstruction")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
        Assert.Equal("You are a test assistant.", systemText);

        // User content
        var userText = root
            .GetProperty("contents")[0]
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
        Assert.Equal("Return JSON.", userText);

        // Generation config
        var genConfig = root.GetProperty("generationConfig");
        var maxOutputTokens = genConfig.GetProperty("maxOutputTokens").GetInt32();
        Assert.Equal(4096, maxOutputTokens);

        var temperature = genConfig.GetProperty("temperature").GetDouble();
        Assert.Equal(0.0, temperature);
    }

    [Fact]
    public async Task CompleteAsync_ApiKeyInQueryParameter()
    {
        var fixture = LoadFixture("complete-success.json");
        var capturingHandler = new CapturingHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(capturingHandler);

        await client.CompleteAsync(DefaultRequest);

        Assert.Contains("key=test-api-key", capturingHandler.LastRequestUrl);
    }

    // ── Error handling ──────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_400Error_ThrowsGeminiApiException()
    {
        var fixture = LoadFixture("error-400.json");
        var handler = new StaticResponseHandler(HttpStatusCode.BadRequest, fixture);
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<GeminiApiException>(
            () => client.CompleteAsync(DefaultRequest));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("INVALID_ARGUMENT", ex.ErrorStatus);
    }

    [Fact]
    public async Task CompleteAsync_403Error_ThrowsGeminiApiException()
    {
        var fixture = LoadFixture("error-403.json");
        var handler = new StaticResponseHandler(HttpStatusCode.Forbidden, fixture);
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<GeminiApiException>(
            () => client.CompleteAsync(DefaultRequest));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Equal("PERMISSION_DENIED", ex.ErrorStatus);
    }

    [Fact]
    public async Task CompleteAsync_403Error_DoesNotRetry()
    {
        var fixture = LoadFixture("error-403.json");
        var handler = new StaticResponseHandler(HttpStatusCode.Forbidden, fixture);
        var client = CreateClient(handler, maxRetries: 3);

        await Assert.ThrowsAsync<GeminiApiException>(
            () => client.CompleteAsync(DefaultRequest));

        Assert.Equal(1, handler.CallCount);
    }

    // ── Retry behaviour ─────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_429Error_RetriesAndSucceeds()
    {
        var errorFixture = LoadFixture("error-429.json");
        var successFixture = LoadFixture("complete-success.json");

        var handler = new SequentialResponseHandler(
            (HttpStatusCode.TooManyRequests, errorFixture),
            (HttpStatusCode.OK, successFixture));

        var client = CreateClient(handler, maxRetries: 2);
        var result = await client.CompleteAsync(DefaultRequest);

        Assert.Equal("{\"domain\": \"Science\"}", result.Content);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_500Error_RetriesAndSucceeds()
    {
        var successFixture = LoadFixture("complete-success.json");
        var handler = new SequentialResponseHandler(
            (HttpStatusCode.InternalServerError, "{\"error\":{\"message\":\"Internal error\"}}"),
            (HttpStatusCode.OK, successFixture));

        var client = CreateClient(handler, maxRetries: 2);
        var result = await client.CompleteAsync(DefaultRequest);

        Assert.Equal(2, handler.CallCount);
    }

    // ── Cancellation ────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_CancelledToken_ThrowsOperationCancelled()
    {
        var handler = new DelayedHandler(TimeSpan.FromSeconds(10));
        var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CompleteAsync(DefaultRequest, cts.Token));
    }

    // ── Request body structure ──────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_RequestBodyDoesNotIncludeTools()
    {
        var fixture = LoadFixture("complete-success.json");
        var capturingHandler = new CapturingHandler(HttpStatusCode.OK, fixture);
        var client = CreateClient(capturingHandler);

        await client.CompleteAsync(DefaultRequest);

        Assert.DoesNotContain("google_search", capturingHandler.LastRequestBody);
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
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class CapturingHandler(HttpStatusCode statusCode, string body)
        : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = string.Empty;
        public string LastRequestUrl { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUrl = request.RequestUri?.ToString() ?? string.Empty;
            LastRequestBody = await (request.Content?.ReadAsStringAsync(ct) ?? Task.FromResult(string.Empty));
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
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
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
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
