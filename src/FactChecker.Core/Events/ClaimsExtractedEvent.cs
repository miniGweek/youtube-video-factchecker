using FactChecker.Core.Models;

namespace FactChecker.Core.Events;

public record ClaimsExtractedEvent(string AnalysisId, DateTimeOffset Timestamp, IReadOnlyList<Claim> Claims)
    : AnalysisEvent(AnalysisId, Timestamp);
