namespace FactChecker.Core.Events;

public abstract record AnalysisEvent(string AnalysisId, DateTimeOffset Timestamp);
