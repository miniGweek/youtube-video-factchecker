using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FactChecker.Core.Enums;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
namespace FactChecker.Infrastructure.LlmProviders.Anthropic;

/// <summary>
/// Anthropic implementation of <see cref="ILlmClient"/>.
/// Delegates to the Anthropic Messages API with model-tier routing,
/// retry with exponential backoff, and web search support.
/// </summary>
public sealed partial class AnthropicLlmClient : ILlmClient
{
    private const string AnthropicApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicLlmClient> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    public AnthropicLlmClient(
        IHttpClientFactory httpClientFactory,
        IOptions<AnthropicOptions> options,
        ILogger<AnthropicLlmClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _retryPipeline = BuildRetryPipeline(_options.MaxRetries);
    }

    /// <inheritdoc/>
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var model = ResolveModel(request.Tier);
        var sw = Stopwatch.StartNew();

        var rawJson = await _retryPipeline.ExecuteAsync(
            async token => await SendRawAsync(request.SystemPrompt, request.UserPrompt, model, request.MaxTokens, request.Temperature, token)
                .ConfigureAwait(false),
            ct).ConfigureAwait(false);

        sw.Stop();

        // Parse the response to extract text content and usage
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        var usage = ExtractUsage(root);
        var content = ExtractFirstTextBlock(root);

        LogCompleteResponse(request.StageId, model, usage.InputTokens, usage.OutputTokens, sw.ElapsedMilliseconds);

        return new LlmResponse(content, usage);
    }

    /// <inheritdoc/>
    public async Task<LlmSearchResponse> CompleteWithSearchAsync(LlmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var model = ResolveModel(request.Tier);
        var sw = Stopwatch.StartNew();

        var rawJson = await _retryPipeline.ExecuteAsync(
            async token => await SendRawWebSearchAsync(request.SystemPrompt, request.UserPrompt, model, request.MaxTokens, request.Temperature, token)
                .ConfigureAwait(false),
            ct).ConfigureAwait(false);

        sw.Stop();

        using var doc = JsonDocument.Parse(rawJson);
        var textContent = AnthropicWebSearchParser.ExtractTextContent(doc);
        var sources = AnthropicWebSearchParser.ExtractSources(doc);
        var usage = AnthropicWebSearchParser.ExtractUsage(doc);

        LogSearchResponse(request.StageId, model, sources.Count, usage.InputTokens, usage.OutputTokens, sw.ElapsedMilliseconds);

        return new LlmSearchResponse(textContent, sources, usage);
    }

    private async Task<string> SendRawAsync(
        string systemPrompt,
        string userMessage,
        string model,
        int maxTokens,
        double temperature,
        CancellationToken ct)
    {
        var requestObj = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["temperature"] = temperature,
            ["system"] = systemPrompt,
            ["messages"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = userMessage
                })
        };

        return await SendHttpRequestAsync(requestObj, ct).ConfigureAwait(false);
    }

    private async Task<string> SendRawWebSearchAsync(
        string systemPrompt,
        string userMessage,
        string model,
        int maxTokens,
        double temperature,
        CancellationToken ct)
    {
        var requestObj = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["temperature"] = temperature,
            ["system"] = systemPrompt,
            ["messages"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = userMessage
                }),
            ["tools"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "web_search_20250305",
                    ["name"] = "web_search"
                })
        };

        return await SendHttpRequestAsync(requestObj, ct, includeWebSearchBeta: true).ConfigureAwait(false);
    }

    private async Task<string> SendHttpRequestAsync(
        JsonObject requestBody,
        CancellationToken ct,
        bool includeWebSearchBeta = false)
    {
        using var httpClient = _httpClientFactory.CreateClient(nameof(AnthropicLlmClient));

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, new Uri(AnthropicApiUrl));
        requestMessage.Headers.Add("x-api-key", _options.ApiKey);
        requestMessage.Headers.Add("anthropic-version", AnthropicVersion);

        if (includeWebSearchBeta)
            requestMessage.Headers.Add("anthropic-beta", "web-search-2025-03-05");

        requestMessage.Content = new StringContent(
            requestBody.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

        using var httpResponse = await httpClient.SendAsync(requestMessage, ct)
            .ConfigureAwait(false);

        var rawJson = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
            HandleErrorResponse(httpResponse.StatusCode, rawJson);

        return rawJson;
    }

    private void HandleErrorResponse(HttpStatusCode statusCode, string rawJson)
    {
        string? errorType = null;
        string? errorMessage = null;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("error", out var errorObj))
            {
                errorType = errorObj.TryGetProperty("type", out var t) ? t.GetString() : null;
                errorMessage = errorObj.TryGetProperty("message", out var m) ? m.GetString() : null;
            }
        }
        catch (JsonException)
        {
            // Best-effort parse
        }

        var message = errorMessage ?? $"Anthropic API returned {(int)statusCode} {statusCode}.";

        // Transient errors: throw HttpRequestException so Polly retries
        if (statusCode is HttpStatusCode.TooManyRequests
                       or HttpStatusCode.InternalServerError
                       or HttpStatusCode.ServiceUnavailable)
        {
            LogTransientApiError((int)statusCode, errorType);
            throw new HttpRequestException(message, inner: null, statusCode);
        }

        // Non-transient: throw AnthropicException directly (no retry)
        LogPermanentApiError((int)statusCode, errorType);
        throw new AnthropicException(statusCode, message, errorType);
    }

    private string ResolveModel(ModelTier tier) => tier switch
    {
        ModelTier.Fast => _options.FastModel,
        ModelTier.Standard => _options.StandardModel,
        ModelTier.Premium => _options.PremiumModel,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null)
    };

    private static TokenUsage ExtractUsage(JsonElement root)
    {
        if (root.TryGetProperty("usage", out var usage))
        {
            var input = usage.TryGetProperty("input_tokens", out var inp) ? inp.GetInt32() : 0;
            var output = usage.TryGetProperty("output_tokens", out var outp) ? outp.GetInt32() : 0;
            return new TokenUsage(input, output);
        }

        return new TokenUsage(0, 0);
    }

    private static string ExtractFirstTextBlock(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var contentArray))
            return string.Empty;

        foreach (var block in contentArray.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) &&
                type.GetString() == "text" &&
                block.TryGetProperty("text", out var text))
            {
                return text.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static ResiliencePipeline BuildRetryPipeline(int maxRetries)
    {
        if (maxRetries <= 0)
            return ResiliencePipeline.Empty;

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = maxRetries,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>(),
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
            })
            .Build();
    }

    // ── Source-generated LoggerMessage methods ──────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Anthropic [{StageId}] {Model} complete: input={InputTokens}, output={OutputTokens}, elapsed={ElapsedMs}ms.")]
    private partial void LogCompleteResponse(string stageId, string model, int inputTokens, int outputTokens, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Anthropic [{StageId}] {Model} search: sources={SourceCount}, input={InputTokens}, output={OutputTokens}, elapsed={ElapsedMs}ms.")]
    private partial void LogSearchResponse(string stageId, string model, int sourceCount, int inputTokens, int outputTokens, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Anthropic API {StatusCode} ({ErrorType}) — transient, will retry.")]
    private partial void LogTransientApiError(int statusCode, string? errorType);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Anthropic API {StatusCode} ({ErrorType}) — non-transient, aborting.")]
    private partial void LogPermanentApiError(int statusCode, string? errorType);
}
