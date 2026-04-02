using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FactChecker.Core.Enums;
using FactChecker.Infrastructure.LlmProviders.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace FactChecker.Infrastructure.LlmProviders.Gemini;

/// <summary>
/// Google Gemini API implementation of <see cref="ILlmClient"/>.
/// Uses direct HTTP calls to the Gemini REST API (no Google Cloud SDK).
/// </summary>
public sealed partial class GeminiLlmClient : ILlmClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiLlmClient> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    public GeminiLlmClient(
        IHttpClientFactory httpClientFactory,
        IOptions<GeminiOptions> options,
        ILogger<GeminiLlmClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _retryPipeline = BuildRetryPipeline(_options.MaxRetries);
    }

    /// <inheritdoc />
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var model = ResolveModel(request.Tier);
        var sw = Stopwatch.StartNew();

        var requestBodyJson = BuildRequestBody(request.SystemPrompt, request.UserPrompt, enableSearch: false, request.MaxTokens, request.Temperature);
        var responseJson = await SendRequestAsync(model, requestBodyJson, ct).ConfigureAwait(false);

        var content = ExtractTextContent(responseJson);
        var usage = ExtractTokenUsage(responseJson);

        if (string.IsNullOrEmpty(content))
        {
            var finishReason = ExtractFinishReason(responseJson);
            LogEmptyContent(request.StageId, model, finishReason);
            LogRawResponseDebug(request.StageId, model, responseJson.ToString());
        }

        LogModelResponse(request.StageId, model, usage.InputTokens, usage.OutputTokens, sw.ElapsedMilliseconds);

        return new LlmResponse(content, usage);
    }

    /// <inheritdoc />
    public async Task<LlmSearchResponse> CompleteWithSearchAsync(LlmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var model = ResolveModel(request.Tier);
        var sw = Stopwatch.StartNew();

        var requestBodyJson = BuildRequestBody(request.SystemPrompt, request.UserPrompt, enableSearch: true, request.MaxTokens, request.Temperature);
        var responseJson = await SendRequestAsync(model, requestBodyJson, ct).ConfigureAwait(false);

        var content = ExtractTextContent(responseJson);
        var usage = ExtractTokenUsage(responseJson);
        var sources = GeminiGroundingParser.ExtractSources(responseJson);

        if (string.IsNullOrEmpty(content))
        {
            var finishReason = ExtractFinishReason(responseJson);
            LogEmptyContent(request.StageId, model, finishReason);
            LogRawResponseDebug(request.StageId, model, responseJson.ToString());
        }

        LogModelResponseWithGrounding(request.StageId, model, usage.InputTokens, usage.OutputTokens, sw.ElapsedMilliseconds, sources.Count);

        return new LlmSearchResponse(content, sources, usage);
    }

    private async Task<JsonElement> SendRequestAsync(string model, string requestBodyJson, CancellationToken ct)
    {
        var responseText = await _retryPipeline.ExecuteAsync(
            async token => await SendRawAsync(model, requestBodyJson, token).ConfigureAwait(false),
            ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(responseText);
        // Clone so we can dispose the document
        return doc.RootElement.Clone();
    }

    private async Task<string> SendRawAsync(string model, string requestBodyJson, CancellationToken ct)
    {
        // NOTE: Gemini REST API uses query-param auth by default; the key will appear
        // in server access logs and HTTP diagnostic traces. Rotate keys regularly and
        // ensure structured-log sinks do not capture full URLs at Debug level.
        var url = $"{BaseUrl}{model}:generateContent?key={_options.ApiKey}";

        using var httpClient = _httpClientFactory.CreateClient(nameof(GeminiLlmClient));
        using var content = new StringContent(
            requestBodyJson,
            System.Text.Encoding.UTF8,
            "application/json");

        using var httpResponse = await httpClient.PostAsync(new Uri(url), content, ct).ConfigureAwait(false);
        var rawJson = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
            HandleErrorResponse(httpResponse.StatusCode, rawJson);

        return rawJson;
    }

    internal static string BuildRequestBody(string systemPrompt, string userPrompt, bool enableSearch, int maxTokens, double temperature)
    {
        var body = new System.Text.Json.Nodes.JsonObject
        {
            ["systemInstruction"] = new System.Text.Json.Nodes.JsonObject
            {
                ["parts"] = new System.Text.Json.Nodes.JsonArray(
                    new System.Text.Json.Nodes.JsonObject { ["text"] = systemPrompt })
            },
            ["contents"] = new System.Text.Json.Nodes.JsonArray(
                new System.Text.Json.Nodes.JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new System.Text.Json.Nodes.JsonArray(
                        new System.Text.Json.Nodes.JsonObject { ["text"] = userPrompt })
                }),
            ["generationConfig"] = new System.Text.Json.Nodes.JsonObject
            {
                ["maxOutputTokens"] = maxTokens,
                ["temperature"] = temperature,
                ["responseMimeType"] = "application/json"
            }
        };

        if (enableSearch)
        {
            body["tools"] = new System.Text.Json.Nodes.JsonArray(
                new System.Text.Json.Nodes.JsonObject
                {
                    ["google_search"] = new System.Text.Json.Nodes.JsonObject()
                });
        }

        return body.ToJsonString();
    }

    private static string ExtractTextContent(JsonElement responseJson)
    {
        if (!responseJson.TryGetProperty("candidates", out var candidates) ||
            candidates.GetArrayLength() == 0)
            return string.Empty;

        var firstCandidate = candidates[0];
        if (!firstCandidate.TryGetProperty("content", out var contentObj))
            return string.Empty;

        if (!contentObj.TryGetProperty("parts", out var parts) ||
            parts.GetArrayLength() == 0)
            return string.Empty;

        // Iterate all parts — take the last text part found.
        // Mirrors AnthropicWebSearchParser behavior and handles non-text parts
        // appearing before the model's text response.
        string? lastText = null;
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textProp))
                lastText = textProp.GetString();
        }

        return lastText ?? string.Empty;
    }

    private static string? ExtractFinishReason(JsonElement responseJson)
    {
        if (!responseJson.TryGetProperty("candidates", out var candidates) ||
            candidates.GetArrayLength() == 0)
            return null;
        return candidates[0].TryGetProperty("finishReason", out var reason)
            ? reason.GetString()
            : null;
    }

    private static TokenUsage ExtractTokenUsage(JsonElement responseJson)
    {
        if (!responseJson.TryGetProperty("usageMetadata", out var usage))
            return new TokenUsage(0, 0);

        var inputTokens = usage.TryGetProperty("promptTokenCount", out var promptProp)
            ? promptProp.GetInt32()
            : 0;

        var outputTokens = usage.TryGetProperty("candidatesTokenCount", out var candidatesProp)
            ? candidatesProp.GetInt32()
            : 0;

        return new TokenUsage(inputTokens, outputTokens);
    }

    internal string ResolveModel(ModelTier tier) => tier switch
    {
        ModelTier.Fast => _options.FastModel,
        ModelTier.Standard => _options.StandardModel,
        ModelTier.Premium => _options.PremiumModel,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null)
    };

    private void HandleErrorResponse(HttpStatusCode statusCode, string rawJson)
    {
        string? errorMessage = null;
        string? errorStatus = null;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                errorMessage = error.TryGetProperty("message", out var msgProp)
                    ? msgProp.GetString()
                    : null;
                errorStatus = error.TryGetProperty("status", out var statusProp)
                    ? statusProp.GetString()
                    : null;
            }
        }
        catch (JsonException)
        {
            // Best-effort parse — proceed with null
        }

        var message = errorMessage ?? $"Gemini API returned {(int)statusCode} {statusCode}.";

        // Transient errors — throw HttpRequestException so Polly can retry
        if (statusCode is HttpStatusCode.TooManyRequests
                       or HttpStatusCode.InternalServerError
                       or HttpStatusCode.ServiceUnavailable)
        {
            LogTransientApiError((int)statusCode, errorStatus);
            throw new HttpRequestException(message, inner: null, statusCode);
        }

        // Non-transient errors — throw GeminiApiException directly (no retry)
        LogPermanentApiError((int)statusCode, errorStatus);
        throw new GeminiApiException(statusCode, message, errorStatus);
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
                UseJitter = true,
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

    // ── Source-generated log methods ─────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Gemini [{StageId}] {Model} responded: input={InputTokens}, output={OutputTokens}, latency={ElapsedMs}ms.")]
    private partial void LogModelResponse(string stageId, string model, int inputTokens, int outputTokens, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Gemini [{StageId}] {Model} responded with grounding: input={InputTokens}, output={OutputTokens}, latency={ElapsedMs}ms, sources={SourceCount}.")]
    private partial void LogModelResponseWithGrounding(string stageId, string model, int inputTokens, int outputTokens, long elapsedMs, int sourceCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Gemini [{StageId}] {Model}: response contained no text content (finishReason={FinishReason}). Possible safety/recitation filter or unexpected response structure.")]
    private partial void LogEmptyContent(string stageId, string model, string? finishReason);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Gemini [{StageId}] {Model}: raw API response for empty-content diagnosis: {RawResponse}")]
    private partial void LogRawResponseDebug(string stageId, string model, string rawResponse);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Gemini API {StatusCode} ({ErrorStatus}) — transient, will retry.")]
    private partial void LogTransientApiError(int statusCode, string? errorStatus);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Gemini API {StatusCode} ({ErrorStatus}) — non-transient, aborting.")]
    private partial void LogPermanentApiError(int statusCode, string? errorStatus);
}
