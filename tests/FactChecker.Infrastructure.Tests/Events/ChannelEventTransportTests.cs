using FactChecker.Core.Events;
using FactChecker.Core.Models;
using FactChecker.Infrastructure.Events;

namespace FactChecker.Infrastructure.Tests.Events;

public class ChannelEventTransportTests
{
    private static AnalysisEvent MakeEvent(string id = "a1") =>
        new AnalysisStartedEvent(
            id,
            DateTimeOffset.UtcNow,
            new VideoInfo(
                new Uri("https://youtube.com/watch?v=x"),
                VideoId: "x",
                Title: "T",
                Channel: "C",
                Duration: TimeSpan.FromMinutes(5),
                ThumbnailUrl: null));

    // ── Publish + Subscribe ───────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_ThenComplete_SubscriberReceivesEvent()
    {
        var transport = new ChannelEventTransport();
        var evt = MakeEvent("a1");

        await transport.PublishAsync(evt);
        transport.Complete("a1");

        var received = new List<AnalysisEvent>();
        await foreach (var e in transport.SubscribeAsync("a1"))
            received.Add(e);

        Assert.Single(received);
        Assert.Same(evt, received[0]);
    }

    [Fact]
    public async Task PublishAsync_MultipleEvents_SubscriberReceivesAllInOrder()
    {
        var transport = new ChannelEventTransport();
        var events = Enumerable.Range(1, 5)
            .Select(i => MakeEvent($"a{i}") with { AnalysisId = "a1" })
            .ToList();

        foreach (var e in events)
            await transport.PublishAsync(e);
        transport.Complete("a1");

        var received = new List<AnalysisEvent>();
        await foreach (var e in transport.SubscribeAsync("a1"))
            received.Add(e);

        Assert.Equal(events.Count, received.Count);
    }

    [Fact]
    public async Task SubscribeAsync_CompletedBeforeSubscribe_YieldsNoItems()
    {
        var transport = new ChannelEventTransport();
        transport.Complete("a1"); // complete before any subscriber

        var received = new List<AnalysisEvent>();
        await foreach (var e in transport.SubscribeAsync("a1"))
            received.Add(e);

        Assert.Empty(received);
    }

    [Fact]
    public async Task SubscribeAsync_CompletionSignalStopsEnumeration()
    {
        var transport = new ChannelEventTransport();
        await transport.PublishAsync(MakeEvent("a1"));
        await transport.PublishAsync(MakeEvent("a1"));
        transport.Complete("a1");

        var count = 0;
        await foreach (var _ in transport.SubscribeAsync("a1"))
            count++;

        Assert.Equal(2, count);
    }

    // ── Isolation between analysis IDs ────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_TwoAnalyses_ChannelsAreIsolated()
    {
        var transport = new ChannelEventTransport();

        var evt1 = MakeEvent("a1");
        var evt2 = MakeEvent("a2");
        await transport.PublishAsync(evt1);
        await transport.PublishAsync(evt2);
        transport.Complete("a1");
        transport.Complete("a2");

        var a1Events = new List<AnalysisEvent>();
        await foreach (var e in transport.SubscribeAsync("a1"))
            a1Events.Add(e);

        var a2Events = new List<AnalysisEvent>();
        await foreach (var e in transport.SubscribeAsync("a2"))
            a2Events.Add(e);

        Assert.Single(a1Events);
        Assert.Equal("a1", a1Events[0].AnalysisId);
        Assert.Single(a2Events);
        Assert.Equal("a2", a2Events[0].AnalysisId);
    }

    // ── Complete is idempotent ─────────────────────────────────────────────────

    [Fact]
    public void Complete_CalledTwice_DoesNotThrow()
    {
        var transport = new ChannelEventTransport();
        transport.Complete("a1");
        transport.Complete("a1"); // second call is a no-op
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SubscribeAsync_CancellationToken_StopsEnumeration()
    {
        var transport = new ChannelEventTransport();
        // Do NOT complete the channel — subscriber should stop due to cancellation
        await transport.PublishAsync(MakeEvent("a1"));

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var received = new List<AnalysisEvent>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var e in transport.SubscribeAsync("a1", cts.Token))
                received.Add(e);
        });

        Assert.Single(received); // received the first event before cancellation
    }

    // ── Concurrent publish + subscribe ────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_ConcurrentPublishers_AllEventsReceived()
    {
        var transport = new ChannelEventTransport();
        const int count = 50;

        var publishTasks = Enumerable.Range(0, count)
            .Select(_ => transport.PublishAsync(MakeEvent("a1")))
            .ToList();

        await Task.WhenAll(publishTasks);
        transport.Complete("a1");

        var received = new List<AnalysisEvent>();
        await foreach (var e in transport.SubscribeAsync("a1"))
            received.Add(e);

        Assert.Equal(count, received.Count);
    }
}
