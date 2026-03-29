using System.Text.Json.Serialization;
using FactChecker.Core.Enums;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.LlmProviders.Stages.Prompts;
using Microsoft.Extensions.Options;

namespace FactChecker.Infrastructure.LlmProviders.Stages;

public sealed class SummariserStage : ISummariser
{
    private const string StageId = "Summarisation";

    private readonly ILlmClient _client;
    private readonly StageModelOptions _options;

    public SummariserStage(ILlmClient client, IOptions<StageModelOptions> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _options = options.Value;
    }

    public async Task<Summary> SummariseAsync(
        string transcript, ContentDomain domain, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var userMessage = $"Domain: {domain}\n\nTranscript:\n{transcript}";

        var request = new LlmRequest(
            StageId: StageId,
            Tier: _options.Summarisation,
            SystemPrompt: StagePrompts.Summarisation,
            UserPrompt: userMessage);

        var response = await _client.CompleteAsync(request, ct).ConfigureAwait(false);

        var parsed = StructuredOutputParser.Parse<SummaryResponse>(response.Content);

        return new Summary(
            Thesis: parsed.Thesis,
            KeyPoints: parsed.KeyPoints,
            Domain: domain);
    }
}

#pragma warning disable CA1812
file sealed record SummaryResponse(
    [property: JsonPropertyName("thesis")] string Thesis,
    [property: JsonPropertyName("keyPoints")] List<string> KeyPoints);
#pragma warning restore CA1812
