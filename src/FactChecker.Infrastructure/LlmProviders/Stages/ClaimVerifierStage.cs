using System.Text.Json;
using System.Text.Json.Serialization;
using FactChecker.Core.Enums;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.LlmProviders.Stages.Prompts;
using Microsoft.Extensions.Options;

namespace FactChecker.Infrastructure.LlmProviders.Stages;

public sealed class ClaimVerifierStage : IClaimVerifier
{
    private const string StageId = "ClaimVerification";

    private readonly ILlmClient _client;
    private readonly StageModelOptions _options;

    public ClaimVerifierStage(ILlmClient client, IOptions<StageModelOptions> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _options = options.Value;
    }

    public async Task<FactCheck> VerifyAsync(
        Claim claim, Summary summary, ContentDomain domain, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(summary);

        var userMessage = BuildUserMessage(claim, summary, domain);

        var request = new LlmRequest(
            StageId: StageId,
            Tier: _options.ClaimVerification,
            SystemPrompt: StagePrompts.ClaimVerification,
            UserPrompt: userMessage);

        LlmSearchResponse searchResponse;
        try
        {
            searchResponse = await _client.CompleteWithSearchAsync(request, ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Intentional: any provider failure produces Unverifiable
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return UnverifiableFallback(claim.Id, "LLM provider returned an error during verification.");
        }
#pragma warning restore CA1031

        return ParseVerificationResponse(claim.Id, searchResponse);
    }

    private static string BuildUserMessage(Claim claim, Summary summary, ContentDomain domain)
    {
        return $"""
            Domain: {domain}
            Video Thesis: {summary.Thesis}

            Claim to verify:
            "{claim.Text}"

            Context from video:
            "{claim.Context}"
            """;
    }

    private static FactCheck ParseVerificationResponse(string claimId, LlmSearchResponse searchResponse)
    {
        try
        {
            var response = StructuredOutputParser.Parse<VerificationResponse>(searchResponse.Content);

            var verdict = Enum.TryParse<Verdict>(response.Verdict, ignoreCase: true, out var v)
                ? v
                : Verdict.Unverifiable;

            var confidence = Enum.TryParse<Confidence>(response.Confidence, ignoreCase: true, out var c)
                ? c
                : Confidence.Low;

            // Merge sources from both the LLM's JSON response and the provider's search results.
            // Provider search results (from ILlmClient) take precedence as they are the actual
            // search results returned by the search tool/grounding.
            // Merge sources: provider search results take precedence over LLM JSON sources.
            IReadOnlyList<Source> sources;
            if (searchResponse.Sources.Count > 0)
            {
                sources = searchResponse.Sources
                    .Select(s => new Source(
                        Url: s.Url,
                        Title: s.Title,
                        Snippet: s.Snippet,
                        IsAccessible: false))
                    .ToList()
                    .AsReadOnly();
            }
            else
            {
                sources = (response.Sources ?? [])
                    .Where(s => Uri.TryCreate(s.Url, UriKind.Absolute, out _))
                    .Select(s => new Source(
                        Url: new Uri(s.Url),
                        Title: s.Title,
                        Snippet: s.Snippet,
                        IsAccessible: false))
                    .ToList()
                    .AsReadOnly();
            }

            return new FactCheck(claimId, verdict, confidence, response.Reasoning, sources);
        }
        catch (JsonException)
        {
            return UnverifiableFallback(claimId, "Verification response was not valid JSON.");
        }
    }

    private static FactCheck UnverifiableFallback(string claimId, string reason) =>
        new(claimId, Verdict.Unverifiable, Confidence.Low, reason, []);
}

#pragma warning disable CA1812
file sealed record SourceDto(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("snippet")] string Snippet);

file sealed record VerificationResponse(
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("confidence")] string Confidence,
    [property: JsonPropertyName("reasoning")] string Reasoning,
    [property: JsonPropertyName("sources")] List<SourceDto>? Sources);
#pragma warning restore CA1812
