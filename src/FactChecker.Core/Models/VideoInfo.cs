namespace FactChecker.Core.Models;

public record VideoInfo(
    Uri Url,
    string VideoId,
    string Title,
    string Channel,
    TimeSpan Duration,
    Uri? ThumbnailUrl);
