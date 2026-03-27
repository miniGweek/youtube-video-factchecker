using System.Text.Json;
using System.Text.Json.Serialization;
using FactChecker.Core.Enums;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using FactChecker.Infrastructure.Anthropic.Prompts;

namespace FactChecker.Infrastructure.Anthropic.Stages;

public sealed class AnthropicClaimVerifier : IClaimVerifier
{
    private static readonly AnthropicTool WebSearchTool = new(
        Name: "web_search",
        Description: "Search the web for up-to-date information to verify factual claims.",
        InputSchema: new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "The search query" }
            },
            required = new[] { "query" }
        });

    private readonly AnthropicClientWrapper _client;

    public AnthropicClaimVerifier(AnthropicClientWrapper client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async Task<FactCheck> VerifyAsync(
        Claim claim, Summary summary, ContentDomain domain, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(summary);

        var userMessage = BuildUserMessage(claim, summary, domain);

        // Try tool-use first; fall back to plain completion if tool-use response is complex
        string rawResponse;
        try
        {
            rawResponse = await _client.SendWithToolsAsync(
                StagePrompts.ClaimVerification,
                userMessage,
                [WebSearchTool],
                ModelTier.Standard,
                maxTokens: 2048,
                ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Propagate transient errors — pipeline will mark claim as Unverifiable
            throw;
        }

        return ParseVerificationResponse(claim.Id, rawResponse);
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

    private static FactCheck ParseVerificationResponse(string claimId, string rawResponse)
    {
        // The raw response is the full Anthropic API JSON.
        // Extract the last text block which contains the structured verdict JSON.
        try
        {
            using var doc = JsonDocument.Parse(rawResponse);
            var root = doc.RootElement;

            // Walk content blocks to find the final text block (the JSON verdict)
            if (root.TryGetProperty("content", out var contentArray))
            {
                string? lastText = null;
                foreach (var block in contentArray.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var type) &&
                        type.GetString() == "text" &&
                        block.TryGetProperty("text", out var text))
                    {
                        lastText = text.GetString();
                    }
                }

                if (lastText is not null)
                    return ParseVerdictJson(claimId, lastText);
            }
        }
#pragma warning disable CA1031 // Intentional: any parse failure produces Unverifiable
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fall through to Unverifiable
        }
#pragma warning restore CA1031

        return UnverifiableFallback(claimId, "Could not parse verification response.");
    }

    private static FactCheck ParseVerdictJson(string claimId, string verdictJson)
    {
        try
        {
            var response = StructuredOutputParser.Parse<VerificationResponse>(verdictJson);

            var verdict = Enum.TryParse<Verdict>(response.Verdict, ignoreCase: true, out var v)
                ? v
                : Verdict.Unverifiable;

            var confidence = Enum.TryParse<Confidence>(response.Confidence, ignoreCase: true, out var c)
                ? c
                : Confidence.Low;

            var sources = (response.Sources ?? [])
                .Where(s => Uri.TryCreate(s.Url, UriKind.Absolute, out _))
                .Select(s => new Source(
                    Url: new Uri(s.Url),
                    Title: s.Title,
                    Snippet: s.Snippet,
                    IsAccessible: false)) // Accessibility validated separately by HttpSourceValidator
                .ToList()
                .AsReadOnly();

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
