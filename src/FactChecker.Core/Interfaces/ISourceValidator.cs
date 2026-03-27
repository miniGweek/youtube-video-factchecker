using FactChecker.Core.Models;

namespace FactChecker.Core.Interfaces;

public interface ISourceValidator
{
    Task<Source> ValidateAsync(Source source, CancellationToken ct = default);
}
