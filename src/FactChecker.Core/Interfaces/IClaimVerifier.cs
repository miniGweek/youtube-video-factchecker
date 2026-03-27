using FactChecker.Core.Enums;
using FactChecker.Core.Models;

namespace FactChecker.Core.Interfaces;

public interface IClaimVerifier
{
    Task<FactCheck> VerifyAsync(
        Claim claim, Summary summary, ContentDomain domain, CancellationToken ct = default);
}
