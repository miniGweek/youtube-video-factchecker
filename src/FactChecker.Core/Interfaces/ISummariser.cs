using FactChecker.Core.Enums;
using FactChecker.Core.Models;

namespace FactChecker.Core.Interfaces;

public interface ISummariser
{
    Task<Summary> SummariseAsync(string transcript, ContentDomain domain, CancellationToken ct = default);
}
