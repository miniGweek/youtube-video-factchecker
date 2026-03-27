using FactChecker.Core.Models;

namespace FactChecker.Core.Events;

public record SummaryCompleteEvent(string AnalysisId, DateTimeOffset Timestamp, Summary Summary)
    : AnalysisEvent(AnalysisId, Timestamp);
