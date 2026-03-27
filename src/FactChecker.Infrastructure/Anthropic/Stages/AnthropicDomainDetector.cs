using System.Text.Json.Serialization;
using FactChecker.Core.Enums;
using FactChecker.Core.Interfaces;
using FactChecker.Infrastructure.Anthropic.Prompts;

namespace FactChecker.Infrastructure.Anthropic.Stages;

public sealed class AnthropicDomainDetector : IDomainDetector
{
    // First ~1000 words is enough context for domain classification
    private const int SnippetWordLimit = 1000;

    private readonly AnthropicClientWrapper _client;

    public AnthropicDomainDetector(AnthropicClientWrapper client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async Task<ContentDomain> DetectAsync(string transcriptSnippet, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transcriptSnippet);

        var snippet = TruncateToWords(transcriptSnippet, SnippetWordLimit);

        var response = await _client.SendAsync<DomainResponse>(
            StagePrompts.DomainDetection,
            snippet,
            ModelTier.Fast,
            maxTokens: 64,
            ct).ConfigureAwait(false);

        return Enum.TryParse<ContentDomain>(response.Domain, ignoreCase: true, out var domain)
            ? domain
            : ContentDomain.General;
    }

    private static string TruncateToWords(string text, int maxWords)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= maxWords
            ? text
            : string.Join(' ', words[..maxWords]);
    }
}

#pragma warning disable CA1812
file sealed record DomainResponse(
    [property: JsonPropertyName("domain")] string Domain);
#pragma warning restore CA1812
