using FactChecker.Infrastructure.YouTube;

namespace FactChecker.Infrastructure.Tests.YouTube;

public class YouTubeUrlValidatorTests
{
    // ── valid URL formats ────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("http://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PLxxx")]
    [InlineData("dQw4w9WgXcQ")]  // bare video ID
    public void TryParseVideoId_ValidUrl_ReturnsTrueAndExtractsId(string url)
    {
        var result = YouTubeUrlValidator.TryParseVideoId(url, out var videoId);

        Assert.True(result);
        Assert.Equal("dQw4w9WgXcQ", videoId);
    }

    // ── invalid URL formats ──────────────────────────────────────────────────

    [Theory]
    [InlineData("https://www.vimeo.com/123456")]
    [InlineData("https://www.example.com")]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData("https://youtube.com")]          // no video ID
    [InlineData("https://youtube.com/channel/UCxxx")] // channel URL
    public void TryParseVideoId_InvalidUrl_ReturnsFalse(string url)
    {
        var result = YouTubeUrlValidator.TryParseVideoId(url, out var videoId);

        Assert.False(result);
        Assert.Equal(string.Empty, videoId);
    }

    // ── IsVideoId ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("dQw4w9WgXcQ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("short", false)]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ", true)] // also accepted
    public void IsVideoId_VariousInputs_ReturnsExpected(string value, bool expected)
    {
        Assert.Equal(expected, YouTubeUrlValidator.IsVideoId(value));
    }

    // ── edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void TryParseVideoId_UrlWithTimestamp_ExtractsCorrectId()
    {
        var url = "https://youtu.be/dQw4w9WgXcQ?t=30";

        var result = YouTubeUrlValidator.TryParseVideoId(url, out var videoId);

        Assert.True(result);
        Assert.Equal("dQw4w9WgXcQ", videoId);
    }

    [Fact]
    public void TryParseVideoId_PlaylistUrlWithVideoId_ExtractsVideoId()
    {
        var url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PLxxx&index=2";

        var result = YouTubeUrlValidator.TryParseVideoId(url, out var videoId);

        Assert.True(result);
        Assert.Equal("dQw4w9WgXcQ", videoId);
    }
}
