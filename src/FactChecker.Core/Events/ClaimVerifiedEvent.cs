using FactChecker.Core.Models;

namespace FactChecker.Core.Events;

public record ClaimVerifiedEvent(string AnalysisId, DateTimeOffset Timestamp, FactCheck FactCheck)
    : AnalysisEvent(AnalysisId, Timestamp);
