using FactChecker.Core.Enums;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.LlmProviders.Stages;

namespace FactChecker.Infrastructure.Tests.LlmProviders.Stages;

public class AssessmentGeneratorStageTests
{
    private static Summary TestSummary => new(
        Thesis: "Exercise reduces heart disease risk.",
        KeyPoints: ["Regular exercise is beneficial", "30 min/day is sufficient"],
        Domain: ContentDomain.Health);

    private static ScoreBreakdown GoodScore => new(
        AccuracyScore: 85, SourceQualityScore: 90, VerifiabilityScore: 80,
        AggregateScore: 85, ScoreMethod: "v1.0-weighted");

    private static ScoreBreakdown BadScore => new(
        AccuracyScore: 20, SourceQualityScore: 40, VerifiabilityScore: 50,
        AggregateScore: 28, ScoreMethod: "v1.0-weighted");

    private static IReadOnlyList<FactCheck> SupportedFacts =>
    [
        new FactCheck("c1", Verdict.Supported, Confidence.High, "Well supported.", [])
    ];

    private static IReadOnlyList<FactCheck> RefutedFacts =>
    [
        new FactCheck("c1", Verdict.Refuted, Confidence.High, "Contradicted by evidence.", [])
    ];

    private const string WatchResponse = """
        {
          "recommendation": "Watch",
          "reasoning": "The video presents well-supported claims backed by credible research.",
          "informationDensity": "High",
          "caveats": []
        }
        """;

    private const string SkipResponse = """
        {
          "recommendation": "Skip",
          "reasoning": "Multiple central claims were refuted by credible sources.",
          "informationDensity": "High",
          "caveats": ["The claim about vitamin D is refuted", "Statistics cited are incorrect"]
        }
        """;

    [Fact]
    public async Task GenerateAsync_WatchResponse_ReturnsWatchRecommendation()
    {
        var client = new StubLlmClient().WithCompleteResponse(WatchResponse);
        var generator = new AssessmentGeneratorStage(client, StageTestHelper.CreateOptions());

        var result = await generator.GenerateAsync(TestSummary, SupportedFacts, GoodScore);

        Assert.Equal(WatchRecommendation.Watch, result.Recommendation);
        Assert.False(string.IsNullOrEmpty(result.Reasoning));
        Assert.Equal("High", result.InformationDensity);
        Assert.Empty(result.Caveats);
    }

    [Fact]
    public async Task GenerateAsync_SkipResponse_ReturnsCaveats()
    {
        var client = new StubLlmClient().WithCompleteResponse(SkipResponse);
        var generator = new AssessmentGeneratorStage(client, StageTestHelper.CreateOptions());

        var result = await generator.GenerateAsync(TestSummary, RefutedFacts, BadScore);

        Assert.Equal(WatchRecommendation.Skip, result.Recommendation);
        Assert.NotEmpty(result.Caveats);
    }

    [Fact]
    public async Task GenerateAsync_UnknownRecommendation_FallsBackToWatchWithCaution()
    {
        const string unknownRec = """
            {
              "recommendation": "MaybeSortOf",
              "reasoning": "Hard to say.",
              "informationDensity": "Medium",
              "caveats": []
            }
            """;
        var client = new StubLlmClient().WithCompleteResponse(unknownRec);
        var generator = new AssessmentGeneratorStage(client, StageTestHelper.CreateOptions());

        var result = await generator.GenerateAsync(TestSummary, SupportedFacts, GoodScore);

        Assert.Equal(WatchRecommendation.WatchWithCaution, result.Recommendation);
    }

    [Fact]
    public async Task GenerateAsync_NullCaveatsInResponse_ReturnsEmptyList()
    {
        const string noCaveats = """
            {
              "recommendation": "Watch",
              "reasoning": "Good content.",
              "informationDensity": "High",
              "caveats": null
            }
            """;
        var client = new StubLlmClient().WithCompleteResponse(noCaveats);
        var generator = new AssessmentGeneratorStage(client, StageTestHelper.CreateOptions());

        var result = await generator.GenerateAsync(TestSummary, SupportedFacts, GoodScore);

        Assert.Empty(result.Caveats);
    }

    [Fact]
    public async Task GenerateAsync_UsesCorrectModelTier()
    {
        var options = StageTestHelper.CreateOptions(new StageModelOptions { Assessment = ModelTier.Premium });
        var client = new StubLlmClient().WithCompleteResponse(WatchResponse);
        var generator = new AssessmentGeneratorStage(client, options);

        await generator.GenerateAsync(TestSummary, SupportedFacts, GoodScore);

        Assert.NotNull(client.LastRequest);
        Assert.Equal(ModelTier.Premium, client.LastRequest.Tier);
    }

    [Fact]
    public async Task GenerateAsync_SetsCorrectStageId()
    {
        var client = new StubLlmClient().WithCompleteResponse(WatchResponse);
        var generator = new AssessmentGeneratorStage(client, StageTestHelper.CreateOptions());

        await generator.GenerateAsync(TestSummary, SupportedFacts, GoodScore);

        Assert.NotNull(client.LastRequest);
        Assert.Equal("Assessment", client.LastRequest.StageId);
    }
}
