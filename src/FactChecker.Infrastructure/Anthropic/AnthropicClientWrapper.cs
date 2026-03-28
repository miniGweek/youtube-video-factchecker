using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FactChecker.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace FactChecker.Infrastructure.Anthropic;

/// <summary>
/// Shared wrapper around the Anthropic Messages API.
/// Handles model tier routing, retry with exponential backoff, and structured JSON output parsing.
/// </summary>
public sealed partial class AnthropicClientWrapper
{
    private const string AnthropicApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int DefaultMaxTokens = 2048;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicClientWrapper> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    public AnthropicClientWrapper(
        IHttpClientFactory httpClientFactory,
        IOptions<AnthropicOptions> options,
        ILogger<AnthropicClientWrapper> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _retryPipeline = BuildRetryPipeline(_options.MaxRetries);
    }

    /// <summary>
    /// Sends a prompt to the Anthropic API and deserialises the JSON response to <typeparamref name="T"/>.
    /// Retries once with a JSON nudge if the initial response cannot be parsed.
    /// </summary>
    public async Task<T> SendAsync<T>(
        string systemPrompt,
        string userMessage,
        ModelTier tier,
        int maxTokens = DefaultMaxTokens,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(systemPrompt);
        ArgumentNullException.ThrowIfNull(userMessage);

        var model = ResolveModel(tier);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var responseText = await _retryPipeline.ExecuteAsync(
            async token => await SendRawAsync(systemPrompt, userMessage, model, maxTokens, token)
                .ConfigureAwait(false),
            ct).ConfigureAwait(false);

        LogModelResponse(model, sw.ElapsedMilliseconds);

        try
        {
            return StructuredOutputParser.Parse<T>(responseText);
        }
        catch (JsonException)
        {
            LogJsonParseRetry(typeof(T).Name);

            var nudgedSystem = systemPrompt +
                "\n\nIMPORTANT: Your previous response was not valid JSON. " +
                "Respond ONLY with a valid JSON object. Do not include any text outside the JSON.";

            var retryText = await _retryPipeline.ExecuteAsync(
                async token => await SendRawAsync(nudgedSystem, userMessage, model, maxTokens, token)
                    .ConfigureAwait(false),
                ct).ConfigureAwait(false);

            return StructuredOutputParser.Parse<T>(retryText);
        }
    }

    /// <summary>
    /// Sends a prompt with tool definitions enabled and returns the raw JSON response string.
    /// The caller is responsible for parsing tool-use response blocks.
    /// </summary>
    public async Task<string> SendWithToolsAsync(
        string systemPrompt,
        string userMessage,
        IReadOnlyList<AnthropicTool> tools,
        ModelTier tier,
        int maxTokens = DefaultMaxTokens,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(systemPrompt);
        ArgumentNullException.ThrowIfNull(userMessage);
        ArgumentNullException.ThrowIfNull(tools);

        var model = ResolveModel(tier);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = await _retryPipeline.ExecuteAsync(
            async token => await SendRawAsync(systemPrompt, userMessage, model, maxTokens, token, tools)
                .ConfigureAwait(false),
            ct).ConfigureAwait(false);

        LogModelResponse(model, sw.ElapsedMilliseconds);
        return result;
    }

    /// <summary>
    /// Sends a prompt with Anthropic's built-in server-side web search tool enabled.
    /// The API executes searches internally and returns tool_use + tool_result + text blocks
    /// all in a single response (stop_reason: end_turn).
    /// </summary>
    public async Task<string> SendWithBuiltinWebSearchAsync(
        string systemPrompt,
        string userMessage,
        ModelTier tier,
        int maxTokens = DefaultMaxTokens,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(systemPrompt);
        ArgumentNullException.ThrowIfNull(userMessage);

        var model = ResolveModel(tier);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = await _retryPipeline.ExecuteAsync(
            async token => await SendRawWebSearchAsync(systemPrompt, userMessage, model, maxTokens, token)
                .ConfigureAwait(false),
            ct).ConfigureAwait(false);

        LogModelResponse(model, sw.ElapsedMilliseconds);
        return result;
    }

    private async Task<string> SendRawWebSearchAsync(
        string systemPrompt,
        string userMessage,
        string model,
        int maxTokens,
        CancellationToken ct)
    {
        // Built-in tools use a different shape from custom tools (type + name, no description/input_schema).
        // Build the request JSON directly to avoid fighting with the AnthropicRequest serialization model.
        var requestNode = new System.Text.Json.Nodes.JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["system"] = systemPrompt,
            ["messages"] = new System.Text.Json.Nodes.JsonArray(
                new System.Text.Json.Nodes.JsonObject
                {
                    ["role"] = "user",
                    ["content"] = userMessage
                }),
            ["tools"] = new System.Text.Json.Nodes.JsonArray(
                new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "web_search_20250305",
                    ["name"] = "web_search"
                })
        };

        using var content = new System.Net.Http.StringContent(
            requestNode.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

        using var httpClient = _httpClientFactory.CreateClient(nameof(AnthropicClientWrapper));
        httpClient.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
        httpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
        httpClient.DefaultRequestHeaders.Add("anthropic-beta", "web-search-2025-03-05");

        using var httpResponse = await httpClient.PostAsync(new Uri(AnthropicApiUrl), content, ct)
            .ConfigureAwait(false);

        var rawJson = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
            HandleErrorResponse(httpResponse.StatusCode, rawJson);

        return rawJson;
    }

    private async Task<string> SendRawAsync(
        string systemPrompt,
        string userMessage,
        string model,
        int maxTokens,
        CancellationToken ct,
        IReadOnlyList<AnthropicTool>? tools = null)
    {
        var request = new AnthropicRequest(
            Model: model,
            MaxTokens: maxTokens,
            System: systemPrompt,
            Messages: [new AnthropicMessage("user", userMessage)],
            Tools: tools?.Count > 0 ? [.. tools] : null);

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new System.Net.Http.StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

        using var httpClient = _httpClientFactory.CreateClient(nameof(AnthropicClientWrapper));
        httpClient.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
        httpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);

        using var httpResponse = await httpClient.PostAsync(new Uri(AnthropicApiUrl), content, ct)
            .ConfigureAwait(false);

        var rawJson = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
            HandleErrorResponse(httpResponse.StatusCode, rawJson);

        // For tool-use responses, return the full raw JSON so callers can parse tool blocks
        if (tools?.Count > 0)
            return rawJson;

        var response = JsonSerializer.Deserialize<AnthropicResponse>(rawJson, JsonOptions)
            ?? throw new AnthropicException(httpResponse.StatusCode, "Empty response body from Anthropic API.");

        LogApiUsage(response.Usage.InputTokens, response.Usage.OutputTokens);

        var textBlock = response.Content.FirstOrDefault(b => b.Type == "text");
        return textBlock?.Text ?? string.Empty;
    }

    private void HandleErrorResponse(HttpStatusCode statusCode, string rawJson)
    {
        AnthropicErrorResponse? errorBody = null;
        try
        {
            errorBody = JsonSerializer.Deserialize<AnthropicErrorResponse>(rawJson, JsonOptions);
        }
        catch (JsonException)
        {
            // Best-effort parse — proceed with null error body if not valid JSON
        }

        var message = errorBody?.Error?.Message
            ?? $"Anthropic API returned {(int)statusCode} {statusCode}.";

        // Transient errors — throw HttpRequestException so Polly can retry
        if (statusCode is HttpStatusCode.TooManyRequests
                       or HttpStatusCode.InternalServerError
                       or HttpStatusCode.ServiceUnavailable)
        {
            LogTransientApiError((int)statusCode, errorBody?.Error?.Type);
            throw new HttpRequestException(message, inner: null, statusCode);
        }

        // Non-transient errors — throw AnthropicException directly (no retry)
        LogPermanentApiError((int)statusCode, errorBody?.Error?.Type);
        throw new AnthropicException(statusCode, message, errorBody?.Error?.Type);
    }

    private string ResolveModel(ModelTier tier) => tier switch
    {
        ModelTier.Fast => _options.FastModel,
        ModelTier.Standard => _options.StandardModel,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null)
    };

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
            .Build();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Anthropic {Model} responded in {ElapsedMs}ms.")]
    private partial void LogModelResponse(string model, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "JSON parse failed for {TypeName}; retrying with JSON nudge.")]
    private partial void LogJsonParseRetry(string typeName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Anthropic API usage: input={InputTokens}, output={OutputTokens}.")]
    private partial void LogApiUsage(int inputTokens, int outputTokens);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anthropic API {StatusCode} ({ErrorType}) — transient, will retry.")]
    private partial void LogTransientApiError(int statusCode, string? errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Anthropic API {StatusCode} ({ErrorType}) — non-transient, aborting.")]
    private partial void LogPermanentApiError(int statusCode, string? errorType);
}

// ── Anthropic API models ─────────────────────────────────────────────────────
// These types are only ever instantiated via JsonSerializer.Deserialize<T> (reflection).
// CA1812 cannot detect reflection-based instantiation, so it is suppressed here.
#pragma warning disable CA1812

file sealed record AnthropicMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

file sealed record AnthropicRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("system")] string? System,
    [property: JsonPropertyName("messages")] AnthropicMessage[] Messages,
    [property: JsonPropertyName("tools")] AnthropicTool[]? Tools);

file sealed record AnthropicContentBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text);

file sealed record AnthropicUsageBlock(
    [property: JsonPropertyName("input_tokens")] int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens);

file sealed record AnthropicResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("content")] AnthropicContentBlock[] Content,
    [property: JsonPropertyName("stop_reason")] string StopReason,
    [property: JsonPropertyName("usage")] AnthropicUsageBlock Usage);

file sealed record AnthropicErrorDetail(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("message")] string? Message);

file sealed record AnthropicErrorResponse(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("error")] AnthropicErrorDetail? Error);

#pragma warning restore CA1812
