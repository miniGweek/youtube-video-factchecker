using FactChecker.Core.Enums;
using FactChecker.Core.Models;
using FactChecker.Infrastructure.Anthropic.Stages;

namespace FactChecker.Infrastructure.Tests.Anthropic.Stages;

public class AnthropicAssessmentGeneratorTests : StageTestBase
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

    [Fact]
    public async Task GenerateAsync_WatchResponse_ReturnsWatchRecommendation()
    {
        var wrapper = CreateWrapper(LoadFixture("assessment_response_watch.json"));
        var generator = new AnthropicAssessmentGenerator(wrapper);

        var result = await generator.GenerateAsync(TestSummary, SupportedFacts, GoodScore);

        Assert.Equal(WatchRecommendation.Watch, result.Recommendation);
        Assert.False(string.IsNullOrEmpty(result.Reasoning));
        Assert.Equal("High", result.InformationDensity);
        Assert.Empty(result.Caveats);
    }

    [Fact]
    public async Task GenerateAsync_SkipResponse_ReturnsCaveats()
    {
        var wrapper = CreateWrapper(LoadFixture("assessment_response_skip.json"));
        var generator = new AnthropicAssessmentGenerator(wrapper);

        var result = await generator.GenerateAsync(TestSummary, RefutedFacts, BadScore);

        Assert.Equal(WatchRecommendation.Skip, result.Recommendation);
        Assert.NotEmpty(result.Caveats);
    }

    [Fact]
    public async Task GenerateAsync_UnknownRecommendation_FallsBackToWatchWithCaution()
    {
        // Fixture with an unrecognised recommendation value
        const string fixture = """
            {
              "id": "msg_assess_bad",
              "type": "message",
              "role": "assistant",
              "content": [{ "type": "text", "text": "{\"recommendation\":\"MaybeSortOf\",\"reasoning\":\"Hard to say.\",\"informationDensity\":\"Medium\",\"caveats\":[]}" }],
              "model": "claude-haiku-4-5-20251001",
              "stop_reason": "end_turn",
              "usage": { "input_tokens": 100, "output_tokens": 20 }
            }
            """;
        var wrapper = CreateWrapper(fixture);
        var generator = new AnthropicAssessmentGenerator(wrapper);

        var result = await generator.GenerateAsync(TestSummary, SupportedFacts, GoodScore);

        Assert.Equal(WatchRecommendation.WatchWithCaution, result.Recommendation);
    }

    [Fact]
    public async Task GenerateAsync_NullCaveatsInResponse_ReturnsEmptyList()
    {
        const string fixture = """
            {
              "id": "msg_assess_nocaveats",
              "type": "message",
              "role": "assistant",
              "content": [{ "type": "text", "text": "{\"recommendation\":\"Watch\",\"reasoning\":\"Good content.\",\"informationDensity\":\"High\",\"caveats\":null}" }],
              "model": "claude-haiku-4-5-20251001",
              "stop_reason": "end_turn",
              "usage": { "input_tokens": 100, "output_tokens": 20 }
            }
            """;
        var wrapper = CreateWrapper(fixture);
        var generator = new AnthropicAssessmentGenerator(wrapper);

        var result = await generator.GenerateAsync(TestSummary, SupportedFacts, GoodScore);

        Assert.Empty(result.Caveats);
    }
}
