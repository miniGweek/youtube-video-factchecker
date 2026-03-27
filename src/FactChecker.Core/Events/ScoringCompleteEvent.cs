using FactChecker.Core.Models;

namespace FactChecker.Core.Events;

public record ScoringCompleteEvent(string AnalysisId, DateTimeOffset Timestamp, ScoreBreakdown Score)
    : AnalysisEvent(AnalysisId, Timestamp);
