using System.Text.Json.Serialization;
using FactChecker.Core.Enums;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using FactChecker.Infrastructure.Anthropic.Prompts;

namespace FactChecker.Infrastructure.Anthropic.Stages;

public sealed class AnthropicSummariser : ISummariser
{
    private readonly AnthropicClientWrapper _client;

    public AnthropicSummariser(AnthropicClientWrapper client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async Task<Summary> SummariseAsync(
        string transcript, ContentDomain domain, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var userMessage = $"Domain: {domain}\n\nTranscript:\n{transcript}";

        var response = await _client.SendAsync<SummaryResponse>(
            StagePrompts.Summarisation,
            userMessage,
            ModelTier.Standard,
            maxTokens: 1024,
            ct).ConfigureAwait(false);

        return new Summary(
            Thesis: response.Thesis,
            KeyPoints: response.KeyPoints,
            Domain: domain);
    }
}

#pragma warning disable CA1812
file sealed record SummaryResponse(
    [property: JsonPropertyName("thesis")] string Thesis,
    [property: JsonPropertyName("keyPoints")] List<string> KeyPoints);
#pragma warning restore CA1812
