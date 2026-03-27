using FactChecker.Core.Interfaces;
using FactChecker.Core.Models;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;

namespace FactChecker.Infrastructure.YouTube;

public sealed class YouTubeMetadataProvider : IVideoMetadataProvider
{
    private readonly YoutubeClient _youtubeClient;

    public YouTubeMetadataProvider(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        var httpClient = httpClientFactory.CreateClient(nameof(YouTubeMetadataProvider));
        _youtubeClient = new YoutubeClient(httpClient);
    }

    public async Task<VideoInfo> GetMetadataAsync(Uri videoUri, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(videoUri);

        var videoId = VideoId.TryParse(videoUri.OriginalString)
            ?? throw new ArgumentException($"'{videoUri}' is not a valid YouTube video URL.", nameof(videoUri));

        var video = await _youtubeClient.Videos.GetAsync(videoId, ct).ConfigureAwait(false);

        var thumbnailUrl = video.Thumbnails
            .MaxBy(t => t.Resolution.Area)
            ?.Url is string url
            ? new Uri(url)
            : null;

        return new VideoInfo(
            Url: videoUri,
            VideoId: video.Id.Value,
            Title: video.Title,
            Channel: video.Author.ChannelTitle,
            Duration: video.Duration ?? TimeSpan.Zero,
            ThumbnailUrl: thumbnailUrl);
    }
}
