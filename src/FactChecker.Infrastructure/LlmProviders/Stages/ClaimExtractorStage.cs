using System.Text.Json.Serialization;
using FactChecker.Core.Enums;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.LlmProviders.Stages.Prompts;
using Microsoft.Extensions.Options;

namespace FactChecker.Infrastructure.LlmProviders.Stages;

public sealed class ClaimExtractorStage : IClaimExtractor
{
    private const string StageId = "ClaimExtraction";

    private readonly ILlmClient _client;
    private readonly StageModelOptions _options;

    public ClaimExtractorStage(ILlmClient client, IOptions<StageModelOptions> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<Claim>> ExtractAsync(
        string transcript, ContentDomain domain, int maxClaims, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var systemPrompt = StagePrompts.ClaimExtraction
            .Replace("{maxClaims}", maxClaims.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var userMessage = $"Domain: {domain}\n\nTranscript:\n{transcript}";

        var request = new LlmRequest(
            StageId: StageId,
            Tier: _options.ClaimExtraction,
            SystemPrompt: systemPrompt,
            UserPrompt: userMessage);

        var response = await _client.CompleteAsync(request, ct).ConfigureAwait(false);

        var parsed = StructuredOutputParser.Parse<ClaimExtractionResponse>(response.Content);

        return parsed.Claims
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
