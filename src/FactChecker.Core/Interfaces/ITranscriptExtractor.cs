using FactChecker.Core.Models;

namespace FactChecker.Core.Interfaces;

public interface ITranscriptExtractor
{
    Task<Transcript> ExtractAsync(string videoId, CancellationToken ct = default);
}
