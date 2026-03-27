using FactChecker.Core.Enums;

namespace FactChecker.Core.Interfaces;

public interface IDomainDetector
{
    Task<ContentDomain> DetectAsync(string transcriptSnippet, CancellationToken ct = default);
}
