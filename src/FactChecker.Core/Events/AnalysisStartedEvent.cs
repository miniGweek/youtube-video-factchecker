using FactChecker.Core.Models;

namespace FactChecker.Core.Events;

public record AnalysisStartedEvent(string AnalysisId, DateTimeOffset Timestamp, VideoInfo Video)
    : AnalysisEvent(AnalysisId, Timestamp);
