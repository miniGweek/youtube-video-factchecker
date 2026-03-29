using System.Text.Json;
using System.Text.Json.Serialization;
using FactChecker.Core.Enums;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace FactChecker.Infrastructure.Anthropic;

/// <summary>
/// Temporary adapter that wraps the existing <see cref="AnthropicClientWrapper"/> to implement
/// <see cref="ILlmClient"/>. This bridge allows the new provider-agnostic stages to work with
/// the existing Anthropic infrastructure until <c>AnthropicLlmClient</c> is implemented in Task 013.
/// </summary>
public sealed class AnthropicLlmClientAdapter : ILlmClient
{
    private readonly AnthropicClientWrapper _wrapper;
    private readonly AnthropicOptions _options;

    public AnthropicLlmClientAdapter(AnthropicClientWrapper wrapper, IOptions<AnthropicOptions> options)
    {
        ArgumentNullException.ThrowIfNull(wrapper);
        ArgumentNullException.ThrowIfNull(options);
        _wrapper = wrapper;
        _options = options.Value;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var oldTier = MapTier(request.Tier);
        var result = await _wrapper.SendAsync<JsonElement>(
            request.SystemPrompt,
            request.UserPrompt,
            oldTier,
            maxTokens: 2048,
            ct).ConfigureAwait(false);

        // The wrapper already parsed the JSON; serialize it back to a string for the stage to re-parse.
        var content = result.ValueKind == JsonValueKind.Undefined
            ? string.Empty
            : result.GetRawText();

        return new LlmResponse(content, new TokenUsage(0, 0));
    }

    public async Task<LlmSearchResponse> CompleteWithSearchAsync(LlmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var oldTier = MapTier(request.Tier);
        var rawJson = await _wrapper.SendWithBuiltinWebSearchAsync(
            request.SystemPrompt,
            request.UserPrompt,
            oldTier,
            maxTokens: 2048,
            ct).ConfigureAwait(false);

        // Extract text content and sources from the Anthropic response format.
        var (content, sources) = ParseWebSearchResponse(rawJson);

        return new LlmSearchResponse(content, sources, new TokenUsage(0, 0));
    }

    private static ModelTier MapTier(Core.Enums.ModelTier tier) => tier switch
    {
        Core.Enums.ModelTier.Fast => ModelTier.Fast,
        Core.Enums.ModelTier.Standard => ModelTier.Standard,
        // Premium maps to Standard for Anthropic (same model in old config)
        Core.Enums.ModelTier.Premium => ModelTier.Standard,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null)
    };

    private static (string Content, IReadOnlyList<SearchResultSource> Sources) ParseWebSearchResponse(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            string? lastText = null;
            if (root.TryGetProperty("content", out var contentArray))
            {
                foreach (var block in contentArray.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var type) &&
                        type.GetString() == "text" &&
                        block.TryGetProperty("text", out var text))
                    {
                        lastText = text.GetString();
                    }
                }
            }

            // The adapter returns an empty source list because the old Anthropic web search
            // embeds sources in the LLM's JSON text response, not as separate structured data.
            // The ClaimVerifierStage will fall back to parsing sources from the JSON content.
            return (lastText ?? string.Empty, Array.Empty<SearchResultSource>());
        }
        catch (JsonException)
        {
            return (string.Empty, Array.Empty<SearchResultSource>());
        }
    }
}

#pragma warning disable CA1812
file sealed record ContentBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text);
#pragma warning restore CA1812
