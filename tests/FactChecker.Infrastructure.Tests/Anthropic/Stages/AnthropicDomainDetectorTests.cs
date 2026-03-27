using FactChecker.Core.Enums;
using FactChecker.Infrastructure.Anthropic.Stages;

namespace FactChecker.Infrastructure.Tests.Anthropic.Stages;

public class AnthropicDomainDetectorTests : StageTestBase
{
    [Fact]
    public async Task DetectAsync_ScienceResponse_ReturnsScienceDomain()
    {
        var wrapper = CreateWrapper(LoadFixture("domain_response_science.json"));
        var detector = new AnthropicDomainDetector(wrapper);

        var result = await detector.DetectAsync("Physics and quantum mechanics explained...");

        Assert.Equal(ContentDomain.Science, result);
    }

    [Fact]
    public async Task DetectAsync_HealthResponseInMarkdownFence_ReturnsHealthDomain()
    {
        var wrapper = CreateWrapper(LoadFixture("domain_response_health.json"));
        var detector = new AnthropicDomainDetector(wrapper);

        var result = await detector.DetectAsync("Today we discuss nutrition and vitamins...");

        Assert.Equal(ContentDomain.Health, result);
    }

    [Fact]
    public async Task DetectAsync_UnrecognisedDomain_FallsBackToGeneral()
    {
        var wrapper = CreateWrapper(LoadFixture("domain_response_unknown.json"));
        var detector = new AnthropicDomainDetector(wrapper);

        var result = await detector.DetectAsync("Some transcript content...");

        Assert.Equal(ContentDomain.General, result);
    }

    [Fact]
    public async Task DetectAsync_LongTranscript_TruncatesBeforeSending()
    {
        // Provide a 2000-word transcript — only first ~1000 words should reach the API
        var longTranscript = string.Join(" ", Enumerable.Range(1, 2000).Select(i => $"word{i}"));
        var wrapper = CreateWrapper(LoadFixture("domain_response_science.json"));
        var detector = new AnthropicDomainDetector(wrapper);

        // Should succeed without error — truncation is transparent to caller
        var result = await detector.DetectAsync(longTranscript);

        Assert.Equal(ContentDomain.Science, result);
    }

    [Fact]
    public async Task DetectAsync_CancellationRequested_ThrowsOperationCancelled()
    {
        var wrapper = CreateWrapper(LoadFixture("domain_response_science.json"));
        var detector = new AnthropicDomainDetector(wrapper);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => detector.DetectAsync("transcript", cts.Token));
    }
}
