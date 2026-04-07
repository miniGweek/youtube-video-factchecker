using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FactChecker.Core.Events;
using FactChecker.Core.Interfaces;
using FactChecker.Core.Pipeline;

namespace FactChecker.Infrastructure.Events;

/// <summary>
/// In-process event transport backed by <see cref="System.Threading.Channels"/>.
/// Creates one unbounded channel per analysis ID. The channel is completed (closed for writing)
/// when <see cref="Complete"/> is called by the pipeline.
/// Completed channels are removed after all subscribers drain.
/// Implements <see cref="IAnalysisEventSink"/>, <see cref="IAnalysisEventSource"/>,
/// and <see cref="IAnalysisEventCompleter"/>.
/// </summary>
public sealed class ChannelEventTransport : IAnalysisEventSink, IAnalysisEventSource, IAnalysisEventCompleter
{
    private readonly ConcurrentDictionary<string, Channel<AnalysisEvent>> _channels = new();
    private readonly ConcurrentDictionary<string, int> _subscriberCounts = new();

    private Channel<AnalysisEvent> GetOrCreateChannel(string analysisId) =>
        _channels.GetOrAdd(analysisId, static _ => Channel.CreateUnbounded<AnalysisEvent>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }));

    public async Task PublishAsync(AnalysisEvent analysisEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(analysisEvent);
        var channel = GetOrCreateChannel(analysisEvent.AnalysisId);
        await channel.Writer.WriteAsync(analysisEvent, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<AnalysisEvent> SubscribeAsync(
        string analysisId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisId);
        var channel = GetOrCreateChannel(analysisId);
        _subscriberCounts.AddOrUpdate(analysisId, 1, static (_, count) => count + 1);

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return evt;
        }
        finally
        {
            var remaining = _subscriberCounts.AddOrUpdate(analysisId, 0, static (_, count) => count - 1);
            if (remaining <= 0 && channel.Reader.Completion.IsCompleted)
            {
                _channels.TryRemove(analysisId, out _);
                _subscriberCounts.TryRemove(analysisId, out _);
            }
        }
    }

    public void Complete(string analysisId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisId);
        // GetOrCreate ensures the channel exists so that a subscriber arriving AFTER
        // Complete() sees an already-completed channel and yields zero items.
        var channel = GetOrCreateChannel(analysisId);
        channel.Writer.TryComplete();
        // Cleanup deferred to SubscribeAsync finally block — removing here would
        // cause late subscribers to get a new, never-completed channel.
    }
}
