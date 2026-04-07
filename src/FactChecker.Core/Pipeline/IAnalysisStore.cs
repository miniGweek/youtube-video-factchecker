using FactChecker.Core.Models;

namespace FactChecker.Core.Pipeline;

public interface IAnalysisStore
{
    void Add(AnalysisResult result);
    AnalysisResult? TryGet(string id);

    /// <summary>
    /// Returns the ID of an in-progress analysis for the given video, or null if none exists.
    /// </summary>
    string? TryGetActiveByVideoId(string videoId);

    /// <summary>
    /// Associates a video ID with an analysis ID so duplicate submissions can be detected.
    /// </summary>
    void TrackVideoId(string videoId, string analysisId);
}
