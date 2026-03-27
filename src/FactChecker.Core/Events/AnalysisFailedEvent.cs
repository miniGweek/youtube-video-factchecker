using FactChecker.Core.Models;

namespace FactChecker.Core.Events;

public record AnalysisFailedEvent(string AnalysisId, DateTimeOffset Timestamp, AnalysisError Error)
    : AnalysisEvent(AnalysisId, Timestamp);
