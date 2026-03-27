using FactChecker.Core.Events;

namespace FactChecker.Core.Interfaces;

public interface IAnalysisEventSink
{
    Task PublishAsync(AnalysisEvent analysisEvent, CancellationToken ct = default);
}
