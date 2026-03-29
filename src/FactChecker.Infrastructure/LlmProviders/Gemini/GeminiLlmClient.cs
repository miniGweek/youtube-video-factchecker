using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FactChecker.Core.Enums;
using FactChecker.Infrastructure.LlmProviders.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
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

        var requestBody = BuildRequestBody(request.SystemPrompt, request.UserPrompt, enableSearch: false);
        var responseJson = await SendRequestAsync(model, requestBody, ct).ConfigureAwait(false);

        var content = ExtractTextContent(responseJson);
        var usage = ExtractTokenUsage(responseJson);

        LogModelResponse(model, usage.InputTokens, usage.OutputTokens, sw.ElapsedMilliseconds);

        // Attempt JSON parse retry if the content looks like it should be JSON but fails
        return new LlmResponse(content, usage);
    }

    /// <inheritdoc />
    public async Task<LlmSearchResponse> CompleteWithSearchAsync(LlmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var model = ResolveModel(request.Tier);
        var sw = Stopwatch.StartNew();

        var requestBody = BuildRequestBody(request.SystemPrompt, request.UserPrompt, enableSearch: true);
        var responseJson = await SendRequestAsync(model, requestBody, ct).ConfigureAwait(false);

        var content = ExtractTextContent(responseJson);
        var usage = ExtractTokenUsage(responseJson);
        var sources = GeminiGroundingParser.ExtractSources(responseJson);

        LogModelResponseWithGrounding(model, usage.InputTokens, usage.OutputTokens, sw.ElapsedMilliseconds, sources.Count);

        return new LlmSearchResponse(content, sources, usage);
    }

    /// <summary>
    /// Sends a completion request and returns the raw response content as a string.
    /// On JSON parse failure, retries once with a nudge for valid JSON.
    /// This method is intended for callers that need structured JSON output from the LLM.
    /// </summary>
    internal async Task<string> CompleteWithJsonRetryAsync(LlmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await CompleteAsync(request, ct).ConfigureAwait(false);
        var content = response.Content;

        try
        {
            // Validate it's parseable JSON
            using var doc = JsonDocument.Parse(StructuredOutputParser.ExtractJson(content));
            return content;
        }
        catch (JsonException)
        {
            LogJsonParseRetry(request.StageId);

            var nudgedRequest = request with
            {
                SystemPrompt = request.SystemPrompt +
                    "\n\nIMPORTANT: Your previous response was not valid JSON. " +
                    "Respond ONLY with a valid JSON object. Do not include any text outside the JSON."
            };

            var retryResponse = await CompleteAsync(nudgedRequest, ct).ConfigureAwait(false);
            return retryResponse.Content;
        }
    }

    private async Task<JsonElement> SendRequestAsync(string model, JsonElement requestBody, CancellationToken ct)
    {
        var responseText = await _retryPipeline.ExecuteAsync(
            async token => await SendRawAsync(model, requestBody, token).ConfigureAwait(false),
            ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(responseText);
        // Clone so we can dispose the document
        return doc.RootElement.Clone();
    }

    private async Task<string> SendRawAsync(string model, JsonElement requestBody, CancellationToken ct)
    {
        var url = $"{BaseUrl}{model}:generateContent?key={_options.ApiKey}";

        using var httpClient = _httpClientFactory.CreateClient(nameof(GeminiLlmClient));
        using var content = new StringContent(
            requestBody.GetRawText(),
            System.Text.Encoding.UTF8,
            "application/json");

        using var httpResponse = await httpClient.PostAsync(new Uri(url), content, ct).ConfigureAwait(false);
        var rawJson = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
            HandleErrorResponse(httpResponse.StatusCode, rawJson);

        return rawJson;
    }

    internal static JsonElement BuildRequestBody(string systemPrompt, string userPrompt, bool enableSearch)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            // systemInstruction
            writer.WritePropertyName("systemInstruction");
            writer.WriteStartObject();
            writer.WritePropertyName("parts");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("text", systemPrompt);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();

            // contents
            writer.WritePropertyName("contents");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WritePropertyName("parts");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("text", userPrompt);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            // tools (optional)
            if (enableSearch)
            {
                writer.WritePropertyName("tools");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WritePropertyName("google_search");
                writer.WriteStartObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
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

        var firstPart = parts[0];
        if (firstPart.TryGetProperty("text", out var textProp))
            return textProp.GetString() ?? string.Empty;

        return string.Empty;
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
            .Build();
    }

    // ── Source-generated log methods ─────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Gemini {Model} responded: input={InputTokens}, output={OutputTokens}, latency={ElapsedMs}ms.")]
    private partial void LogModelResponse(string model, int inputTokens, int outputTokens, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Gemini {Model} responded with grounding: input={InputTokens}, output={OutputTokens}, latency={ElapsedMs}ms, sources={SourceCount}.")]
    private partial void LogModelResponseWithGrounding(string model, int inputTokens, int outputTokens, long elapsedMs, int sourceCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "JSON parse failed for stage {StageId}; retrying with JSON nudge.")]
    private partial void LogJsonParseRetry(string stageId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Gemini API {StatusCode} ({ErrorStatus}) — transient, will retry.")]
    private partial void LogTransientApiError(int statusCode, string? errorStatus);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Gemini API {StatusCode} ({ErrorStatus}) — non-transient, aborting.")]
    private partial void LogPermanentApiError(int statusCode, string? errorStatus);
}
