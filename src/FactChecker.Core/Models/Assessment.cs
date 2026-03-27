using FactChecker.Core.Enums;

namespace FactChecker.Core.Models;

public record Assessment(
    WatchRecommendation Recommendation,
    string Reasoning,
    string InformationDensity,
    IReadOnlyList<string> Caveats);
