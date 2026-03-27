using System.Text.Json;
using System.Text.Json.Serialization;
using FactChecker.Core.Enums;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using FactChecker.Infrastructure.Anthropic.Prompts;

namespace FactChecker.Infrastructure.Anthropic.Stages;

public sealed class AnthropicAssessmentGenerator : IAssessmentGenerator
{
    private readonly AnthropicClientWrapper _client;

    public AnthropicAssessmentGenerator(AnthropicClientWrapper client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async Task<Assessment> GenerateAsync(
        Summary summary,
        IReadOnlyList<FactCheck> factChecks,
        ScoreBreakdown score,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(factChecks);
        ArgumentNullException.ThrowIfNull(score);

        var userMessage = BuildUserMessage(summary, factChecks, score);

        var response = await _client.SendAsync<AssessmentResponse>(
            StagePrompts.Assessment,
            userMessage,
            ModelTier.Fast,
            maxTokens: 512,
            ct).ConfigureAwait(false);

        var recommendation = Enum.TryParse<WatchRecommendation>(
            response.Recommendation, ignoreCase: true, out var rec)
            ? rec
            : WatchRecommendation.WatchWithCaution;

        return new Assessment(
            Recommendation: recommendation,
            Reasoning: response.Reasoning,
            InformationDensity: response.InformationDensity,
            Caveats: response.Caveats ?? []);
    }

    private static string BuildUserMessage(
        Summary summary, IReadOnlyList<FactCheck> factChecks, ScoreBreakdown score)
    {
        var verdictSummary = factChecks
            .GroupBy(f => f.Verdict)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();

        return $"""
            Summary:
            Thesis: {summary.Thesis}
            Domain: {summary.Domain}
            Key Points: {string.Join("; ", summary.KeyPoints)}

            Fact-Check Results:
            {string.Join(", ", verdictSummary)}

            Score:
            Aggregate: {score.AggregateScore:F1}/100
            Accuracy: {score.AccuracyScore:F1}/100
            Source Quality: {score.SourceQualityScore:F1}/100
            Verifiability: {score.VerifiabilityScore:F1}/100
            """;
    }
}

#pragma warning disable CA1812
file sealed record AssessmentResponse(
    [property: JsonPropertyName("recommendation")] string Recommendation,
    [property: JsonPropertyName("reasoning")] string Reasoning,
    [property: JsonPropertyName("informationDensity")] string InformationDensity,
    [property: JsonPropertyName("caveats")] List<string>? Caveats);
#pragma warning restore CA1812
