using FactChecker.Core.Enums;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;

namespace FactChecker.Core.Scoring;

public sealed class DefaultScoringEngine : IScoringEngine
{
    private const double AccuracyWeight = 0.60;
    private const double SourceQualityWeight = 0.20;
    private const double VerifiabilityWeight = 0.15;
    private const double TranscriptQualityWeight = 0.05;
    private const double ManualTranscriptBonus = 100.0;
    private const double WorstClaimPenaltyPerClaim = 15.0;
    private const double MaxWorstClaimPenalty = 30.0;
    private const int HighImportanceThreshold = 4;
    private const string ScoreMethodVersion = "v1.0-weighted";

    public ScoreBreakdown Calculate(
        IReadOnlyList<Claim> claims,
        IReadOnlyList<FactCheck> factChecks,
        ContentDomain domain,
        TranscriptQuality transcriptQuality)
    {
        var claimById = claims.ToDictionary(c => c.Id);

        // Rule R3: NotAClaim verdicts are excluded from all scoring
        var scorablePairs = factChecks
            .Where(f => f.Verdict != Verdict.NotAClaim)
            .Select(f => (FactCheck: f, Claim: claimById.GetValueOrDefault(f.ClaimId)))
            .Where(p => p.Claim is not null)
            .Select(p => (p.FactCheck, Claim: p.Claim!))
            .ToList();

        double accuracy = CalculateAccuracy(scorablePairs);
        double sourceQuality = CalculateSourceQuality(scorablePairs);
        double verifiability = CalculateVerifiability(scorablePairs);
        double transcriptBonus = transcriptQuality == TranscriptQuality.Manual ? ManualTranscriptBonus : 0.0;

        double aggregate =
            AccuracyWeight * accuracy +
            SourceQualityWeight * sourceQuality +
            VerifiabilityWeight * verifiability +
            TranscriptQualityWeight * transcriptBonus;

        return new ScoreBreakdown(
            AccuracyScore: Clamp(accuracy),
            SourceQualityScore: Clamp(sourceQuality),
            VerifiabilityScore: Clamp(verifiability),
            AggregateScore: Clamp(aggregate),
            ScoreMethod: ScoreMethodVersion);
    }

    private static double CalculateAccuracy(
        List<(FactCheck FactCheck, Claim Claim)> scorablePairs)
    {
        if (scorablePairs.Count == 0) return 0;

        int totalImportance = scorablePairs.Sum(p => p.Claim.Importance);
        if (totalImportance == 0) return 0;

        double weightedScore = scorablePairs.Sum(p =>
        {
            double weight = (double)p.Claim.Importance / totalImportance;
            double verdictScore = p.FactCheck.Verdict switch
            {
                Verdict.Supported => 100.0,
                Verdict.PartiallySupported => 60.0,
                Verdict.Unverifiable => 40.0,
                Verdict.Refuted => 0.0,
                _ => 0.0
            };
            return weight * verdictScore;
        });

        // Rule R4: penalise worst claims — -15 per high-importance refuted claim, capped at -30
        int refutedHighImportance = scorablePairs.Count(p =>
            p.FactCheck.Verdict == Verdict.Refuted &&
            p.Claim.Importance >= HighImportanceThreshold);

        double penalty = Math.Min(
            refutedHighImportance * WorstClaimPenaltyPerClaim,
            MaxWorstClaimPenalty);

        return Math.Max(0, weightedScore - penalty);
    }

    private static double CalculateSourceQuality(
        List<(FactCheck FactCheck, Claim Claim)> scorablePairs)
    {
        var allSources = scorablePairs.SelectMany(p => p.FactCheck.Sources).ToList();
        if (allSources.Count == 0) return 0;
        return (double)allSources.Count(s => s.IsAccessible) / allSources.Count * 100.0;
    }

    private static double CalculateVerifiability(
        List<(FactCheck FactCheck, Claim Claim)> scorablePairs)
    {
        if (scorablePairs.Count == 0) return 0;
        int verifiable = scorablePairs.Count(p => p.FactCheck.Verdict != Verdict.Unverifiable);
        return (double)verifiable / scorablePairs.Count * 100.0;
    }

    private static double Clamp(double value) => Math.Clamp(value, 0.0, 100.0);
}
