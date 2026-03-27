using System.Text.Json.Serialization;
using FactChecker.Core.Enums;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using FactChecker.Infrastructure.Anthropic.Prompts;

namespace FactChecker.Infrastructure.Anthropic.Stages;

public sealed class AnthropicClaimExtractor : IClaimExtractor
{
    private readonly AnthropicClientWrapper _client;

    public AnthropicClaimExtractor(AnthropicClientWrapper client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async Task<IReadOnlyList<Claim>> ExtractAsync(
        string transcript, ContentDomain domain, int maxClaims, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var systemPrompt = StagePrompts.ClaimExtraction
            .Replace("{maxClaims}", maxClaims.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var userMessage = $"Domain: {domain}\n\nTranscript:\n{transcript}";

        var response = await _client.SendAsync<ClaimExtractionResponse>(
            systemPrompt,
            userMessage,
            ModelTier.Standard,
            maxTokens: 2048,
            ct).ConfigureAwait(false);

        return response.Claims
            .Take(maxClaims)
            .Select(c => new Claim(
                Id: c.Id,
                Text: c.Text,
                Context: c.Context,
                Importance: Math.Clamp(c.Importance, 1, 5)))
            .ToList()
            .AsReadOnly();
    }
}

#pragma warning disable CA1812
file sealed record ClaimDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("importance")] int Importance);

file sealed record ClaimExtractionResponse(
    [property: JsonPropertyName("claims")] List<ClaimDto> Claims);
#pragma warning restore CA1812
