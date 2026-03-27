using FactChecker.Core.Enums;

namespace FactChecker.Core.Events;

public record DomainDetectedEvent(string AnalysisId, DateTimeOffset Timestamp, ContentDomain Domain)
    : AnalysisEvent(AnalysisId, Timestamp);
