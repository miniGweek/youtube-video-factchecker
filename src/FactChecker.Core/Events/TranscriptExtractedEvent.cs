using FactChecker.Core.Enums;

namespace FactChecker.Core.Events;

public record TranscriptExtractedEvent(
    string AnalysisId,
    DateTimeOffset Timestamp,
    TranscriptQuality Quality,
    int WordCount,
    string Text)
    : AnalysisEvent(AnalysisId, Timestamp);
