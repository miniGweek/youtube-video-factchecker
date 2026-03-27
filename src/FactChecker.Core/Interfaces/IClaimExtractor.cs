using FactChecker.Core.Enums;
using FactChecker.Core.Models;

namespace FactChecker.Core.Interfaces;

public interface IClaimExtractor
{
    Task<IReadOnlyList<Claim>> ExtractAsync(
        string transcript, ContentDomain domain, int maxClaims, CancellationToken ct = default);
}
