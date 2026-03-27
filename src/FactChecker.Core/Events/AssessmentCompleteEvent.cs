using FactChecker.Core.Models;

namespace FactChecker.Core.Events;

public record AssessmentCompleteEvent(string AnalysisId, DateTimeOffset Timestamp, Assessment Assessment)
    : AnalysisEvent(AnalysisId, Timestamp);
