namespace FactChecker.Core.Models;

public record ScoreBreakdown(
    double AccuracyScore,
    double SourceQualityScore,
    double VerifiabilityScore,
    double AggregateScore,
    string ScoreMethod);
