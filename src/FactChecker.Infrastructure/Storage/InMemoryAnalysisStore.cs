using System.Collections.Concurrent;
using FactChecker.Core.Enums;
using FactChecker.Core.Models;
using FactChecker.Core.Options;
using FactChecker.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace FactChecker.Infrastructure.Storage;

/// <summary>
/// Thread-safe in-memory store for <see cref="AnalysisResult"/> instances.
/// A background timer evicts completed/failed entries older than
/// <see cref="AnalysisOptions.CompletedAnalysisRetentionMinutes"/>.
/// </summary>
public sealed class InMemoryAnalysisStore : IAnalysisStore, IDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, string> _videoIdToAnalysisId = new();
    private readonly Timer _evictionTimer;
    private readonly int _retentionMinutes;

    public InMemoryAnalysisStore(IOptions<AnalysisOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _retentionMinutes = options.Value.CompletedAnalysisRetentionMinutes;
        _evictionTimer = new Timer(Evict, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public void Add(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _entries[result.Id] = new Entry(result, DateTimeOffset.UtcNow);
    }

    public AnalysisResult? TryGet(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _entries.TryGetValue(id, out var entry) ? entry.Result : null;
    }

    public void TrackVideoId(string videoId, string analysisId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisId);
        _videoIdToAnalysisId[videoId] = analysisId;
    }

    public string? TryGetActiveByVideoId(string videoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        if (!_videoIdToAnalysisId.TryGetValue(videoId, out var analysisId))
            return null;

        // Only return if the analysis is still in progress
        if (_entries.TryGetValue(analysisId, out var entry)
            && entry.Result.Status is not (AnalysisStatus.Complete or AnalysisStatus.Failed))
        {
            return analysisId;
        }

        return null;
    }

    private void Evict(object? state)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_retentionMinutes);
        foreach (var (id, entry) in _entries)
        {
            if (entry.Result.Status is AnalysisStatus.Complete or AnalysisStatus.Failed
                && entry.CreatedAt <= cutoff)
            {
                _entries.TryRemove(id, out _);
                // Clean up videoId mapping for evicted entries
                if (entry.Result.Video is { } video)
                    _videoIdToAnalysisId.TryRemove(video.VideoId, out _);
            }
        }
    }

    public void Dispose()
    {
        _evictionTimer.Dispose();
    }

    private sealed record Entry(AnalysisResult Result, DateTimeOffset CreatedAt);
}
