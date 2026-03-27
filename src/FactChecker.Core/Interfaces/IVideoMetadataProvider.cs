using FactChecker.Core.Models;

namespace FactChecker.Core.Interfaces;

public interface IVideoMetadataProvider
{
    Task<VideoInfo> GetMetadataAsync(Uri videoUri, CancellationToken ct = default);
}
