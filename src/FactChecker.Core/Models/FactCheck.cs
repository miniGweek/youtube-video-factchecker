using FactChecker.Core.Enums;

namespace FactChecker.Core.Models;

public record FactCheck(
    string ClaimId,
    Verdict Verdict,
    Confidence Confidence,
    string Reasoning,
    IReadOnlyList<Source> Sources);
