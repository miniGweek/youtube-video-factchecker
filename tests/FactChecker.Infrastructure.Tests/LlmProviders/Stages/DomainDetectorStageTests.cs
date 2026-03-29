using FactChecker.Core.Enums;
using FactChecker.Core.Options;
using FactChecker.Infrastructure.LlmProviders.Stages;

namespace FactChecker.Infrastructure.Tests.LlmProviders.Stages;

public class DomainDetectorStageTests
{
    [Fact]
    public async Task DetectAsync_ScienceResponse_ReturnsScienceDomain()
    {
        var client = new StubLlmClient()
            .WithCompleteResponse("""{"domain":"Science"}""");
        var detector = new DomainDetectorStage(client, StageTestHelper.CreateOptions());

        var result = await detector.DetectAsync("Physics and quantum mechanics explained...");

        Assert.Equal(ContentDomain.Science, result);
    }

    [Fact]
    public async Task DetectAsync_HealthResponseInMarkdownFence_ReturnsHealthDomain()
    {
        var client = new StubLlmClient()
            .WithCompleteResponse("```json\n{\"domain\":\"Health\"}\n```");
        var detector = new DomainDetectorStage(client, StageTestHelper.CreateOptions());

        var result = await detector.DetectAsync("Today we discuss nutrition and vitamins...");

        Assert.Equal(ContentDomain.Health, result);
    }

    [Fact]
    public async Task DetectAsync_UnrecognisedDomain_FallsBackToGeneral()
    {
        var client = new StubLlmClient()
            .WithCompleteResponse("""{"domain":"NotARealDomain"}""");
        var detector = new DomainDetectorStage(client, StageTestHelper.CreateOptions());

        var result = await detector.DetectAsync("Some transcript content...");

        Assert.Equal(ContentDomain.General, result);
    }

    [Fact]
    public async Task DetectAsync_LongTranscript_TruncatesBeforeSending()
    {
        var longTranscript = string.Join(" ", Enumerable.Range(1, 2000).Select(i => $"word{i}"));
        var client = new StubLlmClient()
            .WithCompleteResponse("""{"domain":"Science"}""");
        var detector = new DomainDetectorStage(client, StageTestHelper.CreateOptions());

        var result = await detector.DetectAsync(longTranscript);

        Assert.Equal(ContentDomain.Science, result);
        Assert.NotNull(client.LastRequest);
        var wordCount = client.LastRequest.UserPrompt.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(wordCount <= 1000, $"Expected at most 1000 words but got {wordCount}");
    }

    [Fact]
    public async Task DetectAsync_CancellationRequested_ThrowsOperationCancelled()
    {
        var client = new StubLlmClient()
            .WithCompleteResponse("""{"domain":"Science"}""");
        var detector = new DomainDetectorStage(client, StageTestHelper.CreateOptions());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => detector.DetectAsync("transcript", cts.Token));
    }

    [Fact]
    public async Task DetectAsync_UsesCorrectModelTier()
    {
        var options = StageTestHelper.CreateOptions(new StageModelOptions { DomainDetection = ModelTier.Premium });
        var client = new StubLlmClient()
            .WithCompleteResponse("""{"domain":"Science"}""");
        var detector = new DomainDetectorStage(client, options);

        await detector.DetectAsync("transcript...");

        Assert.NotNull(client.LastRequest);
        Assert.Equal(ModelTier.Premium, client.LastRequest.Tier);
    }

    [Fact]
    public async Task DetectAsync_SetsCorrectStageId()
    {
        var client = new StubLlmClient()
            .WithCompleteResponse("""{"domain":"General"}""");
        var detector = new DomainDetectorStage(client, StageTestHelper.CreateOptions());

        await detector.DetectAsync("transcript...");

        Assert.NotNull(client.LastRequest);
        Assert.Equal("DomainDetection", client.LastRequest.StageId);
    }
}
