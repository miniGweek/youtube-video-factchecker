namespace FactChecker.Infrastructure.YouTube;

public sealed class TranscriptNotAvailableException : Exception
{
    public string VideoId { get; }

    public TranscriptNotAvailableException()
        : this("unknown") { }

    public TranscriptNotAvailableException(string videoId)
        : base($"No captions are available for video '{videoId}'. The video may not have manual or auto-generated captions.")
    {
        VideoId = videoId;
    }

    public TranscriptNotAvailableException(string videoId, Exception innerException)
        : base(
            $"No captions are available for video '{videoId}'. The video may not have manual or auto-generated captions.",
            innerException)
    {
        VideoId = videoId;
    }
}
