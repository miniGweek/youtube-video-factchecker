using FactChecker.Core.Enums;

namespace FactChecker.Core.Models;

public class AnalysisResult
{
    private readonly List<FactCheck> _factChecks = new();

    public string Id { get; init; }
    public AnalysisStatus Status { get; private set; }
    public VideoInfo? Video { get; private set; }
    public Transcript? Transcript { get; private set; }
    public Summary? Summary { get; private set; }
    public IReadOnlyList<Claim>? Claims { get; private set; }
    public IReadOnlyList<FactCheck> FactChecks => _factChecks.AsReadOnly();
    public ScoreBreakdown? Score { get; private set; }
    public Assessment? Assessment { get; private set; }
    public AnalysisError? Error { get; private set; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public AnalysisResult(string id)
    {
        Id = id;
        Status = AnalysisStatus.Submitted;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void SetVideo(VideoInfo video)
    {
        ArgumentNullException.ThrowIfNull(video);
        Video = video;
        Status = AnalysisStatus.Extracting;
    }

    public void SetTranscript(Transcript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        if (Status is not (AnalysisStatus.Submitted or AnalysisStatus.Extracting))
            throw new InvalidOperationException($"Cannot set transcript in status {Status}.");
        Transcript = transcript;
        Status = AnalysisStatus.Analysing;
    }

    public void SetSummary(Summary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (Status is not AnalysisStatus.Analysing)
            throw new InvalidOperationException($"Cannot set summary in status {Status}.");
        Summary = summary;
    }

    public void SetClaims(IReadOnlyList<Claim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        if (Status is not AnalysisStatus.Analysing)
            throw new InvalidOperationException($"Cannot set claims in status {Status}.");
        Claims = claims;
        Status = AnalysisStatus.FactChecking;
    }

    public void AddFactCheck(FactCheck factCheck)
    {
        ArgumentNullException.ThrowIfNull(factCheck);
        if (Status is not AnalysisStatus.FactChecking)
            throw new InvalidOperationException($"Cannot add fact check in status {Status}.");
        _factChecks.Add(factCheck);
    }

    public void SetScore(ScoreBreakdown score)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (Status is not AnalysisStatus.FactChecking)
            throw new InvalidOperationException($"Cannot set score in status {Status}.");
        Score = score;
        Status = AnalysisStatus.Scoring;
    }

    public void SetAssessment(Assessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (Status is not AnalysisStatus.Scoring)
            throw new InvalidOperationException($"Cannot set assessment in status {Status}.");
        Assessment = assessment;
        Status = AnalysisStatus.Complete;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Complete()
    {
        if (Status is not (AnalysisStatus.Scoring or AnalysisStatus.FactChecking))
            throw new InvalidOperationException($"Cannot complete analysis in status {Status}.");
        Status = AnalysisStatus.Complete;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(AnalysisStage stage, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Error = new AnalysisError(stage, message, Recoverable: false);
        Status = AnalysisStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
