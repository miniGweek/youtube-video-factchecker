using System.Collections.Concurrent;
using FactChecker.Core.Models;
using FactChecker.Core.Pipeline;

namespace FactChecker.Infrastructure.Storage;

/// <summary>
/// Thread-safe in-memory store for <see cref="AnalysisResult"/> instances.
/// Backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>; no persistence across restarts.
/// </summary>
public sealed class InMemoryAnalysisStore : IAnalysisStore
{
    private readonly ConcurrentDictionary<string, AnalysisResult> _results = new();

    public void Add(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _results[result.Id] = result;
    }

    public AnalysisResult? TryGet(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _results.TryGetValue(id, out var result) ? result : null;
    }
}
