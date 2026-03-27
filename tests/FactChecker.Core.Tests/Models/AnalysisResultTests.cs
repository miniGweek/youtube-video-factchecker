using FactChecker.Core.Enums;
using FactChecker.Core.Models;

namespace FactChecker.Core.Tests.Models;

public class AnalysisResultTests
{
    private static VideoInfo CreateVideoInfo() => new(
        Url: new Uri("https://youtube.com/watch?v=test123"),
        VideoId: "test123",
        Title: "Test Video",
        Channel: "Test Channel",
        Duration: TimeSpan.FromMinutes(10),
        ThumbnailUrl: null);

    private static Transcript CreateTranscript() => new(
        Text: "Test transcript text",
        Quality: TranscriptQuality.Manual,
        WordCount: 3);

    private static Summary CreateSummary() => new(
        Thesis: "Test thesis",
        KeyPoints: new[] { "Point 1", "Point 2" },
        Domain: ContentDomain.General);

    private static Claim CreateClaim(int importance = 3) => new(
        Id: Guid.NewGuid().ToString(),
        Text: "Test claim",
        Context: "Test context",
        Importance: importance);

    private static FactCheck CreateFactCheck(string claimId) => new(
        ClaimId: claimId,
        Verdict: Verdict.Supported,
        Confidence: Confidence.High,
        Reasoning: "Test reasoning",
        Sources: Array.Empty<Source>());

    private static ScoreBreakdown CreateScore() => new(
        AccuracyScore: 80,
        SourceQualityScore: 90,
        VerifiabilityScore: 85,
        AggregateScore: 83,
        ScoreMethod: "v1.0-weighted");

    private static Assessment CreateAssessment() => new(
        Recommendation: WatchRecommendation.Watch,
        Reasoning: "Good video",
        InformationDensity: "High",
        Caveats: Array.Empty<string>());

    [Fact]
    public void NewAnalysisResult_HasSubmittedStatus()
    {
        var result = new AnalysisResult("test-id");

        Assert.Equal(AnalysisStatus.Submitted, result.Status);
        Assert.Equal("test-id", result.Id);
        Assert.Null(result.Video);
        Assert.Null(result.Transcript);
        Assert.Null(result.Error);
        Assert.Empty(result.FactChecks);
    }

    [Fact]
    public void SetVideo_TransitionsToExtractingStatus()
    {
        var result = new AnalysisResult("test-id");
        result.SetVideo(CreateVideoInfo());

        Assert.Equal(AnalysisStatus.Extracting, result.Status);
        Assert.NotNull(result.Video);
    }

    [Fact]
    public void SetTranscript_TransitionsToAnalysingStatus()
    {
        var result = new AnalysisResult("test-id");
        result.SetVideo(CreateVideoInfo());
        result.SetTranscript(CreateTranscript());

        Assert.Equal(AnalysisStatus.Analysing, result.Status);
        Assert.NotNull(result.Transcript);
    }

    [Fact]
    public void SetClaims_TransitionsToFactCheckingStatus()
    {
        var result = new AnalysisResult("test-id");
        result.SetVideo(CreateVideoInfo());
        result.SetTranscript(CreateTranscript());
        result.SetSummary(CreateSummary());
        result.SetClaims(new[] { CreateClaim() });

        Assert.Equal(AnalysisStatus.FactChecking, result.Status);
        Assert.NotNull(result.Claims);
    }

    [Fact]
    public void AddFactCheck_CanBeCalledMultipleTimes()
    {
        var result = new AnalysisResult("test-id");
        result.SetVideo(CreateVideoInfo());
        result.SetTranscript(CreateTranscript());
        result.SetSummary(CreateSummary());
        var claim1 = CreateClaim();
        var claim2 = CreateClaim();
        result.SetClaims(new[] { claim1, claim2 });

        result.AddFactCheck(CreateFactCheck(claim1.Id));
        result.AddFactCheck(CreateFactCheck(claim2.Id));

        Assert.Equal(2, result.FactChecks.Count);
    }

    [Fact]
    public void SetScore_TransitionsToScoringStatus()
    {
        var result = new AnalysisResult("test-id");
        result.SetVideo(CreateVideoInfo());
        result.SetTranscript(CreateTranscript());
        result.SetSummary(CreateSummary());
        result.SetClaims(new[] { CreateClaim() });
        result.SetScore(CreateScore());

        Assert.Equal(AnalysisStatus.Scoring, result.Status);
        Assert.NotNull(result.Score);
    }

    [Fact]
    public void SetAssessment_TransitionsToCompleteStatus()
    {
        var result = new AnalysisResult("test-id");
        result.SetVideo(CreateVideoInfo());
        result.SetTranscript(CreateTranscript());
        result.SetSummary(CreateSummary());
        result.SetClaims(new[] { CreateClaim() });
        result.SetScore(CreateScore());
        result.SetAssessment(CreateAssessment());

        Assert.Equal(AnalysisStatus.Complete, result.Status);
        Assert.NotNull(result.Assessment);
        Assert.NotNull(result.CompletedAt);
    }

    [Fact]
    public void Fail_TransitionsToFailedStatus()
    {
        var result = new AnalysisResult("test-id");
        result.Fail(AnalysisStage.TranscriptExtraction, "No captions available");

        Assert.Equal(AnalysisStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
        Assert.Equal(AnalysisStage.TranscriptExtraction, result.Error.Stage);
        Assert.Equal("No captions available", result.Error.Message);
        Assert.NotNull(result.CompletedAt);
    }

    [Fact]
    public void SetTranscript_WhenAlreadyComplete_ThrowsInvalidOperationException()
    {
        var result = new AnalysisResult("test-id");
        result.SetVideo(CreateVideoInfo());
        result.SetTranscript(CreateTranscript());
        result.SetSummary(CreateSummary());
        result.SetClaims(new[] { CreateClaim() });
        result.SetScore(CreateScore());
        result.SetAssessment(CreateAssessment());

        Assert.Throws<InvalidOperationException>(() => result.SetTranscript(CreateTranscript()));
    }

    [Fact]
    public void AddFactCheck_WhenNotInFactCheckingStatus_ThrowsInvalidOperationException()
    {
        var result = new AnalysisResult("test-id");
        result.SetVideo(CreateVideoInfo());
        result.SetTranscript(CreateTranscript());
        result.SetSummary(CreateSummary());

        Assert.Throws<InvalidOperationException>(() => result.AddFactCheck(CreateFactCheck("claim-1")));
    }

    [Fact]
    public void SetVideo_WithNullVideo_ThrowsArgumentNullException()
    {
        var result = new AnalysisResult("test-id");

        Assert.Throws<ArgumentNullException>(() => result.SetVideo(null!));
    }

    [Fact]
    public void Fail_WithNullMessage_ThrowsArgumentException()
    {
        var result = new AnalysisResult("test-id");

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException (a subtype) for null
        Assert.ThrowsAny<ArgumentException>(() => result.Fail(AnalysisStage.Validation, null!));
    }

    [Fact]
    public void FactChecks_ReturnsReadOnlyView()
    {
        var result = new AnalysisResult("test-id");
        result.SetVideo(CreateVideoInfo());
        result.SetTranscript(CreateTranscript());
        result.SetSummary(CreateSummary());
        result.SetClaims(new[] { CreateClaim() });
        result.AddFactCheck(CreateFactCheck("claim-1"));

        Assert.IsAssignableFrom<IReadOnlyList<FactCheck>>(result.FactChecks);
    }

    [Fact]
    public void Complete_FromScoringStatus_TransitionsToComplete()
    {
        var result = new AnalysisResult("test-id");
        result.SetVideo(CreateVideoInfo());
        result.SetTranscript(CreateTranscript());
        result.SetSummary(CreateSummary());
        result.SetClaims(Array.Empty<Claim>());
        result.SetScore(CreateScore());
        result.Complete();

        Assert.Equal(AnalysisStatus.Complete, result.Status);
        Assert.NotNull(result.CompletedAt);
    }

    [Fact]
    public void SetSummary_InAnalysingStatus_SetsSummary()
    {
        var result = new AnalysisResult("test-id");
        result.SetVideo(CreateVideoInfo());
        result.SetTranscript(CreateTranscript());
        result.SetSummary(CreateSummary());

        Assert.NotNull(result.Summary);
        Assert.Equal("Test thesis", result.Summary.Thesis);
    }
}
