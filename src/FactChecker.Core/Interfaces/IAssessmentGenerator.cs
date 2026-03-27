using FactChecker.Core.Models;

namespace FactChecker.Core.Interfaces;

public interface IAssessmentGenerator
{
    Task<Assessment> GenerateAsync(
        Summary summary,
        IReadOnlyList<FactCheck> factChecks,
        ScoreBreakdown score,
        CancellationToken ct = default);
}
