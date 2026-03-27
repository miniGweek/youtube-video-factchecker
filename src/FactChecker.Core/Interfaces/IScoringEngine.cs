using FactChecker.Core.Enums;
using FactChecker.Core.Models;

namespace FactChecker.Core.Interfaces;

public interface IScoringEngine
{
    ScoreBreakdown Calculate(
        IReadOnlyList<Claim> claims,
        IReadOnlyList<FactCheck> factChecks,
        ContentDomain domain,
        TranscriptQuality transcriptQuality);
}
