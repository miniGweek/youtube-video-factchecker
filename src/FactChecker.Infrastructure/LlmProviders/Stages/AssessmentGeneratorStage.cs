using System.Text.Json.Serialization;
using FactChecker.Core.Enums;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.LlmProviders.Common;
using FactChecker.Infrastructure.LlmProviders.Stages.Prompts;
using Microsoft.Extensions.Options;

namespace FactChecker.Infrastructure.LlmProviders.Stages;

public sealed class AssessmentGeneratorStage : IAssessmentGenerator
{
    private const string StageId = "Assessment";

    private readonly ILlmClient _client;
    private readonly StageModelOptions _options;

    public AssessmentGeneratorStage(ILlmClient client, IOptions<StageModelOptions> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _options = options.Value;
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

        var request = new LlmRequest(
            StageId: StageId,
            Tier: _options.Assessment,
            SystemPrompt: StagePrompts.Assessment,
            UserPrompt: userMessage);

        var response = await _client.CompleteAsync(request, ct).ConfigureAwait(false);

        var parsed = StructuredOutputParser.Parse<AssessmentResponse>(response.Content);

        var recommendation = Enum.TryParse<WatchRecommendation>(
            parsed.Recommendation, ignoreCase: true, out var rec)
            ? rec
            : WatchRecommendation.WatchWithCaution;

        return new Assessment(
            Recommendation: recommendation,
            Reasoning: parsed.Reasoning,
            InformationDensity: parsed.InformationDensity,
            Caveats: parsed.Caveats ?? []);
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
