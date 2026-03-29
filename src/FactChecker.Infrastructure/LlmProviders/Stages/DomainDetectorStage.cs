using System.Text.Json.Serialization;
using FactChecker.Core.Enums;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.LlmProviders.Stages.Prompts;
using Microsoft.Extensions.Options;

namespace FactChecker.Infrastructure.LlmProviders.Stages;

public sealed class DomainDetectorStage : IDomainDetector
{
    private const string StageId = "DomainDetection";
    private const int SnippetWordLimit = 1000;

    private readonly ILlmClient _client;
    private readonly StageModelOptions _options;

    public DomainDetectorStage(ILlmClient client, IOptions<StageModelOptions> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _options = options.Value;
    }

    public async Task<ContentDomain> DetectAsync(string transcriptSnippet, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transcriptSnippet);

        var snippet = TruncateToWords(transcriptSnippet, SnippetWordLimit);

        var request = new LlmRequest(
            StageId: StageId,
            Tier: _options.DomainDetection,
            SystemPrompt: StagePrompts.DomainDetection,
            UserPrompt: snippet);

        var response = await _client.CompleteAsync(request, ct).ConfigureAwait(false);

        var parsed = StructuredOutputParser.Parse<DomainResponse>(response.Content);

        return Enum.TryParse<ContentDomain>(parsed.Domain, ignoreCase: true, out var domain)
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
