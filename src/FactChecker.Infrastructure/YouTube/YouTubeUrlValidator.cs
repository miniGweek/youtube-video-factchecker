using YoutubeExplode.Videos;

namespace FactChecker.Infrastructure.YouTube;

public static class YouTubeUrlValidator
{
    /// <summary>
    /// Returns true if the string is a recognised YouTube video URL or bare video ID,
    /// and extracts the video ID. Accepts youtube.com/watch?v=, youtu.be/, m.youtube.com/watch?v=,
    /// and bare 11-character video IDs.
    /// </summary>
    public static bool TryParseVideoId(string? urlOrId, out string videoId)
    {
        if (urlOrId is null)
        {
            videoId = string.Empty;
            return false;
        }

        var parsed = VideoId.TryParse(urlOrId);
        if (parsed.HasValue)
        {
            videoId = parsed.Value.Value;
            return true;
        }

        videoId = string.Empty;
        return false;
    }

    /// <summary>
    /// Returns true if the URI is a recognised YouTube video URL and extracts the video ID.
    /// </summary>
    public static bool TryParseVideoId(Uri? url, out string videoId) =>
        TryParseVideoId(url?.OriginalString, out videoId);

    /// <summary>
    /// Returns true if the string is a bare 11-character YouTube video ID or a valid YouTube URL.
    /// </summary>
    public static bool IsVideoId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && VideoId.TryParse(value).HasValue;
}
