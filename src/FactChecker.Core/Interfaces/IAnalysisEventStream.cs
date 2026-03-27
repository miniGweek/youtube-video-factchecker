using FactChecker.Core.Events;

namespace FactChecker.Core.Interfaces;

public interface IAnalysisEventSource
{
    IAsyncEnumerable<AnalysisEvent> SubscribeAsync(string analysisId, CancellationToken ct = default);
}
